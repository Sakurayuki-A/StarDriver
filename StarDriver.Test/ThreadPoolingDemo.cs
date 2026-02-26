using StarDriver.Core.Models;
using StarDriver.Downloader;

namespace StarDriver.Test;

/// <summary>
/// 智能线程池调配演示
/// </summary>
public class ThreadPoolingDemo
{
    public static async Task RunAsync()
    {
        Console.WriteLine("=== 智能线程池调配演示 ===\n");
        
        // 创建模拟的下载任务
        var items = new List<DownloadItem>();
        
        // 10个大文件 (100MB 每个)
        for (int i = 0; i < 10; i++)
        {
            var patch = new PatchListItem(
                $"data{i}.pak.pat",
                "00000000000000000000000000000000",
                100 * 1024 * 1024);
            items.Add(new DownloadItem(patch, $"D:\\test\\data{i}.pak"));
        }
        
        // 20个中等文件 (10MB 每个)
        for (int i = 0; i < 20; i++)
        {
            var patch = new PatchListItem(
                $"script{i}.pak.pat",
                "00000000000000000000000000000000",
                10 * 1024 * 1024);
            items.Add(new DownloadItem(patch, $"D:\\test\\script{i}.pak"));
        }
        
        // 50个小文件 (1MB 每个)
        for (int i = 0; i < 50; i++)
        {
            var patch = new PatchListItem(
                $"config{i}.ini.pat",
                "00000000000000000000000000000000",
                1 * 1024 * 1024);
            items.Add(new DownloadItem(patch, $"D:\\test\\config{i}.ini"));
        }
        
        Console.WriteLine($"创建了 {items.Count} 个模拟任务:");
        Console.WriteLine($"  大文件: 10 个 (100MB 每个) = 1000 MB");
        Console.WriteLine($"  中等文件: 20 个 (10MB 每个) = 200 MB");
        Console.WriteLine($"  小文件: 50 个 (1MB 每个) = 50 MB");
        Console.WriteLine($"  总计: 1250 MB\n");
        
        // 使用智能调度器
        var scheduler = new SmartDownloadScheduler();
        scheduler.Enqueue(items);
        
        Console.WriteLine($"调度器初始化完成:");
        Console.WriteLine($"  大文件队列: {scheduler.LargeFileCount}");
        Console.WriteLine($"  中等文件队列: {scheduler.MediumFileCount}");
        Console.WriteLine($"  小文件队列: {scheduler.SmallFileCount}\n");
        
        // 模拟线程池工作
        Console.WriteLine("=== 模拟线程池工作 ===\n");
        
        // 阶段1: 所有线程都在工作
        Console.WriteLine("阶段1: 初始状态 (0-5秒)");
        Console.WriteLine("大文件: ████████████████████████████████ (32线程忙碌)");
        Console.WriteLine("中等:   ████████████████████████████████ (32线程忙碌)");
        Console.WriteLine("小文件: ████████████████████████████████ (32线程忙碌)");
        Console.WriteLine($"队列: 大={scheduler.LargeFileCount}, 中={scheduler.MediumFileCount}, 小={scheduler.SmallFileCount}\n");
        
        await Task.Delay(2000);
        
        // 模拟大文件下载完成
        for (int i = 0; i < 10; i++)
        {
            scheduler.TryDequeueLarge(out _);
        }
        
        Console.WriteLine("阶段2: 大文件完成 (5-10秒)");
        Console.WriteLine("大文件: ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ (0线程，队列空)");
        Console.WriteLine("中等:   ████████████████████████████████████████████████ (32+16线程)");
        Console.WriteLine("小文件: ████████████████████████████████████████████████ (32+16线程)");
        Console.WriteLine($"队列: 大={scheduler.LargeFileCount}, 中={scheduler.MediumFileCount}, 小={scheduler.SmallFileCount}");
        Console.WriteLine("→ 32个大文件线程自动调配到中等和小文件\n");
        
        await Task.Delay(2000);
        
        // 模拟中等文件下载完成
        for (int i = 0; i < 20; i++)
        {
            scheduler.TryDequeueMedium(out _);
        }
        
        Console.WriteLine("阶段3: 中等文件完成 (10-15秒)");
        Console.WriteLine("大文件: ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ (0线程)");
        Console.WriteLine("中等:   ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ (0线程，队列空)");
        Console.WriteLine("小文件: ████████████████████████████████████████████████████████████████ (96线程)");
        Console.WriteLine($"队列: 大={scheduler.LargeFileCount}, 中={scheduler.MediumFileCount}, 小={scheduler.SmallFileCount}");
        Console.WriteLine("→ 所有线程集中处理小文件，最大化吞吐量\n");
        
        await Task.Delay(2000);
        
        // 模拟小文件下载完成
        for (int i = 0; i < 50; i++)
        {
            scheduler.TryDequeueSmall(out _);
        }
        
        Console.WriteLine("阶段4: 全部完成 (15秒)");
        Console.WriteLine("大文件: ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ (0线程)");
        Console.WriteLine("中等:   ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ (0线程)");
        Console.WriteLine("小文件: ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ (0线程)");
        Console.WriteLine($"队列: 大={scheduler.LargeFileCount}, 中={scheduler.MediumFileCount}, 小={scheduler.SmallFileCount}");
        Console.WriteLine("→ 所有任务完成！\n");
        
        Console.WriteLine("=== 性能对比 ===\n");
        
        Console.WriteLine("传统固定线程池 (32线程):");
        Console.WriteLine("  大文件: 1000MB / (32 * 2MB/s) = 15.6秒");
        Console.WriteLine("  中等文件: 200MB / (32 * 2MB/s) = 3.1秒");
        Console.WriteLine("  小文件: 50MB / (32 * 2MB/s) = 0.8秒");
        Console.WriteLine("  总耗时: 15.6 + 3.1 + 0.8 = 19.5秒\n");
        
        Console.WriteLine("智能线程池调配 (96线程):");
        Console.WriteLine("  阶段1 (并行): max(1000/64, 200/64, 50/64) = 15.6秒");
        Console.WriteLine("  阶段2 (调配): 剩余任务 / 96线程 = 2.6秒");
        Console.WriteLine("  总耗时: 15.6 + 2.6 = 18.2秒");
        Console.WriteLine("  性能提升: 6.7% (实际场景中提升更明显)\n");
        
        Console.WriteLine("=== 演示完成 ===");
        Console.WriteLine("\n提示: 实际下载中，性能提升取决于:");
        Console.WriteLine("  1. 文件大小分布");
        Console.WriteLine("  2. 网络带宽");
        Console.WriteLine("  3. 服务器响应速度");
        Console.WriteLine("  4. 磁盘I/O性能");
    }
}
