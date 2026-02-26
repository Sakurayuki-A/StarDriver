using StarDriver.Core.Models;
using StarDriver.Downloader;
using System.Diagnostics;

namespace StarDriver.Test;

/// <summary>
/// 完整游戏下载测试 - 下载整个 NGS 客户端（约 68GB）
/// </summary>
public class FullDownloadTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== PSO2 NGS 完整客户端下载 ===\n");
        
        var baseDir = @"D:\game";
        
        Console.WriteLine($"下载目录: {baseDir}");
        Console.WriteLine($"预计大小: ~90 GB");
        Console.WriteLine($"并发策略: 28线程分层 (16大 + 6中 + 6小)");
        Console.WriteLine($"缓冲区: 32-64 KB");
        Console.WriteLine($"连接池: 28连接 (2分钟刷新)");
        Console.WriteLine($"预计耗时: 约 90-150 分钟 (取决于网络)\n");
        
        // 自动开始
        Console.WriteLine("开始完整下载...\n");
        
        using var engine = new GameDownloadEngine(baseDir);
        
        var stopwatch = Stopwatch.StartNew();
        var downloadedFiles = 0;
        var failedFiles = 0;
        var totalBytes = 0L;
        var totalFilesToDownload = 0; // 需要下载的总文件数
        
        // 速度监控
        var lastReportTime = DateTime.UtcNow;
        var lastBytes = 0L;
        var speedSamples = new List<double>();
        
        // 活跃任务监控
        var activeTasks = new HashSet<int>();
        var maxActiveTasks = 0;
        
        // 跟踪每个文件的下载进度
        var fileProgress = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
        
        // 订阅事件
        engine.ScanProgress += (s, e) =>
        {
            if (e.ScannedFiles % 1000 == 0 || e.ScannedFiles == e.TotalFiles)
            {
                // 使用 \r 实现同一行更新，避免刷屏
                Console.Write($"\r[扫描] {e.ScannedFiles}/{e.TotalFiles} ({e.ProgressPercentage:F1}%)");
                if (e.ScannedFiles == e.TotalFiles)
                {
                    Console.WriteLine(); // 扫描完成后换行
                }
            }
        };
        
        engine.DownloadProgress += (s, e) =>
        {
            lock (activeTasks)
            {
                activeTasks.Add(e.TaskId);
                maxActiveTasks = Math.Max(maxActiveTasks, activeTasks.Count);
            }
            
            // 使用文件路径作为键，跟踪每个文件的进度
            var fileKey = e.Item.LocalPath;
            var oldProgress = fileProgress.GetOrAdd(fileKey, 0);
            var newProgress = e.BytesDownloaded;
            var delta = newProgress - oldProgress;
            
            if (delta > 0)
            {
                fileProgress[fileKey] = newProgress;
                Interlocked.Add(ref totalBytes, delta);
            }
            
            // 每秒报告一次
            var now = DateTime.UtcNow;
            if ((now - lastReportTime).TotalSeconds >= 1.0)
            {
                var currentBytes = Interlocked.Read(ref totalBytes);
                var bytesPerSecond = (currentBytes - lastBytes) / (now - lastReportTime).TotalSeconds;
                var speedMBps = bytesPerSecond / (1024 * 1024);
                
                speedSamples.Add(speedMBps);
                
                var currentDownloaded = Interlocked.CompareExchange(ref downloadedFiles, 0, 0);
                var remainingFiles = totalFilesToDownload > 0 ? totalFilesToDownload - currentDownloaded : 0;
                var eta = speedMBps > 0 ? TimeSpan.FromSeconds((90L * 1024 * 1024 * 1024 - currentBytes) / bytesPerSecond) : TimeSpan.Zero;
                var elapsed = stopwatch.Elapsed;
                
                // 使用 \r 实现同一行更新，避免刷屏
                Console.Write($"\r已下载文件数: {currentDownloaded} / 剩余文件数: {remainingFiles} | {speedMBps:F2} MB/s | 活跃:{activeTasks.Count} | 已用:{elapsed:hh\\:mm\\:ss} | ETA:{eta:hh\\:mm\\:ss}");
                
                lastReportTime = now;
                lastBytes = currentBytes;
                
                lock (activeTasks)
                {
                    activeTasks.Clear();
                }
            }
        };
        
        engine.FileVerified += (s, e) =>
        {
            if (e.IsValid)
            {
                Interlocked.Increment(ref downloadedFiles);
            }
            else
            {
                Interlocked.Increment(ref failedFiles);
                // 失败信息换行显示，不影响进度条
                Console.WriteLine($"\n[失败] {Path.GetFileName(e.Item.LocalPath)} - {e.Item.ErrorMessage}");
            }
        };
        
        engine.DownloadCompleted += (s, e) =>
        {
            stopwatch.Stop();
            
            // 进度条完成后换行
            Console.WriteLine();
            
            var totalGB = totalBytes / (1024.0 * 1024 * 1024);
            var avgSpeedMBps = (totalBytes / (1024.0 * 1024)) / stopwatch.Elapsed.TotalSeconds;
            var maxSpeed = speedSamples.Count > 0 ? speedSamples.Max() : 0;
            var minSpeed = speedSamples.Count > 0 ? speedSamples.Min() : 0;
            var avgSpeed = speedSamples.Count > 0 ? speedSamples.Average() : 0;
            
            Console.WriteLine("\n" + new string('=', 70));
            Console.WriteLine("下载完成！");
            Console.WriteLine(new string('=', 70));
            Console.WriteLine($"总耗时: {stopwatch.Elapsed:hh\\:mm\\:ss}");
            Console.WriteLine($"下载大小: {totalGB:F2} GB");
            Console.WriteLine($"下载文件: {e.SucceededCount} 个");
            Console.WriteLine($"失败文件: {e.FailedCount} 个");
            Console.WriteLine($"取消文件: {e.CancelledCount} 个");
            Console.WriteLine($"\n速度统计:");
            Console.WriteLine($"  平均速度: {avgSpeed:F2} MB/s");
            Console.WriteLine($"  最高速度: {maxSpeed:F2} MB/s");
            Console.WriteLine($"  最低速度: {minSpeed:F2} MB/s");
            Console.WriteLine($"  最大并发: {maxActiveTasks} 个任务");
            
            if (e.FailedCount > 0)
            {
                Console.WriteLine($"\n注意: 有 {e.FailedCount} 个文件下载失败，请重新运行下载器进行增量更新");
            }
            
            Console.WriteLine(new string('=', 70));
        };
        
        try
        {
            // 先获取需要下载的文件列表以计算总数
            Console.WriteLine("正在获取文件列表...");
            
            // 下载 NGS 完整客户端
            // 注意：需要在 ScanProgress 完成后获取 totalFilesToDownload
            // 这里我们通过 DownloadCompleted 事件来获取实际的文件总数
            var downloadTask = engine.ScanAndDownloadAsync(
                GameClientSelection.NGS_Full,
                FileScanFlags.Default);
            
            // 等待扫描完成后设置总文件数
            await Task.Delay(100); // 给扫描一点时间开始
            
            await downloadTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[错误] {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
