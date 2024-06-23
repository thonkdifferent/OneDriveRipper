using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Downloader;

namespace OneDriveRipper.Graph;

internal enum DownloadStatus
{
    NotStarted,
    InProgress,
    Finished,
    Failed
}
internal class DownloadTask
{
    internal DownloadStatus Status { get; private set; }
    private string? _link = null;
    private DownloadConfiguration _options;
    private DriveItem _item;
    private string _path = "";
    private double? _lastPercentage;
    internal DownloadTask(DownloadConfiguration configuration, GraphServiceClient _client, Drive _userDrive, DriveItem item)
    {
        Status = DownloadStatus.NotStarted;
        _item = item;
        var linkTask = GetDownloadUrl(_client, _userDrive);
        linkTask.Wait();
        _link = linkTask.Result;
        _options = configuration;

    }
        
    private async Task<string?> GetDownloadUrl(GraphServiceClient graphServiceClient, Drive userDrive)
    {
        var driveItemInfo = await graphServiceClient.Drives[userDrive.Id].Items[_item.Id].GetAsync();
        if (driveItemInfo == null)
            throw new NullReferenceException(
                $"Could not get the file information for id {_item.Id}. This could be caused by an invalid ID or a network issue");
            
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

    private void OnDownloadStarted(object? sender, DownloadStartedEventArgs args)
    {
        Console.WriteLine($"Downloading {_item.Name} - {args.TotalBytesToReceive.CalcMemoryMensurableUnit()} total");
        Status = DownloadStatus.InProgress;
    }

    private void OnDownloadProgressChanged(object? sender, DownloadProgressChangedEventArgs args)
    {
        double percentageDoubleDec = Math.Truncate(args.ProgressPercentage * 100) / 100;
        if(percentageDoubleDec == _lastPercentage)
            return;
        Console.WriteLine($"[{args.ActiveChunks} jobs total] - {percentageDoubleDec}% completed. Average speed: {args.AverageBytesPerSecondSpeed.CalcMemoryMensurableUnit()}");
        _lastPercentage = percentageDoubleDec;
    }
    
    internal async Task Start(string path)
    {
        //OneNote files cannot be downloaded via MSGraph directly due to them not having a downloadLink property. This causes a NRE, so we skip these files until we implement proper OneNote downloads (pdf?)
        if(string.IsNullOrEmpty(_link))
            return;
        _path = path;
        //Start downloading
        
        var downloader = new DownloadService(_options);

        downloader.DownloadStarted += OnDownloadStarted;
        downloader.DownloadProgressChanged += OnDownloadProgressChanged;
        downloader.DownloadFileCompleted += OnDownloadCompleted;

        await downloader.DownloadFileTaskAsync(_link, _path);
    }

    private void OnDownloadCompleted(object? sender, AsyncCompletedEventArgs e)
    {
        if (e.Error != null || e.Cancelled)
        {
            Status = DownloadStatus.Failed;
            return;
        }

        Status = DownloadStatus.Finished;


        if(!CheckHash(_item, _path)) return;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Success");
        Console.ResetColor();
            
    }

    private static bool AreHashesPresent(DriveItem item)
    {
        if (item.File == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine($"[WARN] File {item.Name} has no file metadata. This download cannot be verified");
            Console.ResetColor();
            return false;
        }

        if (item.File.Hashes == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine($"[WARN] File {item.Name} has no hashes. This download cannot be verified");
            Console.ResetColor();
            return false;
        }

        return true;
    }

    internal static bool CheckHash(DriveItem item, string path)
    {
        Console.WriteLine("Verifying download");
        if (!AreHashesPresent(item)) return false;
        using var sha256Checker = SHA256.Create();
        FileInfo info = new FileInfo(path);
        using (FileStream fileStream = info.OpenRead())
        {
            fileStream.Position = 0;
            if (item.File!.Hashes!.Sha256Hash != null)
            {
                Console.WriteLine("Checking SHA256 hash");
                byte[] hashValue = sha256Checker.ComputeHash(fileStream);
                string hashValueStr = Convert.ToHexString(hashValue);
                if ( hashValueStr != item.File.Hashes.Sha256Hash)
                {
                    File.Delete(path);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"SHA256 Hashes for path {path} do not match.\n Expected:{item.File.Hashes.Sha256Hash}\n Got: {hashValueStr}");
                    Console.ResetColor();
                    return false;
                }
                
                Console.WriteLine("SHA256 Check succeeded");
                return true;
            }
            //TODO: Add CRC32 hash
        }

        return false;
    }

}