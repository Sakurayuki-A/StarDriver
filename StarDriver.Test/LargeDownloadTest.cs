using StarDriver.Core.Models;
using StarDriver.Downloader;
using System.Diagnostics;

namespace StarDriver.Test;

/// <summary>
/// 大文件下载测试 - 测试 30GB 下载性能
/// </summary>
public class LargeDownloadTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 30GB 下载性能测试 ===\n");
        
        var baseDir = @"D:\game";
        
        Console.WriteLine($"下载目录: {baseDir}");
        Console.WriteLine($"目标大小: 30 GB");
        Console.WriteLine($"并发数: 28 (16大 + 6中 + 6小)");
        Console.WriteLine($"缓冲区: 32-64 KB");
        Console.WriteLine($"连接池: 28连接 (2分钟刷新)");
        Console.WriteLine($"策略: 分层并发 - 不同大小文件独立线程池\n");
        
        // 自动开始测试（用于自动化测试）
        Console.WriteLine("自动开始测试...\n");
        
        using var engine = new GameDownloadEngine(baseDir);
        // 不需要设置并发数，引擎内部使用分层策略
        
        var stopwatch = Stopwatch.StartNew();
        var downloadedFiles = 0;
        var failedFiles = 0;
        var totalBytes = 0L;
        var targetBytes = 30L * 1024 * 1024 * 1024; // 30 GB
        
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
                Console.WriteLine($"[扫描] {e.ScannedFiles}/{e.TotalFiles} ({e.ProgressPercentage:F1}%)");
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
                
                var progress = (double)currentBytes / targetBytes * 100;
                var downloadedGB = currentBytes / (1024.0 * 1024 * 1024);
                var eta = speedMBps > 0 ? TimeSpan.FromSeconds((targetBytes - currentBytes) / bytesPerSecond) : TimeSpan.Zero;
                var elapsed = stopwatch.Elapsed;
                
                // 创建进度条
                var barWidth = 30;
                var filledWidth = (int)(progress / 100 * barWidth);
                var progressBar = new string('█', filledWidth) + new string('░', barWidth - filledWidth);
                
                // 使用 \r 实现同一行更新，添加已用时间
                Console.Write($"\r[{progressBar}] {downloadedGB:F2}/{30:F2} GB ({progress:F1}%) | {speedMBps:F2} MB/s | 活跃:{activeTasks.Count} | 已用:{elapsed:hh\\:mm\\:ss} | ETA:{eta:hh\\:mm\\:ss}");
                
                lastReportTime = now;
                lastBytes = currentBytes;
                
                lock (activeTasks)
                {
                    activeTasks.Clear();
                }
            }
            
            // 达到 30GB 后停止
            var currentTotal = Interlocked.Read(ref totalBytes);
            if (currentTotal >= targetBytes)
            {
                Console.WriteLine($"\n已达到 30GB 目标，停止下载...");
                engine.Cancel();
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
                Console.WriteLine($"[失败] {Path.GetFileName(e.Item.LocalPath)} - {e.Item.ErrorMessage}");
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
            
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("测试完成！");
            Console.WriteLine(new string('=', 60));
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
            Console.WriteLine($"\n预估完整下载 (68GB):");
            var fullDownloadTime = TimeSpan.FromSeconds(68 * 1024 / avgSpeed);
            Console.WriteLine($"  预计耗时: {fullDownloadTime:hh\\:mm\\:ss}");
            Console.WriteLine(new string('=', 60));
        };
        
        try
        {
            // 下载 NGS 完整客户端
            await engine.ScanAndDownloadAsync(
                GameClientSelection.NGS_Full,
                FileScanFlags.Default);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n下载已取消（达到 30GB 目标）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[错误] {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
