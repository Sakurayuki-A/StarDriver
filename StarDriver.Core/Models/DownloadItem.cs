namespace StarDriver.Core.Models;

/// <summary>
/// 下载任务项
/// </summary>
public sealed class DownloadItem
{
    /// <summary>补丁信息</summary>
    public PatchListItem PatchInfo { get; }
    
    /// <summary>本地目标路径</summary>
    public string LocalPath { get; }
    
    /// <summary>符号链接目标路径（如果适用）</summary>
    public string? SymlinkTarget { get; }
    
    /// <summary>下载状态</summary>
    public DownloadStatus Status { get; set; }
    
    /// <summary>已下载字节数</summary>
    public long DownloadedBytes { get; set; }
    
    /// <summary>重试次数</summary>
    public int RetryCount { get; set; }
    
    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }

    public DownloadItem(PatchListItem patchInfo, string localPath, string? symlinkTarget = null)
    {
        PatchInfo = patchInfo ?? throw new ArgumentNullException(nameof(patchInfo));
        LocalPath = localPath ?? throw new ArgumentNullException(nameof(localPath));
        SymlinkTarget = symlinkTarget;
        Status = DownloadStatus.Pending;
    }

    /// <summary>下载进度百分比</summary>
    public double ProgressPercentage => 
        PatchInfo.FileSize > 0 ? (double)DownloadedBytes / PatchInfo.FileSize * 100 : 0;

    public override string ToString() => 
        $"{PatchInfo.GetFilenameWithoutAffix()} - {Status} ({ProgressPercentage:F1}%)";
}

/// <summary>
/// 下载状态
/// </summary>
public enum DownloadStatus
{
    Pending,
    Downloading,
    Verifying,
    Completed,
    Failed,
    Cancelled
}
