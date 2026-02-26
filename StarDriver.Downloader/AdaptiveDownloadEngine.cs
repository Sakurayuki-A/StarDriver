using StarDriver.Core.Models;
using System.Collections.Concurrent;

namespace StarDriver.Downloader;

/// <summary>
/// 自适应下载引擎 - 支持动态线程池调配
/// 大文件完成后自动将空闲线程调配到中小文件
/// </summary>
public sealed class AdaptiveDownloadEngine : IDisposable
{
    private readonly SmartDownloadScheduler _scheduler = new();
    
    // 分层线程池配置
    private int _largeFileThreads = 32;
    private int _mediumFileThreads = 16;
    private int _smallFileThreads = 16;
    
    // 动态调配状态
    private int _activeLargeThreads = 0;
    private int _activeMediumThreads = 0;
    private int _activeSmallThreads = 0;
    
    private readonly SemaphoreSlim _largeFileSemaphore;
    private readonly SemaphoreSlim _mediumFileSemaphore;
    private readonly SemaphoreSlim _smallFileSemaphore;
    
    private readonly CancellationTokenSource _cts = new();
    private readonly object _rebalanceLock = new();
    
    // 统计数据
    private long _totalBytesDownloaded = 0;
    private int _completedFiles = 0;
    private int _failedFiles = 0;
    private DateTime _startTime;
    
    public AdaptiveDownloadEngine()
    {
        _largeFileSemaphore = new SemaphoreSlim(_largeFileThreads, _largeFileThreads);
        _mediumFileSemaphore = new SemaphoreSlim(_mediumFileThreads, _mediumFileThreads);
        _smallFileSemaphore = new SemaphoreSlim(_smallFileThreads, _smallFileThreads);
    }
    
    /// <summary>
    /// 开始下载
    /// </summary>
    public async Task DownloadAsync(List<DownloadItem> items)
    {
        _startTime = DateTime.UtcNow;
        _scheduler.Enqueue(items);
        
        Console.WriteLine($"[自适应引擎] 初始配置: 大文件={_largeFileThreads}, 中等={_mediumFileThreads}, 小文件={_smallFileThreads}");
        
        // 启动监控任务
        var monitorTask = Task.Run(() => MonitorAndRebalance(_cts.Token));
        
        // 启动三层下载任务
        var tasks = new List<Task>
        {
            Task.Run(() => DownloadLargeFilesAsync(_cts.Token)),
            Task.Run(() => DownloadMediumFilesAsync(_cts.Token)),
            Task.Run(() => DownloadSmallFilesAsync(_cts.Token))
        };
        
        await Task.WhenAll(tasks);
        
        // 停止监控
        _cts.Cancel();
        await monitorTask;
        
        PrintStatistics();
    }
    
