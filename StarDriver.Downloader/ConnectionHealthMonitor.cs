using System.Collections.Concurrent;

namespace StarDriver.Downloader;

/// <summary>
/// 连接健康监控器 - 监控网络错误率并触发连接池重置
/// </summary>
public sealed class ConnectionHealthMonitor
{
    private readonly ConcurrentQueue<ErrorRecord> _recentErrors = new();
    private readonly TimeSpan _monitorWindow = TimeSpan.FromMinutes(5);
    private readonly int _errorThreshold = 50; // 5分钟内超过50个错误触发警告
    private DateTime _lastResetTime = DateTime.UtcNow;
    private readonly TimeSpan _minResetInterval = TimeSpan.FromMinutes(10); // 最小重置间隔
    
    private int _totalErrors;
    private int _totalRequests;
    
    public int TotalErrors => _totalErrors;
    public int TotalRequests => _totalRequests;
    public double ErrorRate => _totalRequests > 0 ? (double)_totalErrors / _totalRequests : 0;
    
    /// <summary>记录成功请求</summary>
    public void RecordSuccess()
    {
        Interlocked.Increment(ref _totalRequests);
        CleanupOldErrors();
    }
    
    /// <summary>记录错误</summary>
    public void RecordError(string errorType)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _totalErrors);
        
        _recentErrors.Enqueue(new ErrorRecord(DateTime.UtcNow, errorType));
        CleanupOldErrors();
    }
    
    /// <summary>检查是否需要重置连接池</summary>
    public bool ShouldResetConnectionPool()
    {
        var now = DateTime.UtcNow;
        
        // 检查最小重置间隔
        if (now - _lastResetTime < _minResetInterval)
            return false;
        
        // 清理旧错误
        CleanupOldErrors();
        
        // 检查最近5分钟的错误数
        var recentErrorCount = _recentErrors.Count;
        
        if (recentErrorCount >= _errorThreshold)
        {
            Console.WriteLine($"[健康监控] 检测到高错误率：最近5分钟内 {recentErrorCount} 个错误，建议重置连接池");
            _lastResetTime = now;
            return true;
        }
        
        return false;
    }
    
    /// <summary>获取统计信息</summary>
    public string GetStatistics()
    {
        CleanupOldErrors();
        var recentErrorCount = _recentErrors.Count;
        return $"总请求: {TotalRequests}, 总错误: {TotalErrors}, 错误率: {ErrorRate:P2}, 最近5分钟错误: {recentErrorCount}";
    }
    
    /// <summary>清理超过监控窗口的旧错误</summary>
    private void CleanupOldErrors()
    {
        var cutoffTime = DateTime.UtcNow - _monitorWindow;
        
        while (_recentErrors.TryPeek(out var record) && record.Timestamp < cutoffTime)
        {
            _recentErrors.TryDequeue(out _);
        }
    }
    
    private record ErrorRecord(DateTime Timestamp, string ErrorType);
}
