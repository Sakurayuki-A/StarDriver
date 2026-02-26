using StarDriver.Core.Models;
using StarDriver.Downloader;

namespace StarDriver.Test;

public class ConcurrentDownloadTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 并发下载测试 ===\n");
        
        var gameDir = @"D:\pso2ngs";
        Console.WriteLine($"游戏目录: {gameDir}\n");
        
        using var engine = new GameDownloadEngine(gameDir);
        
        // 订阅事件
        int activeDownloads = 0;
        var activeTasks = new HashSet<int>();
        var lockObj = new object();
        
        engine.DownloadProgress += (sender, e) =>
        {
            lock (lockObj)
            {
                if (!activeTasks.Contains(e.TaskId))
                {
                    activeTasks.Add(e.TaskId);
                    activeDownloads++;
                    Console.WriteLine($"[TaskID {e.TaskId}] 开始下载: {Path.GetFileName(e.Item.LocalPath)} (当前活动任务: {activeDownloads})");
                }
            }
        };
        
        engine.FileVerified += (sender, e) =>
        {
            lock (lockObj)
            {
                if (activeTasks.Contains(e.TaskId))
                {
                    activeTasks.Remove(e.TaskId);
                    activeDownloads--;
                    Console.WriteLine($"[TaskID {e.TaskId}] 完成下载: {Path.GetFileName(e.Item.LocalPath)} - {(e.IsValid ? "成功" : "失败")} (当前活动任务: {activeDownloads})");
                }
            }
        };
        
        engine.ScanProgress += (sender, e) =>
        {
            if (e.ScannedFiles % 100 == 0 || e.ScannedFiles == e.TotalFiles)
            {
                Console.WriteLine($"扫描进度: {e.ScannedFiles}/{e.TotalFiles}");
            }
        };
        
        engine.DownloadCompleted += (sender, e) =>
        {
            Console.WriteLine($"\n下载完成！");
            Console.WriteLine($"成功: {e.SucceededCount}");
            Console.WriteLine($"失败: {e.FailedCount}");
            Console.WriteLine($"取消: {e.CancelledCount}");
        };
        
        Console.WriteLine($"并发数: {engine.ConcurrentDownloads}");
        Console.WriteLine($"最大重试: {engine.MaxRetries}\n");
        
        try
        {
            await engine.ScanAndDownloadAsync(
                GameClientSelection.NGS_Full,
                FileScanFlags.Default);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n错误: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        
        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }
}
