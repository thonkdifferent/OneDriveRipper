using System;
using System.IO;

namespace OneDriveRipper;

public sealed class GlobalConfiguration
{
    public bool VerifyDownload
    {
        get => _verifyDownload;
        set => _verifyDownload = value;
    }

    public int MaxDownloadJobs
    {
        get => _maxDownloadJobs;
        set => _maxDownloadJobs = value;
    }

    public bool DoParalelDownload
    {
        get => _doParalelDownload;
        set => _doParalelDownload = value;
    }

    public int MaxTryAgainFailover
    {
        get => _maxTryAgainFailover;
        set => _maxTryAgainFailover = value;
    }

    public long MaximumBytesPerSecond
    {
        get => _maximumBytesPerSecond;
        set => _maximumBytesPerSecond = 1024*1024*value;
    }

    public long MaximumBufferSize
    {
        get => _maximumBufferSize;
        set => _maximumBufferSize = 1024*1024*value;
    }

    public string LogLocation
    {
        get => _logLocation;
    }
    
    private GlobalConfiguration()
    {
    }
    private static readonly Lazy<GlobalConfiguration> Lazy = new Lazy<GlobalConfiguration>(() => new GlobalConfiguration());
    private bool _verifyDownload = true;
    private readonly string _logLocation = $"{Path.GetTempPath()}OneDriveRipper{Path.DirectorySeparatorChar}";
    private int _maxDownloadJobs = Environment.ProcessorCount;
    private bool _doParalelDownload = true;
    private int _maxTryAgainFailover = 5;
    private long _maximumBytesPerSecond = 0; //unlimited
    private long _maximumBufferSize = 1024 * 1024 * 50; //50MB
    public static GlobalConfiguration Instance => Lazy.Value;
}