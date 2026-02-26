using StarDriver.Core.Models;
using StarDriver.Downloader;

namespace StarDriver.Test;

/// <summary>
/// 智能下载引擎终端测试
/// </summary>
public class SmartEngineTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 智能下载引擎测试 ===\n");
        
        Console.Write("请输入下载目录 (默认: D:\\test_download): ");
        var downloadPath = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(downloadPath))
        {
            downloadPath = "D:\\test_download";
        }
        
        Console.Write("并发数 (默认: 8): ");
        var concurrentInput = Console.ReadLine();
        var concurrent = int.TryParse(concurrentInput, out var c) ? c : 8;
        
        Console.Write("使用智能调度器? (y/n, 默认: y): ");
        var useSmartInput = Console.ReadLine();
        var useSmart = string.IsNullOrWhiteSpace(useSmartInput) || useSmartInput.ToLower() == "y";
        
        Console.WriteLine($"\n配置:");
        Console.WriteLine($"  下载目录: {downloadPath}");
        Console.WriteLine($"  并发数: {concurrent}");
        Console.WriteLine($"  智能调度: {(useSmart ? "启用" : "禁用")}");
        Console.WriteLine();
        
        using var engine = new GameDownloadEngine(downloadPath);
        engine.ConcurrentDownloads = concurrent;
        // TODO: engine.UseSmartScheduler = useSmart; // 智能调度器集成待实现
        
        // 订阅事件
        var startTime = DateTime.UtcNow;
        var lastProgressTime = DateTime.UtcNow;
        long lastBytes = 0;
        var completedFiles = 0;
        var failedFiles = 0;
        
        engine.ScanProgress += (sender, e) =>
        {
            if (e.ScannedFiles % 1000 == 0 || e.ScannedFiles == e.TotalFiles)
            {
                Console.WriteLine($"[扫描] {e.ScannedFiles}/{e.TotalFiles} ({e.ProgressPercentage:F1}%)");
            }
        };
        
        engine.DownloadProgress += (sender, e) =>
        {
            var now = DateTime.UtcNow;
            if ((now - lastProgressTime).TotalSeconds >= 2)
            {
                var elapsed = (now - startTime).TotalSeconds;
                var totalMB = e.BytesDownloaded / (1024.0 * 1024.0);
                var speedMBps = (e.BytesDownloaded - lastBytes) / (1024.0 * 1024.0) / (now - lastProgressTime).TotalSeconds;
                
                Console.WriteLine($"[Node {e.TaskId}] {Path.GetFileName(e.Item.LocalPath)} - " +
                    $"{e.ProgressPercentage:F1}% ({speedMBps:F2} MB/s)");
                
                lastProgressTime = now;
                lastBytes = e.BytesDownloaded;
            }
        };
        
        engine.FileVerified += (sender, e) =>
        {
            if (e.IsValid)
            {
                completedFiles++;
                Console.WriteLine($"[✓] {Path.GetFileName(e.Item.LocalPath)} - 完成");
            }
            else
            {
                failedFiles++;
                Console.WriteLine($"[✗] {Path.GetFileName(e.Item.LocalPath)} - 失败: {e.Item.ErrorMessage}");
            }
        };
        
        engine.DownloadCompleted += (sender, e) =>
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            Console.WriteLine($"\n=== 下载完成 ===");
            Console.WriteLine($"总耗时: {elapsed:F1} 秒");
            Console.WriteLine($"成功: {e.SucceededCount}");
            Console.WriteLine($"失败: {e.FailedCount}");
            Console.WriteLine($"取消: {e.CancelledCount}");
        };
        
        try
        {
            Console.WriteLine("开始下载...\n");
            await engine.ScanAndDownloadAsync(GameClientSelection.NGS_Full);
            
            Console.WriteLine("\n测试完成！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ 错误: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        
        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }
}
