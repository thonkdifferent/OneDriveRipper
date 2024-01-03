using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.Graph;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace OneDriveRipper.Graph
{
    public class OneDriveHandler
    {
        public struct FileInfo
        {
            public List<DriveItem> Files;
            public List<DriveItem> Directories;
            public bool IsRoot;
            public string Name;
        }

        public struct DownloadInfo
        {
            public string Id;
            public string Path;
            public DriveItem item;
        }
        public static async Task<FileInfo> ParseGraphData(GraphServiceClient graphServiceClient, string id="", string name="#ROOT#")
        {
            FileInfo fileInfo;
            fileInfo.Files = new List<DriveItem>();
            fileInfo.Directories = new List<DriveItem>();
            fileInfo.Name = name;
            fileInfo.IsRoot = false;
            try
            {
                IDriveItemChildrenCollectionPage folderData;
                folderData = (id == ""
                    ? await graphServiceClient.Me.Drive.Root.Children
                        .Request().GetAsync()
                    : await graphServiceClient.Me.Drive.Items[id].Children
                        .Request().GetAsync());
                var pageIterator = PageIterator<DriveItem>.CreatePageIterator(graphServiceClient,
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

                        if (id == "")
                            fileInfo.IsRoot = true;
                        else
                            fileInfo.IsRoot = false;
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

        public static async Task Download(DriveItem item, GraphServiceClient graphServiceClient, string path)
        {
            const long defaultChunkSize = 32768 * 1024; // 50 KB, TODO: change chunk size to make it realistic for a large file.
            long chunkSize = defaultChunkSize;
            long offset = 0;         // cursor location for updating the Range header.
            byte[] bytesInStream; 
            // We'll use the file metadata to determine size and the name of the downloaded file
            // and to get the download URL.
            var driveItemInfo = await graphServiceClient.Me.Drive.Items[item.Id].Request().GetAsync();
            object downloadUrl;
            try
            {
                // Get the download URL. This URL is preauthenticated and has a short TTL.
                
                driveItemInfo.AdditionalData.TryGetValue("@microsoft.graph.downloadUrl", out downloadUrl);
            }
            catch (ArgumentNullException e)
            {
                return;
            }

            // Get the number of bytes to download. calculate the number of chunks and determine
            // the last chunk size.
            long size = (long)driveItemInfo.Size;
            int numberOfChunks = Convert.ToInt32(size / defaultChunkSize); 
            // We are incrementing the offset cursor after writing the response stream to a file after each chunk. 
            // Subtracting one since the size is 1 based, and the range is 0 base. There should be a better way to do
            // this but I haven't spent the time on that.
            int lastChunkSize = Convert.ToInt32(size % defaultChunkSize) - numberOfChunks - 1; 
            if (lastChunkSize > 0) { numberOfChunks++; }

            // Create a file stream to contain the downloaded file.
            using (FileStream fileStream = System.IO.File.Create((path)))
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
                    HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, (string)downloadUrl);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, chunkSize + offset);

                    // We can use the the client library to send this although it does add an authentication cost.
                    // HttpResponseMessage response = await graphClient.HttpProvider.SendAsync(req);
                    // Since the download URL is preauthenticated, and we aren't deserializing objects, 
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

        public static string ProcessGraphPath(string path)
        {
            try
            {
                return System.Web.HttpUtility.UrlDecode(path.Substring(13));
            }
            catch (ArgumentOutOfRangeException)
            {
                return "";
            }
        }
        public static async Task GetFiles(GraphServiceClient graphServiceClient,string rootPath)
        {
            Stack<FileInfo> directories = new Stack<FileInfo>();
            List<DownloadInfo> anyErrorFiles = new List<DownloadInfo>();
            directories.Push(await ParseGraphData(graphServiceClient));
            if(!rootPath.EndsWith('/'))
                rootPath += "/";
            while (directories.Count > 0)
            {
                FileInfo currentDir = directories.Pop();
                foreach (DriveItem directory in currentDir.Directories)
                {
                    string parentPath = ProcessGraphPath(directory.ParentReference.Path);
                    if (!(parentPath==""))
                        parentPath += '/';
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
                    directories.Push(await ParseGraphData(graphServiceClient,directory.Id,directory.Name));
                }
                foreach (DriveItem file in currentDir.Files)
                {
                    string parentPath = ProcessGraphPath(file.ParentReference.Path);
                    if (!(parentPath==""))
                        parentPath += '/';
                    if (!File.Exists(rootPath + parentPath + file.Name))
                    {
                        Console.WriteLine($"Downloading {rootPath + parentPath + file.Name}");
                        try
                        {
                            await Download(file, graphServiceClient, rootPath + parentPath + file.Name);
                            Console.WriteLine("Done. Waiting 1 second before continuing");
                        }
                        catch (Exception e)
                        {
                            DownloadInfo downloadInfo = new DownloadInfo();
                            downloadInfo.Id = file.Id;
                            downloadInfo.Path = rootPath + parentPath + file.Name;
                            downloadInfo.item = file;
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
                    await Download(file.item, graphServiceClient, file.Path);
                    Console.WriteLine("Done. Waiting 5 seconds before continuing");
                }
                catch (Exception e)
                {
                    DownloadInfo downloadInfo = new DownloadInfo();
                    downloadInfo.Id = file.Id;
                    downloadInfo.Path = file.Path;
                    downloadInfo.item = file.item;
                    anyErrorFiles.Add(downloadInfo);
                    File.Delete(downloadInfo.Path);
                    Console.WriteLine("Couldn't download file. Saving for later...");
                }
                Thread.Sleep(1000);
                
            }

        }
    }
}