    /// <summary>
    /// 大文件下载循环
    /// </summary>
    private async Task DownloadLargeFilesAsync(CancellationToken ct)
    {
        var workerTasks = new List<Task>();
        
        for (int i = 0; i < _largeFileThreads; i++)
        {
            var workerId = i;
            workerTasks.Add(Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await _largeFileSemaphore.WaitAsync(ct);
                    
                    try
                    {
                        if (_scheduler.TryDequeueLarge(out var item) && item != null)
                        {
                            Interlocked.Increment(ref _activeLargeThreads);
                            await DownloadFileAsync(item, $"Large-{workerId}", ct);
                            Interlocked.Decrement(ref _activeLargeThreads);
                        }
                        else
                        {
                            // 大文件队列空了，退出
                            break;
                        }
                    }
                    finally
                    {
                        _largeFileSemaphore.Release();
                    }
                }
            }, ct));
        }
        
        await Task.WhenAll(workerTasks);
        Console.WriteLine($"[大文件层] 所有任务完成，准备释放线程");
    }
    
    /// <summary>
    /// 中等文件下载循环
    /// </summary>
    private async Task DownloadMediumFilesAsync(CancellationToken ct)
    {
        var workerTasks = new List<Task>();
        
        for (int i = 0; i < _mediumFileThreads; i++)
        {
            var workerId = i;
            workerTasks.Add(Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await _mediumFileSemaphore.WaitAsync(ct);
                    
                    try
                    {
                        if (_scheduler.TryDequeueMedium(out var item) && item != null)
                        {
                            Interlocked.Increment(ref _activeMediumThreads);
                            await DownloadFileAsync(item, $"Medium-{workerId}", ct);
                            Interlocked.Decrement(ref _activeMediumThreads);
                        }
                        else
                        {
                            // 中等文件队列空了，退出
                            break;
                        }
                    }
                    finally
                    {
                        _mediumFileSemaphore.Release();
                    }
                }
            }, ct));
        }
        
        await Task.WhenAll(workerTasks);
        Console.WriteLine($"[中等文件层] 所有任务完成");
    }
    
    /// <summary>
    /// 小文件下载循环
    /// </summary>
    private async Task DownloadSmallFilesAsync(CancellationToken ct)
    {
        var workerTasks = new List<Task>();
        
        for (int i = 0; i < _smallFileThreads; i++)
        {
            var workerId = i;
            workerTasks.Add(Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    await _smallFileSemaphore.WaitAsync(ct);
                    
                    try
                    {
                        if (_scheduler.TryDequeueSmall(out var item) && item != null)
                        {
                            Interlocked.Increment(ref _activeSmallThreads);
                            await DownloadFileAsync(item, $"Small-{workerId}", ct);
                            Interlocked.Decrement(ref _activeSmallThreads);
                        }
                        else
                        {
                            // 小文件队列空了，退出
                            break;
                        }
                    }
                    finally
                    {
                        _smallFileSemaphore.Release();
                    }
                }
            }, ct));
        }
        
        await Task.WhenAll(workerTasks);
        Console.WriteLine($"[小文件层] 所有任务完成");
    }
    
    /// <summary>
    /// 监控并动态调配线程
    /// </summary>
    private async Task MonitorAndRebalance(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct); // 每2秒检查一次
            
            lock (_rebalanceLock)
            {
                var largeQueueEmpty = _scheduler.LargeFileCount == 0;
                var mediumQueueEmpty = _scheduler.MediumFileCount == 0;
                var idleLargeThreads = _largeFileThreads - _activeLargeThreads;
                var idleMediumThreads = _mediumFileThreads - _activeMediumThreads;
                
                // 大文件队列空了，释放线程到中小文件
                if (largeQueueEmpty && idleLargeThreads > 0)
                {
                    var threadsToRedistribute = idleLargeThreads;
                    
                    // 优先分配给中等文件
                    if (_scheduler.MediumFileCount > 0)
                    {
                        var toMedium = Math.Min(threadsToRedistribute / 2, 16);
                        _mediumFileSemaphore.Release(toMedium);
                        _mediumFileThreads += toMedium;
                        threadsToRedistribute -= toMedium;
                        
                        Console.WriteLine($"[线程调配] 从大文件层释放 {toMedium} 个线程到中等文件层（新容量: {_mediumFileThreads}）");
                    }
                    
                    // 剩余分配给小文件
                    if (_scheduler.SmallFileCount > 0 && threadsToRedistribute > 0)
                    {
                        _smallFileSemaphore.Release(threadsToRedistribute);
                        _smallFileThreads += threadsToRedistribute;
                        
                        Console.WriteLine($"[线程调配] 从大文件层释放 {threadsToRedistribute} 个线程到小文件层（新容量: {_smallFileThreads}）");
                    }
                    
                    // 重置大文件线程数（避免重复释放）
                    _largeFileThreads = _activeLargeThreads;
                }
                
                // 中等文件队列空了，释放线程到小文件
                if (mediumQueueEmpty && idleMediumThreads > 0 && _scheduler.SmallFileCount > 0)
                {
                    _smallFileSemaphore.Release(idleMediumThreads);
                    _smallFileThreads += idleMediumThreads;
                    
                    Console.WriteLine($"[线程调配] 从中等文件层释放 {idleMediumThreads} 个线程到小文件层（新容量: {_smallFileThreads}）");
                    
                    _mediumFileThreads = _activeMediumThreads;
                }
                
                // 打印状态
                if (_completedFiles % 100 == 0)
                {
                    Console.WriteLine($"[状态] 活跃线程: 大={_activeLargeThreads}/{_largeFileThreads}, " +
                        $"中={_activeMediumThreads}/{_mediumFileThreads}, " +
                        $"小={_activeSmallThreads}/{_smallFileThreads} | " +
                        $"队列: 大={_scheduler.LargeFileCount}, 中={_scheduler.MediumFileCount}, 小={_scheduler.SmallFileCount}");
                }
            }
        }
    }
    
    /// <summary>
    /// 下载单个文件（模拟）
    /// </summary>
    private async Task DownloadFileAsync(DownloadItem item, string workerId, CancellationToken ct)
    {
        try
        {
            var fileName = Path.GetFileName(item.LocalPath);
            var fileSize = item.PatchInfo.FileSize;
            
            // 模拟下载（实际应该调用 PSO2HttpClient）
            var downloadTime = fileSize / (2 * 1024 * 1024.0); // 假设 2MB/s
            await Task.Delay(TimeSpan.FromSeconds(downloadTime), ct);
            
            Interlocked.Add(ref _totalBytesDownloaded, fileSize);
            Interlocked.Increment(ref _completedFiles);
            
            if (_completedFiles % 50 == 0)
            {
                var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
                var speedMBps = (_totalBytesDownloaded / (1024.0 * 1024.0)) / elapsed;
                Console.WriteLine($"[{workerId}] 完成: {fileName} ({fileSize / (1024.0 * 1024):F2} MB) | " +
                    $"总进度: {_completedFiles} 文件, {speedMBps:F2} MB/s");
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedFiles);
            Console.WriteLine($"[{workerId}] 失败: {Path.GetFileName(item.LocalPath)} - {ex.Message}");
        }
    }
    
    private void PrintStatistics()
    {
        var elapsed = DateTime.UtcNow - _startTime;
        var totalGB = _totalBytesDownloaded / (1024.0 * 1024 * 1024);
        var avgSpeedMBps = (_totalBytesDownloaded / (1024.0 * 1024)) / elapsed.TotalSeconds;
        
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("下载完成！");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"总耗时: {elapsed:hh\\:mm\\:ss}");
        Console.WriteLine($"下载大小: {totalGB:F2} GB");
        Console.WriteLine($"完成文件: {_completedFiles}");
        Console.WriteLine($"失败文件: {_failedFiles}");
        Console.WriteLine($"平均速度: {avgSpeedMBps:F2} MB/s");
        Console.WriteLine(new string('=', 60));
    }
    
    public void Dispose()
    {
        _cts?.Dispose();
        _largeFileSemaphore?.Dispose();
        _mediumFileSemaphore?.Dispose();
        _smallFileSemaphore?.Dispose();
    }
}
