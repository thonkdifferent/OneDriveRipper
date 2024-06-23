using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Downloader;
using System.Threading;

using Microsoft.Graph;
using Microsoft.Graph.Models;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace OneDriveRipper.Graph
{
   
    public class OneDriveHandler
    {

        private readonly GraphServiceClient _graphServiceClient;
        private readonly Drive _userDrive;
        private readonly DownloadConfiguration _configuration;
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
        private async Task<FileInfo> ParseGraphData(GraphServiceClient graphServiceClient, string id="root")
        {
            FileInfo fileInfo;
            fileInfo.Files = new List<DriveItem>();
            fileInfo.Directories = new List<DriveItem>();

            try
            {
                DriveItemCollectionResponse? folderData = await graphServiceClient.Drives[_userDrive.Id].Items[id].Children.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select =
                        ["id", "@microsoft.graph.downloadUrl", "name", "size", "file", "parentReference","folder"];
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
                            Console.WriteLine($"[FOLDER_DETECT] {item.Name}");
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




        private async Task<bool> Download(DriveItem item, string path)
        {
            DownloadTask task = new DownloadTask(_configuration, _graphServiceClient, _userDrive, item);
            await task.Start(path);
            return task.Status == DownloadStatus.Finished;
        }

        public OneDriveHandler(GraphServiceClient client)
        {
            _graphServiceClient = client;
            var driveTask = Task.Run(async () => await _graphServiceClient.Me.Drive.GetAsync());
            Console.WriteLine("Trying log-in");
            driveTask.Wait();
            if (driveTask.Result == null)
            {
                throw new NullReferenceException("Could not retrieve drive information");
            }
            _userDrive = driveTask.Result;

            _configuration = new DownloadConfiguration()
            {
                ChunkCount = Environment.ProcessorCount,
                ParallelDownload = true,
                MaxTryAgainOnFailover = 5,
                MaximumBytesPerSecond = 0,
                BufferBlockSize = 10240,
                MinimumSizeOfChunking = 1024,
                ClearPackageOnCompletionWithFailure = true
            };

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
                    directories.Push(await ParseGraphData(_graphServiceClient,directory.Id));
                }
                foreach (DriveItem file in currentDir.Files)
                {
                    var parentPath = GetParentPath(file);
                    var filePath = rootPath + parentPath + file.Name;
                    if (!File.Exists(filePath))
                    {
                        Console.WriteLine($"Downloading {filePath}");
                        
                        try
                        {
                            var result = await Download(file, filePath);
                            if (!result) throw new Exception("Download failed"); //TODO: Make this nicer
                            Console.WriteLine("Done. Waiting 1 second before continuing");
                        }
                        catch (Exception e)
                        {
                            await HandleDownloadError(file, filePath, anyErrorFiles, e);
                        }

                        Thread.Sleep(1000);
                    }
                    else
                    {
                        if (!DownloadTask.CheckHash(file, filePath))
                        {
                            await HandleDownloadError(file, filePath, anyErrorFiles);
                            continue;
                        }
                        Console.WriteLine($"File {filePath} already present. Skipping");
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
                    Console.WriteLine("Done. Waiting 1 second before continuing");
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

        private static async Task HandleDownloadError(DriveItem file, string filePath, List<DownloadInfo> anyErrorFiles, Exception? e = null)
        {
            DownloadInfo downloadInfo = new DownloadInfo();
            if (string.IsNullOrEmpty(file.Id))
            {
                await Console.Error.WriteLineAsync("Failed download has no id property");
                Thread.Sleep(1000);
                return;
            }
            downloadInfo.Id = file.Id;
            downloadInfo.Path = filePath;
            downloadInfo.Item = file;
            anyErrorFiles.Add(downloadInfo);
            if(e!=null)
                Console.WriteLine($"Couldn't download file. Saving for later... Error Data: {e.Message}");
            else
                Console.WriteLine("File hashes did not match. Saving for later...");
        }

        private string GetParentPath(DriveItem directory)
        {
            string parentPath;
            if (directory.ParentReference == null)
                parentPath = "";
            else
                parentPath = ProcessGraphPath(directory.ParentReference.Path);
            if (parentPath != "")
                parentPath += Path.DirectorySeparatorChar;
            return parentPath;
        }
    }
}