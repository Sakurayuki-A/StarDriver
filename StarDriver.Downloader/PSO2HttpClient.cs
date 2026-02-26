using StarDriver.Core.Models;
using System.Net;
using System.Text;

namespace StarDriver.Downloader;

/// <summary>
/// PSO2 HTTP 客户端，负责获取补丁信息和下载文件
/// </summary>
public sealed class PSO2HttpClient : IDisposable
{
    private const string ManagementBetaUrl = "http://patch01.pso2gs.net/patch_prod/patches/management_beta.txt";
    private const string VersionUrl = "http://patch01.pso2gs.net/patch_prod/patches/version.ver";
    
    private readonly HttpClient _httpClient;
    private readonly string? _cacheDirectory;

    public PSO2HttpClient(HttpClient? httpClient = null, string? cacheDirectory = null, IWebProxy? proxy = null)
    {
        if (httpClient == null)
        {
            // 创建专用的 HttpClient，配置与官方启动器完全一致
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                UseProxy = proxy != null,
                Proxy = proxy,
                EnableMultipleHttp2Connections = true,
                UseCookies = true,
                Credentials = null,
                DefaultProxyCredentials = null,
                // 优化连接池配置 - 长时间下载稳定性优化
                MaxConnectionsPerServer = 28, // 28并发：大文件16 + 中等6 + 小文件6
                PooledConnectionLifetime = TimeSpan.FromMinutes(2), // 缩短到2分钟，定期刷新连接
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90), // 90秒空闲超时，快速释放
                ResponseDrainTimeout = TimeSpan.FromSeconds(5) // 响应排空超时，避免连接泄漏
            };
            _httpClient = new HttpClient(handler, true);
        }
        else
        {
            _httpClient = httpClient;
        }
        
        _cacheDirectory = cacheDirectory;
        
        // 设置 PSO2 服务器需要的请求头
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AQUA_HTTP");
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // 5分钟超时，避免连接长时间卡住
    }

    /// <summary>获取补丁根信息</summary>
    public async Task<PatchRootInfo> GetPatchRootInfoAsync(CancellationToken cancellationToken = default)
    {
        var content = await _httpClient.GetStringAsync(ManagementBetaUrl, cancellationToken);
        return PatchRootInfo.Parse(content);
    }

    /// <summary>获取远程版本号</summary>
    public async Task<PSO2Version> GetRemoteVersionAsync(CancellationToken cancellationToken = default)
    {
        // 先获取根信息
        var rootInfo = await GetPatchRootInfoAsync(cancellationToken);
        var versionUrl = $"{rootInfo.PatchUrl.TrimEnd('/')}/version.ver";
        
        var versionString = await _httpClient.GetStringAsync(versionUrl, cancellationToken);
        return new PSO2Version(versionString.Trim());
    }

    /// <summary>获取补丁列表</summary>
    public async Task<List<PatchListItem>> GetPatchListAsync(
        PatchRootInfo rootInfo,
        string patchListFilename,
        bool? isRebootData = null,
        CancellationToken cancellationToken = default)
    {
        var baseUri = new Uri(rootInfo.PatchUrl);
        var filelistUrl = new Uri(baseUri, patchListFilename);
        
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, filelistUrl);
            request.Headers.Host = filelistUrl.Host;
            request.Headers.Add("User-Agent", "AQUA_HTTP");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            request.Headers.Pragma.ParseAdd("no-cache");
            
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new HttpRequestException(
                    "403 Forbidden - PSO2服务器拒绝访问。\n" +
                    "可能原因：\n" +
                    "1. 地域限制：PSO2日服仅允许日本IP访问\n" +
                    "2. 需要使用日本VPN或代理\n" +
                    "3. CloudFront CDN访问限制\n\n" +
                    "建议：请连接日本VPN后重试",
                    null,
                    response.StatusCode);
            }
            
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            
            var items = new List<PatchListItem>();
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                    
                try
                {
                    var item = PatchListItem.Parse(trimmed, isRebootData, rootInfo);
                    items.Add(item);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse line: {trimmed}, Error: {ex.Message}");
                }
            }
            
            return items;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw; // 重新抛出我们自定义的403错误
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"获取补丁列表失败: {ex.Message}", ex, ex.StatusCode);
        }
    }

    /// <summary>获取 NGS 完整补丁列表（序章 + 主体）</summary>
    public async Task<List<PatchListItem>> GetPatchListNGSAsync(
        PatchRootInfo? rootInfo = null,
        CancellationToken cancellationToken = default)
    {
        rootInfo ??= await GetPatchRootInfoAsync(cancellationToken);
        
        // 使用字典去重，以文件名为键
        // 优先级：后面的覆盖前面的（reboot > prologue, launcher 独立）
        var itemsDict = new Dictionary<string, PatchListItem>(StringComparer.OrdinalIgnoreCase);
        
        // 1. 先加载 prologue（序章）
        var prologueList = await GetPatchListAsync(rootInfo, "patchlist_prologue.txt", true, cancellationToken);
        foreach (var item in prologueList)
        {
            var key = item.GetFilenameWithoutAffix();
            itemsDict[key] = item;
        }
        
        await Task.Delay(500, cancellationToken);
        
        // 2. 加载 reboot（主体），覆盖 prologue 中的重复项
        var rebootList = await GetPatchListAsync(rootInfo, "patchlist_reboot.txt", true, cancellationToken);
        foreach (var item in rebootList)
        {
            var key = item.GetFilenameWithoutAffix();
            itemsDict[key] = item; // 覆盖
        }
        
        await Task.Delay(500, cancellationToken);
        
        // 3. 加载 launcher，只添加不存在的（launcher 文件通常不重复）
        var launcherList = await GetPatchListAsync(rootInfo, "launcherlist.txt", null, cancellationToken);
        foreach (var item in launcherList)
        {
            var key = item.GetFilenameWithoutAffix();
            // 使用 TryAdd 避免覆盖已有的 NGS 文件
            if (!itemsDict.ContainsKey(key))
            {
                itemsDict[key] = item;
            }
        }
        
        Console.WriteLine($"[补丁列表] Prologue: {prologueList.Count}, Reboot: {rebootList.Count}, Launcher: {launcherList.Count}, 去重后: {itemsDict.Count}");
        
        return itemsDict.Values.ToList();
    }

    /// <summary>获取 NGS 主体补丁列表（不含序章）</summary>
    public async Task<List<PatchListItem>> GetPatchListNGSMainOnlyAsync(
        PatchRootInfo? rootInfo = null,
        CancellationToken cancellationToken = default)
    {
        rootInfo ??= await GetPatchRootInfoAsync(cancellationToken);
        
        // 使用字典去重
        var itemsDict = new Dictionary<string, PatchListItem>(StringComparer.OrdinalIgnoreCase);
        
        // 1. 加载 reboot（主体）
        var rebootList = await GetPatchListAsync(rootInfo, "patchlist_reboot.txt", true, cancellationToken);
        foreach (var item in rebootList)
        {
            var key = item.GetFilenameWithoutAffix();
            itemsDict[key] = item;
        }
        
        await Task.Delay(500, cancellationToken);
        
        // 2. 加载 launcher，只添加不存在的
        var launcherList = await GetPatchListAsync(rootInfo, "launcherlist.txt", null, cancellationToken);
        foreach (var item in launcherList)
        {
            var key = item.GetFilenameWithoutAffix();
            if (!itemsDict.ContainsKey(key))
            {
                itemsDict[key] = item;
            }
        }
        
        Console.WriteLine($"[补丁列表] Reboot: {rebootList.Count}, Launcher: {launcherList.Count}, 去重后: {itemsDict.Count}");
        
        return itemsDict.Values.ToList();
    }

    /// <summary>获取启动器文件列表</summary>
    public async Task<List<PatchListItem>> GetPatchListLauncherAsync(
        PatchRootInfo? rootInfo = null,
        CancellationToken cancellationToken = default)
    {
        rootInfo ??= await GetPatchRootInfoAsync(cancellationToken);
        return await GetPatchListAsync(rootInfo, "launcherlist.txt", null, cancellationToken);
    }

    /// <summary>打开文件下载流</summary>
    public async Task<HttpResponseMessage> OpenForDownloadAsync(
        PatchListItem item,
        bool preferBackupServer = false,
        CancellationToken cancellationToken = default)
    {
        var uri = item.GetDownloadUrl(preferBackupServer);
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        
        // 设置关键请求头（与官方启动器一致）
        request.Headers.Add("User-Agent", "AQUA_HTTP");
        request.Headers.Host = uri.Host;
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        request.Headers.Pragma.Add(new System.Net.Http.Headers.NameValueHeaderValue("no-cache"));
        
        return await _httpClient.SendAsync(
            request, 
            HttpCompletionOption.ResponseHeadersRead, 
            cancellationToken);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
