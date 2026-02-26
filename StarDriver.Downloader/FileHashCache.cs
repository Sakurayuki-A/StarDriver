using StarDriver.Core.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace StarDriver.Downloader;

/// <summary>
/// 文件哈希缓存，用于加速文件校验
/// </summary>
public sealed class FileHashCache : IDisposable
{
    private readonly string _cacheFilePath;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache;
    private bool _isDirty;

    public FileHashCache(string cacheFilePath)
    {
        _cacheFilePath = cacheFilePath ?? throw new ArgumentNullException(nameof(cacheFilePath));
        _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>加载缓存</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_cacheFilePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(_cacheFilePath, cancellationToken);
            var entries = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json);
            
            if (entries != null)
            {
                foreach (var kvp in entries)
                {
                    _cache.TryAdd(kvp.Key, kvp.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load cache: {ex.Message}");
        }
    }

    /// <summary>保存缓存</summary>
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!_isDirty)
            return;

        try
        {
            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await File.WriteAllTextAsync(_cacheFilePath, json, cancellationToken);
            _isDirty = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save cache: {ex.Message}");
        }
    }

    /// <summary>尝试获取缓存项</summary>
    public bool TryGet(string filename, out CacheEntry entry)
    {
        return _cache.TryGetValue(filename, out entry!);
    }

    /// <summary>设置缓存项</summary>
    public void Set(string filename, string md5Hash, long fileSize, DateTime lastModifiedUtc)
    {
        var entry = new CacheEntry
        {
            MD5Hash = md5Hash,
            FileSize = fileSize,
            LastModifiedUtc = lastModifiedUtc
        };
        
        _cache[filename] = entry;
        _isDirty = true;
    }

    /// <summary>验证文件是否与缓存匹配</summary>
    public bool IsValid(string filename, DateTime fileLastModifiedUtc, long fileSize)
    {
        if (!TryGet(filename, out var entry))
            return false;

        return entry.LastModifiedUtc == fileLastModifiedUtc && entry.FileSize == fileSize;
    }

    /// <summary>计算文件 MD5</summary>
    public static async Task<string> ComputeMD5Async(
        string filePath, 
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var md5 = MD5.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        
        var buffer = new byte[81920]; // 80KB buffer
        long totalRead = 0;
        int bytesRead;
        
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalRead += bytesRead;
            progress?.Report(totalRead);
        }
        
        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(md5.Hash!);
    }

    public void Dispose()
    {
        SaveAsync().GetAwaiter().GetResult();
    }

    /// <summary>缓存条目</summary>
    public class CacheEntry
    {
        public string MD5Hash { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }
}
