using StarDriver.Core.Models;
using StarDriver.Downloader;
using System.Diagnostics;

namespace StarDriver.Test;

/// <summary>
/// 性能测试 - 验证下载速度优化
/// </summary>
public class PerformanceTest
{
    public static async Task TestDownloadSpeed()
    {
        Console.WriteLine("=== 下载性能测试 ===\n");
        
        var baseDir = @"D:\game_test";
        
        using var engine = new GameDownloadEngine(baseDir);
        
        // 配置高并发
        engine.ConcurrentDownloads = 16;
        
        var totalBytes = 0L;
        var completedFiles = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastUpdate = DateTime.UtcNow;
        var lastBytes = 0L;
        
        // 订阅进度事件
        engine.DownloadProgress += (sender, e) =>
        {
            Interlocked.Add(ref totalBytes, e.BytesDownloaded);
            
            var now = DateTime.UtcNow;
            var elapsed = (now - lastUpdate).TotalSeconds;
            
            // 每秒更新一次速度统计
            if (elapsed >= 1.0)
            {
                var currentBytes = Interlocked.Read(ref totalBytes);
                var bytesDelta = currentBytes - lastBytes;
                var speedMBps = (bytesDelta / elapsed) / (1024 * 1024);
                
                Console.WriteLine($"[速度] {speedMBps:F2} MB/s | 已下载: {currentBytes / (1024 * 1024):F1} MB | 活跃任务: {e.TaskId + 1}");
                
                lastBytes = currentBytes;
                lastUpdate = now;
            }
        };
        
        engine.FileVerified += (sender, e) =>
        {
            if (e.IsValid)
            {
                Interlocked.Increment(ref completedFiles);
                Console.WriteLine($"[完成] {Path.GetFileName(e.Item.LocalPath)} (文件 {completedFiles})");
            }
        };
        
        engine.DownloadCompleted += (sender, e) =>
        {
            stopwatch.Stop();
            
            var totalMB = totalBytes / (1024.0 * 1024.0);
            var avgSpeedMBps = totalMB / stopwatch.Elapsed.TotalSeconds;
            
            Console.WriteLine($"\n=== 测试完成 ===");
            Console.WriteLine($"总下载: {totalMB:F2} MB");
            Console.WriteLine($"总耗时: {stopwatch.Elapsed.TotalSeconds:F1} 秒");
            Console.WriteLine($"平均速度: {avgSpeedMBps:F2} MB/s");
            Console.WriteLine($"成功: {e.SucceededCount} | 失败: {e.FailedCount}");
            
            if (avgSpeedMBps >= 10.0)
            {
                Console.WriteLine("✓ 性能目标达成！(>= 10 MB/s)");
            }
            else
            {
                Console.WriteLine($"✗ 未达到目标，当前速度: {avgSpeedMBps:F2} MB/s");
            }
        };
        
        try
        {
            // 下载 NGS 启动器文件（较小，适合快速测试）
            await engine.ScanAndDownloadAsync(
                GameClientSelection.Launcher_Only,
                FileScanFlags.ForceRefreshCache | FileScanFlags.MD5HashMismatch);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
        }
    }
}
