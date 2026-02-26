namespace StarDriver.Core.Models;

/// <summary>
/// 补丁根信息，包含服务器 URL 配置和下载参数
/// </summary>
public sealed class PatchRootInfo
{
    /// <summary>补丁服务器 URL</summary>
    public string PatchUrl { get; }
    
    /// <summary>备用补丁服务器 URL</summary>
    public string? BackupPatchUrl { get; }
    
    /// <summary>主服务器 URL</summary>
    public string MasterUrl { get; }
    
    /// <summary>备用主服务器 URL</summary>
    public string? BackupMasterUrl { get; }
    
    /// <summary>并发线程数（服务器建议值）</summary>
    public int ThreadNum { get; }
    
    /// <summary>并行下载线程数</summary>
    public int ParallelThreadNum { get; }
    
    /// <summary>重试次数（服务器建议值）</summary>
    public int RetryNum { get; }
    
    /// <summary>超时时间（毫秒）</summary>
    public int TimeOut { get; }

    public PatchRootInfo(
        string patchUrl, 
        string masterUrl, 
        string? backupPatchUrl = null, 
        string? backupMasterUrl = null,
        int threadNum = 1,
        int parallelThreadNum = 1,
        int retryNum = 10,
        int timeOut = 30000)
    {
        PatchUrl = patchUrl ?? throw new ArgumentNullException(nameof(patchUrl));
        MasterUrl = masterUrl ?? throw new ArgumentNullException(nameof(masterUrl));
        BackupPatchUrl = backupPatchUrl;
        BackupMasterUrl = backupMasterUrl;
        ThreadNum = threadNum;
        ParallelThreadNum = parallelThreadNum;
        RetryNum = retryNum;
        TimeOut = timeOut;
    }

    /// <summary>从 management_beta.txt 解析</summary>
    public static PatchRootInfo Parse(string content)
    {
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var index = trimmed.IndexOf('=');
            if (index > 0)
            {
                var key = trimmed.Substring(0, index).Trim();
                var value = trimmed.Substring(index + 1).Trim();
                config[key] = value;
            }
        }

        var patchUrl = config.GetValueOrDefault("PatchURL");
        var masterUrl = config.GetValueOrDefault("MasterURL");

        if (string.IsNullOrEmpty(patchUrl) || string.IsNullOrEmpty(masterUrl))
            throw new FormatException("Invalid management_beta.txt format: missing PatchURL or MasterURL");

        return new PatchRootInfo(
            patchUrl,
            masterUrl,
            config.GetValueOrDefault("BackupPatchURL"),
            config.GetValueOrDefault("BackupMasterURL"),
            GetIntValue(config, "ThreadNum", 1),
            GetIntValue(config, "ParallelThreadNum", 1),
            GetIntValue(config, "RetryNum", 10),
            GetIntValue(config, "TimeOut", 30000)
        );
    }

    private static int GetIntValue(Dictionary<string, string> config, string key, int defaultValue)
    {
        if (config.TryGetValue(key, out var value) && int.TryParse(value, out var result))
        {
            return result;
        }
        return defaultValue;
    }

    public override string ToString() => 
        $"Patch: {PatchUrl}, Threads: {ThreadNum}, Retry: {RetryNum}";
}
