namespace StarDriver.Core.Models;

/// <summary>
/// 游戏客户端选择
/// </summary>
public enum GameClientSelection
{
    /// <summary>NGS 完整版（序章 + 主体，约100GB）</summary>
    NGS_Full,
    
    /// <summary>NGS 主体（不含序章，约60GB，可能缺少基础文件）</summary>
    NGS_MainOnly,
    
    /// <summary>仅启动器文件</summary>
    Launcher_Only
}

/// <summary>
/// 文件扫描标志
/// </summary>
[Flags]
public enum FileScanFlags
{
    None = 0,
    
    /// <summary>仅扫描缺失文件</summary>
    MissingFilesOnly = 1 << 0,
    
    /// <summary>MD5 哈希不匹配时重新下载</summary>
    MD5HashMismatch = 1 << 1,
    
    /// <summary>文件大小不匹配时重新下载</summary>
    FileSizeMismatch = 1 << 2,
    
    /// <summary>强制刷新缓存</summary>
    ForceRefreshCache = 1 << 3,
    
    /// <summary>仅使用缓存</summary>
    CacheOnly = 1 << 4,
    
    /// <summary>默认扫描（MD5 + 文件大小）</summary>
    Default = MD5HashMismatch | FileSizeMismatch
}
