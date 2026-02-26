using StarDriver.Core.Models;
using StarDriver.Downloader;
using StarDriver.Test;

Console.WriteLine("=== StarDriver 测试套件 ===\n");
Console.WriteLine("选择测试:");
Console.WriteLine("1. HTTP 客户端测试");
Console.WriteLine("2. 下载引擎测试");
Console.WriteLine("3. 并发下载测试");
Console.WriteLine("4. 性能对比测试");
Console.WriteLine("5. 30GB 大文件下载测试");
Console.WriteLine("6. 完整游戏下载 (~90GB)");
Console.Write("\n请输入选项 (1-6): ");

var choice = Console.ReadLine();

try
{
    switch (choice)
    {
        case "1":
            await HttpClientTest.RunAsync();
            break;
        case "2":
            await DownloadEngineTest.RunAsync();
            break;
        case "3":
            await ConcurrentDownloadTest.RunAsync();
            break;
        case "4":
            await PerformanceComparisonTest.RunAsync();
            break;
        case "5":
            await LargeDownloadTest.RunAsync();
            break;
        case "6":
            await FullDownloadTest.RunAsync();
            break;
        default:
            Console.WriteLine("无效选项，运行默认测试...\n");
            await HttpClientTest.RunAsync();
            break;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\n✗ 测试失败: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

static class HttpClientTest
{
    public static async Task RunAsync()
    {
        Console.WriteLine("\n=== HTTP 客户端测试 ===\n");

        using var httpClient = new PSO2HttpClient();

        Console.WriteLine("1. 获取补丁根信息...");
        var rootInfo = await httpClient.GetPatchRootInfoAsync();
        Console.WriteLine($"   Patch URL: {rootInfo.PatchUrl}");
        Console.WriteLine($"   服务器配置 - 线程数: {rootInfo.ThreadNum}, 重试: {rootInfo.RetryNum}, 超时: {rootInfo.TimeOut}ms\n");

        Console.WriteLine("2. 获取 NGS 补丁列表...");
        var patchList = await httpClient.GetPatchListNGSAsync(rootInfo);
        Console.WriteLine($"   总文件数: {patchList.Count}\n");

        if (patchList.Count > 0)
        {
            Console.WriteLine("3. 测试单个文件下载...");
            var firstItem = patchList[0];
            Console.WriteLine($"   文件: {firstItem.GetFilenameWithoutAffix()}");
            Console.WriteLine($"   大小: {firstItem.FileSize:N0} bytes");
            Console.WriteLine($"   MD5: {firstItem.MD5Hash}");
            Console.WriteLine($"   URL: {firstItem.GetDownloadUrl()}\n");

            Console.WriteLine("4. 尝试打开下载流...");
            try
            {
                using var response = await httpClient.OpenForDownloadAsync(firstItem, false, CancellationToken.None);
                Console.WriteLine($"   ✓ 成功！状态码: {(int)response.StatusCode} {response.StatusCode}");
                Console.WriteLine($"   Content-Type: {response.Content.Headers.ContentType}");
                Console.WriteLine($"   Content-Length: {response.Content.Headers.ContentLength}\n");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"   ✗ HTTP 错误: {ex.Message}");
                Console.WriteLine($"   状态码: {ex.StatusCode}\n");
            }

            Console.WriteLine("5. 测试连续下载多个文件（模拟并发）...");
            var testFiles = patchList.Take(5).ToList();
            int successCount = 0;
            int failCount = 0;

            foreach (var item in testFiles)
            {
                try
                {
                    using var response = await httpClient.OpenForDownloadAsync(item, false, CancellationToken.None);
                    if (response.IsSuccessStatusCode)
                    {
                        successCount++;
                        Console.WriteLine($"   ✓ {item.GetFilenameWithoutAffix()} - {response.StatusCode}");
                    }
                    else
                    {
                        failCount++;
                        Console.WriteLine($"   ✗ {item.GetFilenameWithoutAffix()} - {response.StatusCode}");
                    }
                    
                    // 短暂延迟，避免速率限制
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    failCount++;
                    Console.WriteLine($"   ✗ {item.GetFilenameWithoutAffix()} - {ex.Message}");
                }
            }

            Console.WriteLine($"\n   结果: 成功 {successCount}/{testFiles.Count}, 失败 {failCount}");
        }

        Console.WriteLine("\n测试完成！");
    }
}
