namespace StarDriver.Core.Models;

/// <summary>
/// 表示补丁列表中的单个文件项
/// </summary>
public sealed class PatchListItem : IEquatable<PatchListItem>
{
    private const string AffixFilename = ".pat";
    
    /// <summary>远程文件名（包含 .pat 后缀）</summary>
    public string RemoteFilename { get; }
    
    /// <summary>MD5 哈希值</summary>
    public string MD5Hash { get; }
    
    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; }
    
    /// <summary>是否为补丁文件（true=patch, false=master）</summary>
    public bool? IsPatch { get; }
    
    /// <summary>是否为 NGS/Reboot 数据（true=NGS, false=Classic）</summary>
    public bool? IsRebootData { get; }
    
    /// <summary>补丁根信息</summary>
    public PatchRootInfo? RootInfo { get; }

    public PatchListItem(
        string remoteFilename, 
        string md5Hash, 
        long fileSize, 
        bool? isPatch = null,
        bool? isRebootData = null,
        PatchRootInfo? rootInfo = null)
    {
        RemoteFilename = remoteFilename ?? throw new ArgumentNullException(nameof(remoteFilename));
        MD5Hash = md5Hash ?? throw new ArgumentNullException(nameof(md5Hash));
        FileSize = fileSize;
        IsPatch = isPatch;
        IsRebootData = isRebootData;
        RootInfo = rootInfo;
    }

    /// <summary>获取不带 .pat 后缀的文件名</summary>
    public string GetFilenameWithoutAffix()
    {
        if (RemoteFilename.EndsWith(AffixFilename, StringComparison.OrdinalIgnoreCase))
        {
            return RemoteFilename[..^AffixFilename.Length];
        }
        return RemoteFilename;
    }

    /// <summary>获取下载 URL</summary>
    public Uri GetDownloadUrl(bool preferBackupServer = false)
    {
        if (RootInfo == null)
            throw new InvalidOperationException("RootInfo is required to generate download URL");

        string baseUrl;
        
        if (!IsPatch.HasValue || IsPatch.Value)
        {
            // 补丁文件
            baseUrl = preferBackupServer && !string.IsNullOrEmpty(RootInfo.BackupPatchUrl)
                ? RootInfo.BackupPatchUrl
                : RootInfo.PatchUrl;
        }
        else
        {
            // 基础文件
            baseUrl = preferBackupServer && !string.IsNullOrEmpty(RootInfo.BackupMasterUrl)
                ? RootInfo.BackupMasterUrl
                : RootInfo.MasterUrl;
        }

        // 确保 baseUrl 以 / 结尾，RemoteFilename 不以 / 开头
        baseUrl = baseUrl.TrimEnd('/');
        var filename = RemoteFilename.TrimStart('/');
        
        return new Uri($"{baseUrl}/{filename}", UriKind.Absolute);
    }

    /// <summary>从补丁列表行解析</summary>
    public static PatchListItem Parse(string line, bool? isRebootData = null, PatchRootInfo? rootInfo = null)
    {
        var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
        
        return parts.Length switch
        {
            3 => new PatchListItem(
                parts[0],
                parts[2],
                long.Parse(parts[1]),
                null,
                isRebootData,
                rootInfo),
            
            4 => new PatchListItem(
                parts[0],
                parts[1],
                long.Parse(parts[2]),
                parts[3][0] == 'p',
                isRebootData,
                rootInfo),
            
            _ => throw new FormatException($"Invalid patch list format: {line}")
        };
    }

    public bool Equals(PatchListItem? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(RemoteFilename, other.RemoteFilename, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => Equals(obj as PatchListItem);

    public override int GetHashCode() => 
        StringComparer.OrdinalIgnoreCase.GetHashCode(RemoteFilename);

    public override string ToString() => 
        $"{GetFilenameWithoutAffix()} ({FileSize:N0} bytes, MD5: {MD5Hash})";
}
