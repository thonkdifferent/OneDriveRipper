using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace OneDriveRipper.Graph
{
    public class OneDriveHandler
    {

        private GraphServiceClient _graphServiceClient;
        private Drive _userDrive;
        public struct FileInfo
        {
            public List<DriveItem> Files;
            public List<DriveItem> Directories;
        }

        
        public struct DownloadInfo
        {
            public string Id;
            public string Path;
            public DriveItem Item;
        }
        public async Task<FileInfo> ParseGraphData(GraphServiceClient graphServiceClient, string id="root", string name="#ROOT#")
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

        public async Task Download(DriveItem item, string path)
        {
            const long defaultChunkSize = 32768 * 1024; // 50 KB, TODO: change chunk size to make it realistic for a large file.
            long chunkSize = defaultChunkSize;
            long offset = 0;         // cursor location for updating the Range header.
            byte[] bytesInStream; 
            // We'll use the file metadata to determine size and the name of the downloaded file
            // and to get the download URL.
            var driveItemInfo = await _graphServiceClient.Drives[_userDrive.Id].Items[item.Id].GetAsync();
            if (driveItemInfo == null)
                throw new NullReferenceException(
                    $"Could not get the file information for id {item.Id}. This could be caused by an invalid ID or a network issue");
            string downloadUrl;
            try
            {
                // Get the download URL. This URL is preauthenticated and has a short TTL.
                object? rawUrl;
                driveItemInfo.AdditionalData.TryGetValue("@microsoft.graph.downloadUrl", out rawUrl);
                if (rawUrl == null) throw new ArgumentNullException();

                downloadUrl = (string)rawUrl;
            }
            catch (ArgumentNullException)
            {
                return;
            }

            // Get the number of bytes to download. calculate the number of chunks and determine
            // the last chunk size.
            if(driveItemInfo.Size == null) return;
            long size = (long)driveItemInfo.Size;
            int numberOfChunks = Convert.ToInt32(size / defaultChunkSize); 
            // We are incrementing the offset cursor after writing the response stream to a file after each chunk. 
            // Subtracting one since the size is 1 based, and the range is 0 base. There should be a better way to do
            // this but I haven't spent the time on that.
            int lastChunkSize = Convert.ToInt32(size % defaultChunkSize) - numberOfChunks - 1; 
            if (lastChunkSize > 0) { numberOfChunks++; }

            // Create a file stream to contain the downloaded file.
            using (FileStream fileStream = File.Create((path)))
            {
                for (int i = 0; i < numberOfChunks; i++)
                {
                    Console.WriteLine($"Chunk {i+1}/{numberOfChunks}");
                    // Setup the last chunk to request. This will be called at the end of this loop.
                    if (i == numberOfChunks - 1)
                    {
                        chunkSize = lastChunkSize;
                    }

                    // Create the request message with the download URL and Range header.
                    HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, chunkSize + offset);

                    // We can use the client library to send this, although it does add an authentication cost.
                    // HttpResponseMessage response = await graphClient.HttpProvider.SendAsync(req);
                    // Since the download URL is pre-authenticated, and we aren't deserializing objects, 
                    // we'd be better to make the request with HttpClient.
                    var client = new HttpClient();
                    HttpResponseMessage response = await client.SendAsync(req);

                    using (Stream responseStream = await response.Content.ReadAsStreamAsync())
                    {
                        bytesInStream = new byte[chunkSize];
                        int read;
                        do
                        {
                            read = responseStream.Read(bytesInStream, 0, bytesInStream.Length);
                            if (read > 0)
                                fileStream.Write(bytesInStream, 0, bytesInStream.Length);
                        }
                        while (read > 0);
                    }
                    offset += chunkSize + 1; // Move the offset cursor to the next chunk.
                }
            }
        }

        public OneDriveHandler(GraphServiceClient client)
        {
            _graphServiceClient = client;
            var driveTask = _graphServiceClient.Me.Drive.GetAsync();
            Console.WriteLine("Getting current drive id. This may take a while depending on your network connection");
            driveTask.RunSynchronously();
            if (driveTask.Result == null)
            {
                throw new NullReferenceException("Could not retrieve drive information");
            }
            _userDrive = driveTask.Result;
        }
        public static string ProcessGraphPath(string? path)
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
                    string parentPath;
                    if (directory.ParentReference == null)
                        parentPath = "";
                    else
                        parentPath = ProcessGraphPath(directory.ParentReference.Path);
                    if (parentPath != "")
                        parentPath += Path.PathSeparator;
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
                    directories.Push(await ParseGraphData(_graphServiceClient,directory.Id,directory.Name));
                }
                foreach (DriveItem file in currentDir.Files)
                {
                    string parentPath = ProcessGraphPath(file.ParentReference.Path);
                    if (parentPath != "")
                        parentPath += Path.PathSeparator;
                    if (!File.Exists(rootPath + parentPath + file.Name))
                    {
                        Console.WriteLine($"Downloading {rootPath + parentPath + file.Name}");
                        try
                        {
                            await Download(file, rootPath + parentPath + file.Name);
                            Console.WriteLine("Done. Waiting 1 second before continuing");
                        }
                        catch (Exception e)
                        {
                            DownloadInfo downloadInfo = new DownloadInfo();
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
                catch (Exception e)
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
    }
}