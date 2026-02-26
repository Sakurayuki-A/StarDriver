using StarDriver.Core.Models;
using StarDriver.Downloader;
using System.Diagnostics;

namespace StarDriver.Test;

/// <summary>
/// 性能对比测试 - 测试不同配置下的下载性能
/// </summary>
public class PerformanceComparisonTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 下载性能对比测试 ===\n");
        
        var baseDir = @"D:\game";
        
        // 测试配置
        var configs = new[]
        {
            new { Name = "8并发 + 64KB缓冲", Concurrent = 8, BufferSize = 64 * 1024 },
            new { Name = "16并发 + 128KB缓冲", Concurrent = 16, BufferSize = 128 * 1024 },
            new { Name = "32并发 + 128KB缓冲", Concurrent = 32, BufferSize = 128 * 1024 }
        };
        
        foreach (var config in configs)
        {
            Console.WriteLine($"\n--- 测试配置: {config.Name} ---");
            await TestDownloadPerformance(baseDir, config.Concurrent);
            
            // 等待一段时间，避免服务器限流
            await Task.Delay(5000);
        }
    }
    
    private static async Task TestDownloadPerformance(string baseDir, int concurrentDownloads)
    {
        using var engine = new GameDownloadEngine(baseDir);
        engine.ConcurrentDownloads = concurrentDownloads;
        
        var stopwatch = Stopwatch.StartNew();
        var downloadedFiles = 0;
        var totalBytes = 0L;
        var lastReportTime = DateTime.UtcNow;
        var lastBytes = 0L;
        
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
            Interlocked.Add(ref totalBytes, e.BytesDownloaded);
            
            // 每秒报告一次速度
            var now = DateTime.UtcNow;
            if ((now - lastReportTime).TotalSeconds >= 1.0)
            {
                var currentBytes = Interlocked.Read(ref totalBytes);
                var bytesPerSecond = (currentBytes - lastBytes) / (now - lastReportTime).TotalSeconds;
                var speedMBps = bytesPerSecond / (1024 * 1024);
                
                Console.WriteLine($"[下载] 速度: {speedMBps:F2} MB/s, 总计: {currentBytes / (1024.0 * 1024):F2} MB");
                
                lastReportTime = now;
                lastBytes = currentBytes;
            }
        };
        
        engine.FileVerified += (s, e) =>
        {
            if (e.IsValid)
            {
                Interlocked.Increment(ref downloadedFiles);
            }
        };
        
        engine.DownloadCompleted += (s, e) =>
        {
            stopwatch.Stop();
            
            var totalMB = totalBytes / (1024.0 * 1024);
            var avgSpeedMBps = totalMB / stopwatch.Elapsed.TotalSeconds;
            
            Console.WriteLine($"\n[完成] 耗时: {stopwatch.Elapsed:hh\\:mm\\:ss}");
            Console.WriteLine($"[完成] 下载文件: {e.SucceededCount}");
            Console.WriteLine($"[完成] 总大小: {totalMB:F2} MB");
            Console.WriteLine($"[完成] 平均速度: {avgSpeedMBps:F2} MB/s");
            Console.WriteLine($"[完成] 失败: {e.FailedCount}, 取消: {e.CancelledCount}");
        };
        
        try
        {
            // 下载 NGS 完整客户端（仅测试前100个文件）
            await engine.ScanAndDownloadAsync(
                GameClientSelection.NGS_Full,
                FileScanFlags.Default);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] {ex.Message}");
        }
    }
}
