using StarDriver.Core.Models;
using StarDriver.Downloader;

namespace StarDriver.Test;

public static class DownloadEngineTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 下载引擎测试 ===\n");

        var testDir = Path.Combine(Path.GetTempPath(), "StarDriver_Test_" + Guid.NewGuid().ToString("N"));
        
        try
        {
            Console.WriteLine($"测试目录: {testDir}\n");

            using var engine = new GameDownloadEngine(testDir);
            
            // 订阅事件
            int scannedCount = 0;
            int downloadedCount = 0;
            int verifiedCount = 0;
            int failedCount = 0;

            engine.ScanProgress += (s, e) =>
            {
                scannedCount = e.ScannedFiles;
                if (e.ScannedFiles % 1000 == 0)
                {
                    Console.WriteLine($"   扫描进度: {e.ScannedFiles}/{e.TotalFiles} ({e.ProgressPercentage:F1}%)");
                }
            };

            engine.DownloadProgress += (s, e) =>
            {
                downloadedCount++;
                if (downloadedCount % 10 == 0)
                {
                    var filename = Path.GetFileName(e.Item.LocalPath);
                    Console.WriteLine($"   下载中: {filename} - {e.ProgressPercentage:F1}%");
                }
            };

            engine.FileVerified += (s, e) =>
            {
                verifiedCount++;
                var filename = Path.GetFileName(e.Item.LocalPath);
                var status = e.IsValid ? "✓" : "✗";
                
                if (!e.IsValid)
                {
                    failedCount++;
                    Console.WriteLine($"   {status} {filename} - 失败！错误: {e.Item.ErrorMessage}");
                }
                else if (verifiedCount <= 10)
                {
                    Console.WriteLine($"   {status} {filename} - 成功");
                }
            };

            engine.DownloadCompleted += (s, e) =>
            {
                Console.WriteLine($"\n下载完成:");
                Console.WriteLine($"   成功: {e.SucceededCount}");
                Console.WriteLine($"   失败: {e.FailedCount}");
                Console.WriteLine($"   取消: {e.CancelledCount}");
            };

            Console.WriteLine("开始扫描和下载（仅下载前 20 个文件作为测试）...\n");

            // 使用 CancellationTokenSource 来限制下载数量
            using var cts = new CancellationTokenSource();
            
            var downloadTask = Task.Run(async () =>
            {
                try
                {
                    await engine.ScanAndDownloadAsync(
                        GameClientSelection.NGS_Full,
                        FileScanFlags.Default,
                        cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("\n测试已取消（达到测试限制）");
                }
            });

            // 等待下载 20 个文件后取消
            while (verifiedCount < 20 && !downloadTask.IsCompleted)
            {
                await Task.Delay(100);
            }

            if (verifiedCount >= 20)
            {
                Console.WriteLine($"\n已下载 {verifiedCount} 个文件，停止测试...");
                cts.Cancel();
            }

            await downloadTask;

            Console.WriteLine($"\n最终统计:");
            Console.WriteLine($"   扫描: {scannedCount} 个文件");
            Console.WriteLine($"   验证: {verifiedCount} 个文件");
            Console.WriteLine($"   失败: {failedCount} 个文件");
        }
        finally
        {
            // 清理测试目录
            try
            {
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, true);
                    Console.WriteLine($"\n已清理测试目录");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n清理测试目录失败: {ex.Message}");
            }
        }
    }
}
