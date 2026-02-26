using System.Diagnostics;

namespace StarDriver.Core;

/// <summary>
/// PSO2:NGS 游戏启动器
/// </summary>
public sealed class GameLauncher
{
    private readonly string _gameDirectory;

    public GameLauncher(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentNullException(nameof(gameDirectory));

        _gameDirectory = gameDirectory;
    }

    /// <summary>
    /// 检查游戏是否已安装
    /// </summary>
    public bool IsGameInstalled()
    {
        // 检查 XignCode 版本的游戏文件（当前 PSO2:NGS 使用的反作弊）
        var xignCodeExe = Path.Combine(_gameDirectory, "sub", "pso2.exe");
        var xignCodeLoader = Path.Combine(_gameDirectory, "sub", "ucldr_PSO2_JP_loader_x64.exe");

        return File.Exists(xignCodeExe) && File.Exists(xignCodeLoader);
    }

    /// <summary>
    /// 获取游戏可执行文件路径
    /// </summary>
    public (string exePath, string? loaderPath) GetGameExecutablePaths()
    {
        // PSO2:NGS 使用 Wellbia XignCode 反作弊
        var exePath = Path.Combine(_gameDirectory, "sub", "pso2.exe");
        var loaderPath = Path.Combine(_gameDirectory, "sub", "ucldr_PSO2_JP_loader_x64.exe");

        return (exePath, loaderPath);
    }

    /// <summary>
    /// 启动游戏（不带登录令牌）
    /// </summary>
    public Process LaunchGame()
    {
        var (exePath, loaderPath) = GetGameExecutablePaths();

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"游戏可执行文件不存在: {exePath}");

        if (string.IsNullOrEmpty(loaderPath) || !File.Exists(loaderPath))
            throw new FileNotFoundException($"XignCode 加载器不存在: {loaderPath}");

        // 设置 XignCode 环境变量（必需）
        Environment.SetEnvironmentVariable("{2D9D60D7-F3E7-4018-9D5C-DE024497F7D2}", 
            "{17B73F61-20A0-4578-804E-6C2DF015B94D}", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("2cb236ea4d64138b478e01f9eb1e58d4", 
            "00007FF60BD50000", EnvironmentVariableTarget.Process);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = loaderPath,
                WorkingDirectory = _gameDirectory,
                UseShellExecute = true,
                Verb = "runas" // 以管理员权限运行（XignCode 需要）
            };

            // 添加启动参数
            startInfo.ArgumentList.Add(exePath);      // 游戏可执行文件路径
            startInfo.ArgumentList.Add("-reboot");    // NGS 模式
            startInfo.ArgumentList.Add("-optimize");  // 优化参数

            var process = Process.Start(startInfo);
            
            if (process == null)
                throw new InvalidOperationException("无法启动游戏进程");

            return process;
        }
        finally
        {
            // 清理环境变量
            Environment.SetEnvironmentVariable("{2D9D60D7-F3E7-4018-9D5C-DE024497F7D2}", 
                null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("2cb236ea4d64138b478e01f9eb1e58d4", 
                null, EnvironmentVariableTarget.Process);
        }
    }

    /// <summary>
    /// 检查游戏是否正在运行
    /// </summary>
    public static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("pso2");
        var isRunning = processes.Length > 0;
        
        // 释放进程句柄
        foreach (var proc in processes)
        {
            proc.Dispose();
        }

        return isRunning;
    }

    /// <summary>
    /// 尝试查找正在运行的游戏进程
    /// </summary>
    public Process? FindRunningGameProcess()
    {
        var (exePath, _) = GetGameExecutablePaths();
        var processes = Process.GetProcessesByName("pso2");

        if (processes.Length == 0)
            return null;

        // 如果有多个进程，返回第一个并释放其他的
        var result = processes[0];
        for (int i = 1; i < processes.Length; i++)
        {
            processes[i].Dispose();
        }

        return result;
    }
}
