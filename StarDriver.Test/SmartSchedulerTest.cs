using StarDriver.Core.Models;
using StarDriver.Downloader;

namespace StarDriver.Test;

/// <summary>
/// 智能调度器测试
/// </summary>
public class SmartSchedulerTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 智能调度器测试 ===\n");
        
        // 测试场景：模拟PSO2补丁列表（大文件 + 小文件混合）
        var testItems = new List<DownloadItem>
        {
            // 大文件（会被分块）
            CreateTestItem("data0.pak", 150 * 1024 * 1024), // 150MB
            CreateTestItem("data1.pak", 80 * 1024 * 1024),  // 80MB
            CreateTestItem("data2.pak", 50 * 1024 * 1024),  // 50MB
            
            // 中等文件
            CreateTestItem("script.pak", 5 * 1024 * 1024),  // 5MB
            CreateTestItem("ui.pak", 3 * 1024 * 1024),      // 3MB
            
            // 小文件
            CreateTestItem("config.ini", 50 * 1024),        // 50KB
            CreateTestItem("version.txt", 10 * 1024),       // 10KB
            CreateTestItem("launcher.dll", 200 * 1024),     // 200KB
        };
        
        var scheduler = new SmartTaskScheduler(8);
        scheduler.InitializeTaskQueue(testItems);
        
        Console.WriteLine($"初始化完成，共 {scheduler.TotalTasksInQueue} 个任务\n");
        
        // 模拟节点工作
        Console.WriteLine("=== 模拟节点分配 ===\n");
        
        // 模拟不同速度的节点
        SimulateNode(scheduler, 0, NodeSpeedTier.Fast, 1500); // 1.5 MB/s
        SimulateNode(scheduler, 1, NodeSpeedTier.Fast, 1200); // 1.2 MB/s
        SimulateNode(scheduler, 2, NodeSpeedTier.Medium, 600); // 600 KB/s
        SimulateNode(scheduler, 3, NodeSpeedTier.Medium, 500); // 500 KB/s
        SimulateNode(scheduler, 4, NodeSpeedTier.Slow, 300);   // 300 KB/s
        SimulateNode(scheduler, 5, NodeSpeedTier.Slow, 200);   // 200 KB/s
        SimulateNode(scheduler, 6, NodeSpeedTier.Unknown, 0);  // 未知
        SimulateNode(scheduler, 7, NodeSpeedTier.Unknown, 0);  // 未知
        
        // 分配任务
        for (int i = 0; i < 10; i++)
        {
            var task = scheduler.AssignTaskToNode(i % 8);
            if (task != null)
            {
                Console.WriteLine($"Node {i % 8}: 分配任务 {Path.GetFileName(task.Item.LocalPath)} " +
                    $"({task.TaskSize / 1024.0:F1} KB, 优先级: {task.Priority})");
            }
        }
        
        Console.WriteLine($"\n{scheduler.GetStatistics()}");
        
        Console.WriteLine("\n=== 测试完成 ===");
    }
    
    private static DownloadItem CreateTestItem(string filename, long fileSize)
    {
        var patchInfo = new PatchListItem(
            remoteFilename: filename + ".pat",
            md5Hash: "00000000000000000000000000000000",
            fileSize: fileSize
        );
        
        return new DownloadItem(patchInfo, $"D:\\test\\{filename}");
    }
    
    private static void SimulateNode(SmartTaskScheduler scheduler, int nodeId, NodeSpeedTier tier, double speedKBps)
    {
        // 模拟速度更新
        var bytesDownloaded = (long)(speedKBps * 1024 * 2); // 2秒的数据量
        
        for (int i = 0; i < 5; i++)
        {
            scheduler.UpdateNodeSpeed(nodeId, bytesDownloaded * (i + 1));
            Thread.Sleep(100);
        }
    }
}
