using StarDriver.Core.Models;

namespace StarDriver.Downloader;

/// <summary>
/// 下载节点 - 跟踪单个连接的状态和性能
/// </summary>
public sealed class DownloadNode
{
    public int NodeId { get; }
    public DownloadTask? CurrentTask { get; set; }
    public bool IsIdle => CurrentTask == null;
    
    // 速度统计
    private readonly Queue<SpeedSample> _speedSamples = new(10);
    private long _lastBytesDownloaded;
    private DateTime _lastSpeedUpdate = DateTime.UtcNow;
    
    public double CurrentSpeedBytesPerSecond { get; private set; }
    public double AverageSpeedBytesPerSecond { get; private set; }
    public NodeSpeedTier SpeedTier { get; private set; } = NodeSpeedTier.Unknown;
    
    // 性能统计
    public int CompletedTasks { get; private set; }
    public int FailedTasks { get; private set; }
    public int ConsecutiveSlowTasks { get; private set; }
    
    public DownloadNode(int nodeId)
    {
        NodeId = nodeId;
    }
    
    /// <summary>更新速度统计</summary>
    public void UpdateSpeed(long totalBytesDownloaded)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastSpeedUpdate).TotalSeconds;
        
        if (elapsed < 0.5) return; // 至少0.5秒更新一次
        
        var bytesDelta = totalBytesDownloaded - _lastBytesDownloaded;
        CurrentSpeedBytesPerSecond = bytesDelta / elapsed;
        
        // 记录样本
        _speedSamples.Enqueue(new SpeedSample(CurrentSpeedBytesPerSecond, now));
        if (_speedSamples.Count > 10)
            _speedSamples.Dequeue();
        
        // 计算平均速度（最近10个样本）
        if (_speedSamples.Count > 0)
        {
            AverageSpeedBytesPerSecond = _speedSamples.Average(s => s.Speed);
        }
        
        // 更新速度分级
        UpdateSpeedTier();
        
        _lastBytesDownloaded = totalBytesDownloaded;
        _lastSpeedUpdate = now;
    }
    
    /// <summary>任务完成</summary>
    public void OnTaskCompleted(bool success, bool wasSlow)
    {
        if (success)
        {
            CompletedTasks++;
            if (wasSlow)
            {
                ConsecutiveSlowTasks++;
            }
            else
            {
                ConsecutiveSlowTasks = 0; // 重置慢任务计数
            }
        }
        else
        {
            FailedTasks++;
            ConsecutiveSlowTasks++;
        }
        
        CurrentTask = null;
        _lastBytesDownloaded = 0;
        
        // 如果连续3次慢任务，降级
        if (ConsecutiveSlowTasks >= 3 && SpeedTier > NodeSpeedTier.Slow)
        {
            SpeedTier = NodeSpeedTier.Slow;
            Console.WriteLine($"[Node {NodeId}] 降级为慢节点（连续{ConsecutiveSlowTasks}次慢任务）");
        }
    }
    
    /// <summary>重置速度统计（用于升级节点）</summary>
    public void ResetSlowCounter()
    {
        ConsecutiveSlowTasks = 0;
    }
    
    private void UpdateSpeedTier()
    {
        var speedKBps = AverageSpeedBytesPerSecond / 1024.0;
        
        var newTier = speedKBps switch
        {
            > 800 => NodeSpeedTier.Fast,
            > 400 => NodeSpeedTier.Medium,
            > 0 => NodeSpeedTier.Slow,
            _ => NodeSpeedTier.Unknown
        };
        
        // 如果从慢升级到快，重置慢任务计数
        if (newTier > SpeedTier && SpeedTier == NodeSpeedTier.Slow)
        {
            ResetSlowCounter();
            Console.WriteLine($"[Node {NodeId}] 升级为{newTier}节点（速度: {speedKBps:F1} KB/s）");
        }
        
        SpeedTier = newTier;
    }
    
    private record SpeedSample(double Speed, DateTime Timestamp);
}

/// <summary>节点速度分级</summary>
public enum NodeSpeedTier
{
    Unknown = 0,
    Slow = 1,      // < 400 KB/s
    Medium = 2,    // 400-800 KB/s
    Fast = 3       // > 800 KB/s
}

/// <summary>下载任务 - 可以是完整文件或文件块</summary>
public sealed class DownloadTask
{
    public DownloadItem Item { get; }
    public long RangeStart { get; }
    public long RangeEnd { get; }
    public long TaskSize => RangeEnd - RangeStart;
    public bool IsPartialDownload => RangeStart > 0 || RangeEnd < Item.PatchInfo.FileSize;
    public TaskPriority Priority { get; set; }
    
    public DateTime StartTime { get; set; }
    public long BytesDownloaded { get; set; }
    public int RetryCount { get; set; }
    
    public DownloadTask(DownloadItem item, long rangeStart = 0, long rangeEnd = -1)
    {
        Item = item;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd < 0 ? item.PatchInfo.FileSize : rangeEnd;
        
        // 根据任务大小设置优先级
        Priority = TaskSize switch
        {
            < 500 * 1024 => TaskPriority.Small,           // < 500KB
            < 5 * 1024 * 1024 => TaskPriority.Medium,     // 500KB - 5MB
            _ => TaskPriority.Large                        // > 5MB
        };
    }
    
    /// <summary>判断任务是否卡住（下载时间过长）</summary>
    public bool IsStuck(double thresholdSeconds = 15.0)
    {
        if (StartTime == default) return false;
        
        var elapsed = (DateTime.UtcNow - StartTime).TotalSeconds;
        var expectedTime = TaskSize / (400 * 1024.0); // 假设最低400KB/s
        
        return elapsed > Math.Max(expectedTime * 2, thresholdSeconds);
    }
}

/// <summary>任务优先级</summary>
public enum TaskPriority
{
    Small = 0,   // < 500KB
    Medium = 1,  // 500KB - 5MB
    Large = 2    // > 5MB
}
