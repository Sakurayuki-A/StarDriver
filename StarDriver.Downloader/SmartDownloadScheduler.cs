using StarDriver.Core.Models;
using System.Collections.Concurrent;

namespace StarDriver.Downloader;

/// <summary>
/// 智能下载调度器 - 优化下载速度和稳定性
/// </summary>
public class SmartDownloadScheduler
{
    private const long LargeFileThreshold = 50 * 1024 * 1024; // 50MB
    private const long MediumFileThreshold = 5 * 1024 * 1024;  // 5MB
    
    private readonly ConcurrentQueue<DownloadItem> _largeFiles = new();
    private readonly ConcurrentQueue<DownloadItem> _mediumFiles = new();
    private readonly ConcurrentQueue<DownloadItem> _smallFiles = new();
    
    public int LargeFileCount => _largeFiles.Count;
    public int MediumFileCount => _mediumFiles.Count;
    public int SmallFileCount => _smallFiles.Count;
    public int TotalCount => LargeFileCount + MediumFileCount + SmallFileCount;
    
    /// <summary>
    /// 添加下载任务并自动分类
    /// </summary>
    public void Enqueue(List<DownloadItem> items)
    {
        // 按文件大小分类
        var largeFiles = new List<DownloadItem>();
        var mediumFiles = new List<DownloadItem>();
        var smallFiles = new List<DownloadItem>();
        
        foreach (var item in items)
        {
            if (item.PatchInfo.FileSize >= LargeFileThreshold)
                largeFiles.Add(item);
            else if (item.PatchInfo.FileSize >= MediumFileThreshold)
                mediumFiles.Add(item);
            else
                smallFiles.Add(item);
        }
        
        // 大文件：按大小降序（优先下载最大的）
        foreach (var item in largeFiles.OrderByDescending(x => x.PatchInfo.FileSize))
            _largeFiles.Enqueue(item);
        
        // 中等文件：按大小降序
        foreach (var item in mediumFiles.OrderByDescending(x => x.PatchInfo.FileSize))
            _mediumFiles.Enqueue(item);
        
        // 小文件：保持原顺序（批量处理）
        foreach (var item in smallFiles)
            _smallFiles.Enqueue(item);
        
        Console.WriteLine($"[智能调度] 大文件: {LargeFileCount}, 中等: {MediumFileCount}, 小文件: {SmallFileCount}");
    }
    
    private int _dequeueCounter = 0; // 轮询计数器
    
    /// <summary>
    /// 从大文件队列获取任务
    /// </summary>
    public bool TryDequeueLarge(out DownloadItem? item)
    {
        return _largeFiles.TryDequeue(out item);
    }
    
    /// <summary>
    /// 从中等文件队列获取任务
    /// </summary>
    public bool TryDequeueMedium(out DownloadItem? item)
    {
        return _mediumFiles.TryDequeue(out item);
    }
    
    /// <summary>
    /// 从小文件队列获取任务
    /// </summary>
    public bool TryDequeueSmall(out DownloadItem? item)
    {
        return _smallFiles.TryDequeue(out item);
    }
    
    /// <summary>
    /// 智能获取下一个下载任务（轮询策略，已废弃，使用分层并发）
    /// </summary>
    [Obsolete("使用 TryDequeueLarge/Medium/Small 代替")]
    public bool TryDequeue(out DownloadItem? item)
    {
        _dequeueCounter++;
        
        // 轮询策略：4大 → 1中 → 1小 → 4大 → 1中 → 1小 ...
        // 这样保持 67% 大文件，16.5% 中等，16.5% 小文件
        var cycle = _dequeueCounter % 6;
        
        if (cycle < 4) // 前4次取大文件
        {
            if (_largeFiles.TryDequeue(out item)) return true;
            // 大文件用完，降级到中等
            if (_mediumFiles.TryDequeue(out item)) return true;
            // 中等也用完，降级到小文件
            if (_smallFiles.TryDequeue(out item)) return true;
        }
        else if (cycle == 4) // 第5次取中等文件
        {
            if (_mediumFiles.TryDequeue(out item)) return true;
            // 中等用完，尝试大文件
            if (_largeFiles.TryDequeue(out item)) return true;
            // 都用完，取小文件
            if (_smallFiles.TryDequeue(out item)) return true;
        }
        else // 第6次取小文件
        {
            if (_smallFiles.TryDequeue(out item)) return true;
            // 小文件用完，尝试大文件
            if (_largeFiles.TryDequeue(out item)) return true;
            // 都用完，取中等
            if (_mediumFiles.TryDequeue(out item)) return true;
        }
        
        item = null;
        return false;
    }
    
    /// <summary>
    /// 检查是否还有任务
    /// </summary>
    public bool IsEmpty => _largeFiles.IsEmpty && _mediumFiles.IsEmpty && _smallFiles.IsEmpty;
    
    /// <summary>
    /// 重新入队失败的任务
    /// </summary>
    public void RequeueFailedItem(DownloadItem item)
    {
        // 根据文件大小重新分类入队
        if (item.PatchInfo.FileSize >= LargeFileThreshold)
            _largeFiles.Enqueue(item);
        else if (item.PatchInfo.FileSize >= MediumFileThreshold)
            _mediumFiles.Enqueue(item);
        else
            _smallFiles.Enqueue(item);
    }
}
