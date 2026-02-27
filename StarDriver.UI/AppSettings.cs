using System.IO;
using System.Text.Json;

namespace StarDriver.UI;

/// <summary>
/// 应用设置管理器
/// </summary>
public class AppSettings
{
    private static readonly string AppDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StarDriver");

    private static readonly string SettingsFilePath = Path.Combine(AppDataPath, "settings.json");
    private static readonly string FirstRunFlagFile = Path.Combine(AppDataPath, "StarDriver.firstrun");

    public bool IsFirstRun { get; set; } = true;
    public string? LastGamePath { get; set; }
    public string? GameDirectory { get; set; }
    public int ConcurrentDownloads { get; set; } = 16;
    public string Language { get; set; } = "zh-CN";
    public DateTime? LastRunTime { get; set; }
    
    // 更新检查设置
    public bool CheckForUpdatesAtStartup { get; set; } = true;
    public bool PromptBeforeUpdate { get; set; } = true;
    public string? LastKnownVersion { get; set; }

    /// <summary>
    /// 加载设置
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            // 确保目录存在
            Directory.CreateDirectory(AppDataPath);

            // 检查旧的首次运行标记文件（兼容性）
            var hasOldFlag = File.Exists(FirstRunFlagFile);
            
            System.Diagnostics.Debug.WriteLine($"[AppSettings] SettingsFilePath = {SettingsFilePath}");
            System.Diagnostics.Debug.WriteLine($"[AppSettings] File.Exists(SettingsFilePath) = {File.Exists(SettingsFilePath)}");
            System.Diagnostics.Debug.WriteLine($"[AppSettings] hasOldFlag = {hasOldFlag}");

            // 尝试加载设置文件
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                
                System.Diagnostics.Debug.WriteLine($"[AppSettings] 从文件加载，IsFirstRun = {settings.IsFirstRun}");
                
                // 如果有旧标记文件，标记为非首次运行
                if (hasOldFlag && settings.IsFirstRun)
                {
                    settings.IsFirstRun = false;
                }
                
                return settings;
            }

            // 如果有旧标记文件，创建新设置并标记为非首次运行
            if (hasOldFlag)
            {
                System.Diagnostics.Debug.WriteLine("[AppSettings] 有旧标记文件，IsFirstRun = false");
                return new AppSettings { IsFirstRun = false };
            }

            // 全新安装
            System.Diagnostics.Debug.WriteLine("[AppSettings] 全新安装，IsFirstRun = true");
            return new AppSettings();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppSettings] 加载失败: {ex.Message}");
            return new AppSettings();
        }
    }

    /// <summary>
    /// 保存设置
    /// </summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDataPath);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            File.WriteAllText(SettingsFilePath, json);

            // 同时创建旧的标记文件（兼容性）
            if (!IsFirstRun && !File.Exists(FirstRunFlagFile))
            {
                File.WriteAllText(FirstRunFlagFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    /// <summary>
    /// 标记为已运行
    /// </summary>
    public void MarkAsRun()
    {
        IsFirstRun = false;
        LastRunTime = DateTime.Now;
        Save();
    }
}
