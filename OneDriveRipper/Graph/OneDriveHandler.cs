using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using OctaneEngineCore;
using System.Threading;
using Autofac;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace OneDriveRipper.Graph
{
    internal class DownloadTask(string link, string destinationPath)
    {
        internal enum DownloadStatus
        {
            NotStarted,
            InProgress,
            Finished,
            Failed
        }
        
        internal DownloadStatus Status { get; private set; } = DownloadStatus.NotStarted;

        internal async Task StartDownload(IEngine octaneEngine)
        {
            var pauseTokenSource = new PauseTokenSource();
            using var cancelTokenSource = new CancellationTokenSource();


            
            octaneEngine.SetProgressCallback(progress =>
            {
                Status = DownloadStatus.InProgress;
                Console.WriteLine($"{progress}% complete");
            });
            octaneEngine.SetDoneCallback(success =>
            {
                if (success) Status = DownloadStatus.Finished;
                else Status = DownloadStatus.Failed;
            });
            await octaneEngine.DownloadFile(link, destinationPath, pauseTokenSource, cancelTokenSource);
        }
    }
    public class OneDriveHandler
    {

        private readonly GraphServiceClient _graphServiceClient;
        private readonly Drive _userDrive;
        private uint _chunkSize;
        private readonly IEngine _octaneEngine;
        public uint ChunkSize
        {
            get => _chunkSize;
            set
            {
                if (value == 0)
                    throw new ArgumentException("Chunks cannot be 0MB in size");
                _chunkSize = (uint)1 << (int)(value - 1) * 1024;
            }
        }

        public struct FileInfo
        {
            public List<DriveItem> Files;
            public List<DriveItem> Directories;
        }

        
        private struct DownloadInfo
        {
            public string Id;
            public string Path;
            public DriveItem Item;
        }
        private async Task<FileInfo> ParseGraphData(GraphServiceClient graphServiceClient, string id="root", string name="#ROOT#")
        {
            FileInfo fileInfo;
            fileInfo.Files = new List<DriveItem>();
            fileInfo.Directories = new List<DriveItem>();

            try
            {
                DriveItemCollectionResponse? folderData = await graphServiceClient.Drives[_userDrive.Id].Items[id].Children.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select =
                        ["id", "@microsoft.graph.downloadUrl", "name", "size", "file", "parentReference"];
                });
                if (folderData == null) throw new ArgumentNullException(nameof(folderData),$"Could not retrieve folder data for ID {id}. This could mean the ID corresponds to a file or that a network error occured. Please try again later. If that does not work, please report this issue on GitHub");
                var pageIterator = PageIterator<DriveItem,DriveItemCollectionResponse>.CreatePageIterator(graphServiceClient,
                    folderData,
                    (item) =>
                    {
                        if (item.Folder == null)
                        {
                            fileInfo.Files.Add(item);
                        }
                        else
                        {
                            //Console.WriteLine($"[FOLDER_DETECT] {item.Name}");
                            fileInfo.Directories.Add(item);
                        }
                        return true;
                    });
                await pageIterator.IterateAsync();

            }
            catch (ServiceException ex)
            {
                Console.WriteLine($"Error getting signed-in user: {ex.Message}");
            }
            return fileInfo;
        }

        private async Task Download(DriveItem item, string path)
        {
            var link = await GetDownloadUrl(item);
            if(string.IsNullOrEmpty(link))
                return;
            var task = new DownloadTask(link, path);
            await task.StartDownload(_octaneEngine);
            if (task.Status == DownloadTask.DownloadStatus.Failed)
                throw new Exception($"Could not download file {item.Name}");

            if (item.File == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                await Console.Error.WriteLineAsync($"[WARN] File {item.Name} has no file metadata. This download cannot be verified");
                Console.ResetColor();
                return;
            }

            if (item.File.Hashes == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                await Console.Error.WriteLineAsync($"[WARN] File {item.Name} has no hashes. This download cannot be verified");
                Console.ResetColor();
                return;
            }
            
            
            Console.WriteLine("Verifying download");
            using var sha256Checker = SHA256.Create();
            using var sha1Checker = SHA1.Create();
            System.IO.FileInfo info = new System.IO.FileInfo(path);
            await using (FileStream fileStream = info.OpenRead())
            {
                fileStream.Position = 0;
                if (item.File.Hashes.Sha256Hash != null)
                {
                    Console.WriteLine("Checking SHA256 hash");
                    byte[] hashValue = await sha256Checker.ComputeHashAsync(fileStream);
                    string hashValueStr = BitConverter.ToString(hashValue);
                    if ( hashValueStr != item.File.Hashes.Sha256Hash)
                    {
                        File.Delete(path);
                        throw new Exception($"SHA256 Hashes for path {path} do not match.\n Expected:{item.File.Hashes.Sha256Hash}\n Got: {hashValueStr}");
                    }
                    
                    Console.WriteLine("SHA256 Check succeeded");
                }

                if (item.File.Hashes.Sha1Hash != null)
                {
                    Console.WriteLine("Checking SHA1 hash");
                    byte[] hashValue = await sha1Checker.ComputeHashAsync(fileStream);
                    string hashValueStr = BitConverter.ToString(hashValue);
                    if ( hashValueStr != item.File.Hashes.Sha256Hash)
                    {
                        File.Delete(path);
                        throw new Exception($"SHA1 Hashes for path {path} do not match.\n Expected:{item.File.Hashes.Sha256Hash}\n Got: {hashValueStr}");
                    }
                    
                    Console.WriteLine("SHA1 Check succeeded");
                }
                //TODO: Add CRC32 hash
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Success");
            Console.ResetColor();
        }   
        

        private async Task<string?> GetDownloadUrl(DriveItem item)
        {
            var driveItemInfo = await _graphServiceClient.Drives[_userDrive.Id].Items[item.Id].GetAsync();
            if (driveItemInfo == null)
                throw new NullReferenceException(
                    $"Could not get the file information for id {item.Id}. This could be caused by an invalid ID or a network issue");
            
            try
            {
                // Get the download URL. This URL is pre-authenticated and has a short TTL.
                object? rawUrl;
                driveItemInfo.AdditionalData.TryGetValue("@microsoft.graph.downloadUrl", out rawUrl);
                return (string?)rawUrl;
            }
            catch (ArgumentNullException)
            {
                return null;
            }
        }

        public OneDriveHandler(GraphServiceClient client)
        {
            _graphServiceClient = client;
            var driveTask = Task.Run(async () => await _graphServiceClient.Me.Drive.GetAsync());
            Console.WriteLine("Getting current drive id. This may take a while depending on your network connection");
            driveTask.Wait();
            if (driveTask.Result == null)
            {
                throw new NullReferenceException("Could not retrieve drive information");
            }
            _userDrive = driveTask.Result;
            
            var containerBuilder = new ContainerBuilder();
            containerBuilder.AddOctane();
            var engineContainer = containerBuilder.Build();
             _octaneEngine = engineContainer.Resolve<IEngine>();
        }
        private static string ProcessGraphPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return "";
            try
            {
                return System.Web.HttpUtility.UrlDecode(path.Substring(13));
            }
            catch (ArgumentOutOfRangeException)
            {
                return "";
            }
        }
        public async Task GetFiles(string rootPath)
        {
            Stack<FileInfo> directories = new Stack<FileInfo>();
            List<DownloadInfo> anyErrorFiles = new List<DownloadInfo>();
            directories.Push(await ParseGraphData(_graphServiceClient));
            if(!rootPath.EndsWith('/'))
                rootPath += "/";
            while (directories.Count > 0)
            {
                FileInfo currentDir = directories.Pop();
                foreach (DriveItem directory in currentDir.Directories)
                {
                    var parentPath = GetParentPath(directory);
                    if (!Directory.Exists(rootPath + parentPath + directory.Name))
                    {
                        Console.WriteLine($"parentPath {parentPath}");
                        Console.WriteLine(
                            $"Creating directory \"{directory.Name}\" in {rootPath + parentPath + directory.Name}");
                        Directory.CreateDirectory(rootPath + parentPath + directory.Name);
                    }
                    else
                    {
                        Console.WriteLine($"Directory {rootPath + parentPath + directory.Name} already present. Skipping");
                    }

                    if (directory.Id == null || directory.Name == null)
                    {
                        throw new NullReferenceException("A directory has no name or no id property. This could be a network issue.");
                    }
                    directories.Push(await ParseGraphData(_graphServiceClient,directory.Id,directory.Name));
                }
                foreach (DriveItem file in currentDir.Files)
                {
                    var parentPath = GetParentPath(file);
                    if (!File.Exists(rootPath + parentPath + file.Name))
                    {
                        Console.WriteLine($"Downloading {rootPath + parentPath + file.Name}");
                        try
                        {
                            await Download(file, rootPath + parentPath + file.Name);
                            Console.WriteLine("Done. Waiting 1 second before continuing");
                        }
                        catch (Exception)
                        {
                            DownloadInfo downloadInfo = new DownloadInfo();
                            if (string.IsNullOrEmpty(file.Id))
                            {
                                await Console.Error.WriteLineAsync("Failed download has no id property");
                                Thread.Sleep(1000);
                                continue;
                            }
                            downloadInfo.Id = file.Id;
                            downloadInfo.Path = rootPath + parentPath + file.Name;
                            downloadInfo.Item = file;
                            anyErrorFiles.Add(downloadInfo);
                            File.Delete(downloadInfo.Path);
                            Console.WriteLine("Couldn't download file. Saving for later...");
                        }

                        Thread.Sleep(1000);
                    }
                    else
                    {
                        Console.WriteLine($"File {rootPath + parentPath + file.Name} already present. Skipping");
                    }
                }
            }


            for (int i = 0; i < anyErrorFiles.Count;i++)
            {
                DownloadInfo file = anyErrorFiles[i];
                Console.WriteLine($"Downloading {file.Path}");
                try
                {
                    await Download(file.Item, file.Path);
                    Console.WriteLine("Done. Waiting 5 seconds before continuing");
                }
                catch (Exception)
                {
                    DownloadInfo downloadInfo = new DownloadInfo();
                    downloadInfo.Id = file.Id;
                    downloadInfo.Path = file.Path;
                    downloadInfo.Item = file.Item;
                    anyErrorFiles.Add(downloadInfo);
                    File.Delete(downloadInfo.Path);
                    Console.WriteLine("Couldn't download file. Saving for later...");
                }
                Thread.Sleep(1000);
                
            }

        }

        private string GetParentPath(DriveItem directory)
        {
            string parentPath;
            if (directory.ParentReference == null)
                parentPath = "";
            else
                parentPath = ProcessGraphPath(directory.ParentReference.Path);
            if (parentPath != "")
                parentPath += Path.PathSeparator;
            return parentPath;
        }
    }
}