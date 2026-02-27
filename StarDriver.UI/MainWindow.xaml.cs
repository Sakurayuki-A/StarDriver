using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.IO;
using StarDriver.Core.Models;
using StarDriver.Downloader;
using StarDriver.UI.ViewModels;
using StarDriver.UI.Views;
using StarDriver.UI.Controls;
using System.Diagnostics;
using MessageBox = System.Windows.MessageBox;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using Color = System.Windows.Media.Color;

namespace StarDriver.UI;

public partial class MainWindow : Window
{
    private GameDownloadEngine? _downloadEngine;
    private bool _isDownloading;
    private readonly Dictionary<string, long> _fileLastBytes = new(); // 跟踪每个文件的上次字节数
    
    private long _totalBytesDownloaded;
    private DateTime _lastSpeedUpdate = DateTime.Now;
    private readonly Stopwatch _downloadTimer = new();

    private readonly HomePage _homePage;
    private readonly DownloadsPage _downloadsPage;
    private readonly SettingsPage _settingsPage;
    private WelcomePage? _welcomePage;

    public MainWindow()
    {
        // 初始化页面
        _homePage = new HomePage();
        _downloadsPage = new DownloadsPage();
        _settingsPage = new SettingsPage();
        
        InitializeComponent();
        
        // 调试输出
        System.Diagnostics.Debug.WriteLine($"[MainWindow] IsFirstRun = {App.Settings.IsFirstRun}");
        
        // 检查是否首次运行
        if (App.Settings.IsFirstRun)
        {
            System.Diagnostics.Debug.WriteLine("[MainWindow] 显示欢迎页面");
            
            // 首次运行：先隐藏侧边栏，避免显示后再缩回的闪烁
            SidebarBorder.Visibility = Visibility.Collapsed;
            
            ShowWelcomePage();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[MainWindow] 显示主页");
            // 默认显示 Home 页面并选中按钮
            PageContent.Content = _homePage;
            HomeNavButton.IsChecked = true;
        }
    }

    /// <summary>
    /// 显示欢迎页面
    /// </summary>
    private void ShowWelcomePage()
    {
        _welcomePage = new WelcomePage();
        _welcomePage.WelcomeCompleted += OnWelcomeCompleted;
        _welcomePage.WelcomeSkipped += OnWelcomeSkipped;
        
        // 直接隐藏侧边栏，不播放动画（首次启动时）
        SidebarBorder.Visibility = Visibility.Collapsed;
        
        // 移除左侧 padding，让欢迎页面居中
        ContentBorder.Padding = new Thickness(30);
        
        // 显示欢迎页面
        PageContent.Content = _welcomePage;
    }

    /// <summary>
    /// 欢迎页面完成
    /// </summary>
    private void OnWelcomeCompleted(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[MainWindow] OnWelcomeCompleted 被调用");
        CompleteWelcome();
    }

    /// <summary>
    /// 跳过欢迎页面
    /// </summary>
    private void OnWelcomeSkipped(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[MainWindow] OnWelcomeSkipped 被调用");
        CompleteWelcome();
    }

    /// <summary>
    /// 完成欢迎流程，显示主界面
    /// </summary>
    private void CompleteWelcome()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[MainWindow] CompleteWelcome 开始执行");
            
            // 标记已完成首次运行
            App.Settings.MarkAsRun();
            
            System.Diagnostics.Debug.WriteLine("[MainWindow] 开始过渡动画");
            
            // 1. 欢迎页面淡出
            var fadeOutWelcome = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            
            fadeOutWelcome.Completed += (s, e) =>
            {
                // 2. 调整布局（在透明状态下，用户看不到）
                ContentBorder.Padding = new Thickness(230, 30, 30, 30);
                
                // 3. 刷新所有页面的游戏路径
                _homePage.RefreshGamePath();
                _downloadsPage.RefreshDownloadPath();
                
                // 4. 切换到主页
                PageContent.Content = _homePage;
                
                // 5. 临时取消事件订阅，避免触发 AnimatePageTransition
                HomeNavButton.Checked -= HomeNavButton_Checked;
                HomeNavButton.IsChecked = true;
                HomeNavButton.Checked += HomeNavButton_Checked;
                
                // 6. 主页淡入
                var fadeInHome = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(350),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                
                PageContent.BeginAnimation(OpacityProperty, fadeInHome);
                
                // 7. 同时播放侧边栏滑入动画
                AnimateSidebarIn();
            };
            
            PageContent.BeginAnimation(OpacityProperty, fadeOutWelcome);
            
            System.Diagnostics.Debug.WriteLine("[MainWindow] CompleteWelcome 执行完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] CompleteWelcome 异常: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MainWindow] 堆栈: {ex.StackTrace}");
            MessageBox.Show($"完成欢迎流程时出错: {ex.Message}\n\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 侧边栏滑入动画
    /// </summary>
    private void AnimateSidebarIn()
    {
        // 设置初始状态
        SidebarBorder.Visibility = Visibility.Visible;
        SidebarBorder.Opacity = 0;
        
        // 创建 TranslateTransform（如果不存在）
        if (SidebarBorder.RenderTransform is not TranslateTransform)
        {
            SidebarBorder.RenderTransform = new TranslateTransform();
        }
        
        var transform = (TranslateTransform)SidebarBorder.RenderTransform;
        transform.X = -200; // 设置初始位置
        
        // 从左侧滑入动画
        var slideAnimation = new DoubleAnimation
        {
            From = -200,  // 从左侧 200px 外开始
            To = 0,       // 滑到正常位置
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        // 淡入动画
        var fadeAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(500),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        // 播放动画
        transform.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
        SidebarBorder.BeginAnimation(OpacityProperty, fadeAnimation);
    }

    /// <summary>
    /// 侧边栏滑出动画
    /// </summary>
    private void AnimateSidebarOut(Action onCompleted)
    {
        // 创建 TranslateTransform（如果不存在）
        if (SidebarBorder.RenderTransform is not TranslateTransform)
        {
            SidebarBorder.RenderTransform = new TranslateTransform();
        }
        
        var transform = (TranslateTransform)SidebarBorder.RenderTransform;
        
        // 向左滑出动画
        var slideAnimation = new DoubleAnimation
        {
            From = 0,
            To = -200,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        
        // 淡出动画
        var fadeAnimation = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        
        // 动画完成后的回调
        slideAnimation.Completed += (s, e) =>
        {
            SidebarBorder.Visibility = Visibility.Collapsed;
            onCompleted?.Invoke();
        };
        
        // 播放动画
        transform.BeginAnimation(TranslateTransform.XProperty, slideAnimation);
        SidebarBorder.BeginAnimation(OpacityProperty, fadeAnimation);
    }

    /// <summary>
    /// 重新显示欢迎页面（从设置页面调用）
    /// </summary>
    public void ShowWelcomePageAgain()
    {
        _welcomePage = new WelcomePage();
        _welcomePage.WelcomeCompleted += (s, e) =>
        {
            // 完成后返回设置页面
            PageContent.Content = _settingsPage;
            SettingsNavButton.IsChecked = true;
            
            // 恢复内容区域 padding
            ContentBorder.Padding = new Thickness(230, 30, 30, 30);
            
            // 恢复侧边栏（带动画）
            AnimateSidebarIn();
        };
        _welcomePage.WelcomeSkipped += (s, e) =>
        {
            // 跳过后返回设置页面
            PageContent.Content = _settingsPage;
            SettingsNavButton.IsChecked = true;
            
            // 恢复内容区域 padding
            ContentBorder.Padding = new Thickness(230, 30, 30, 30);
            
            // 恢复侧边栏（带动画）
            AnimateSidebarIn();
        };
        
        // 隐藏侧边栏（带动画）
        AnimateSidebarOut(() =>
        {
            // 移除左侧 padding
            ContentBorder.Padding = new Thickness(30);
            
            // 动画完成后显示欢迎页面
            AnimatePageTransition(_welcomePage);
        });
    }

    /// <summary>
    /// 显示通知提示
    /// </summary>
    public void ShowToast(string title, string message, ToastNotification.ToastType type = ToastNotification.ToastType.Success, int durationMs = 3000)
    {
        Dispatcher.Invoke(() =>
        {
            var toast = new ToastNotification();
            ToastContainer.Children.Add(toast);
            toast.Show(title, message, type, durationMs);
        });
    }

    // 标题栏事件处理
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // 双击最大化/还原
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            // 单击拖动窗口
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HomeNavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (PageContent != null && _homePage != null)
        {
            AnimatePageTransition(_homePage);
        }
    }

    private void DownloadsNavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (PageContent != null && _downloadsPage != null)
        {
            AnimatePageTransition(_downloadsPage);
        }
    }

    private void SettingsNavButton_Checked(object sender, RoutedEventArgs e)
    {
        if (PageContent != null && _settingsPage != null)
        {
            AnimatePageTransition(_settingsPage);
        }
    }

    private void AnimatePageTransition(object newPage)
    {
        // 如果当前没有内容，直接设置
        if (PageContent.Content == null)
        {
            PageContent.Content = newPage;
            return;
        }

        // 淡出当前页面
        var fadeOut = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            // 切换页面
            PageContent.Content = newPage;

            // 淡入新页面
            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            PageContent.BeginAnimation(OpacityProperty, fadeIn);
        };

        PageContent.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void SwitchToDownloadsPage()
    {
        DownloadsNavButton.IsChecked = true;
    }

    public void StartDownload(string? gameDir = null)
    {
        if (_isDownloading)
            return;

        // 如果没有传入路径，尝试从设置中读取
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            gameDir = App.Settings.GameDirectory;
        }

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            MessageBox.Show("请先选择安装位置", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _isDownloading = true;
                    
                    // 自动切换到 Downloads 页面
                    DownloadsNavButton.IsChecked = true;
                    
                    // 更新连接状态
                    _downloadsPage.UpdateConnectionStatus("DOWNLOADING", 
                        new SolidColorBrush(Color.FromRgb(0, 102, 255)));

                    // 重置统计
                    _totalBytesDownloaded = 0;
                    _fileLastBytes.Clear();
                    _downloadTimer.Restart();

                    _downloadsPage.AppendLog("========================================");
                    _downloadsPage.AppendLog($"开始下载到: {gameDir}");
                    _downloadsPage.AppendLog("正在初始化下载引擎...");
                });

                // 创建下载引擎
                _downloadEngine = new GameDownloadEngine(gameDir);
                
                // 订阅事件
                _downloadEngine.ScanProgress += OnScanProgress;
                _downloadEngine.DownloadProgress += OnDownloadProgress;
                _downloadEngine.FileVerified += OnFileVerified;
                _downloadEngine.DownloadCompleted += OnDownloadCompleted;

                await Dispatcher.InvokeAsync(() =>
                {
                    _downloadsPage.AppendLog("开始扫描文件...");
                });

                // 开始下载
                await _downloadEngine.ScanAndDownloadAsync(
                    GameClientSelection.NGS_Full,
                    FileScanFlags.Default);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _downloadsPage.AppendLog($"✗ 错误: {ex.Message}");
                    MessageBox.Show($"下载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUI();
                });
            }
        });
    }

    public void StartVerification(string gameDir)
    {
        if (_isDownloading)
        {
            MessageBox.Show("已有任务正在进行中", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            MessageBox.Show("请先选择游戏路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _isDownloading = true;
                    
                    // 更新连接状态
                    _downloadsPage.UpdateConnectionStatus("VERIFYING", 
                        new SolidColorBrush(Color.FromRgb(255, 165, 0)));

                    // 重置统计
                    _totalBytesDownloaded = 0;
                    _fileLastBytes.Clear();
                    _downloadTimer.Restart();

                    _downloadsPage.AppendLog("========================================");
                    _downloadsPage.AppendLog($"开始验证游戏文件: {gameDir}");
                    _downloadsPage.AppendLog("正在初始化验证引擎...");
                });

                // 创建下载引擎（用于验证）
                _downloadEngine = new GameDownloadEngine(gameDir);
                
                // 订阅事件
                _downloadEngine.ScanProgress += OnScanProgress;
                _downloadEngine.DownloadProgress += OnDownloadProgress;
                _downloadEngine.FileVerified += OnFileVerified;
                _downloadEngine.DownloadCompleted += OnDownloadCompleted;

                await Dispatcher.InvokeAsync(() =>
                {
                    _downloadsPage.AppendLog("开始扫描和验证文件...");
                    _downloadsPage.AppendLog("提示: 这将检查所有文件的MD5哈希值");
                });

                // 开始验证（使用MD5检查）
                await _downloadEngine.ScanAndDownloadAsync(
                    GameClientSelection.NGS_Full,
                    FileScanFlags.MD5HashMismatch | FileScanFlags.FileSizeMismatch);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    _downloadsPage.AppendLog($"✗ 错误: {ex.Message}");
                    MessageBox.Show($"验证失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetUI();
                });
            }
        });
    }

    public void PauseDownload()
    {
        _downloadEngine?.Cancel();
        _downloadsPage.UpdateConnectionStatus("PAUSED", 
            new SolidColorBrush(Color.FromRgb(255, 165, 0)));
        _downloadsPage.AppendLog("用户暂停下载");
    }

    private void OnScanProgress(object? sender, ScanProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // 每100个文件记录一次日志
            if (e.ScannedFiles % 1000 == 0 || e.ScannedFiles == e.TotalFiles)
            {
                _downloadsPage.AppendLog($"扫描进度: {e.ScannedFiles}/{e.TotalFiles} ({e.ProgressPercentage:F1}%)");
            }
        });
    }

    private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var filename = Path.GetFileName(e.Item.LocalPath);
            
            // 使用 TaskID 直接映射到槽位（就像原始 launcher 一样）
            if (e.TaskId >= 0 && e.TaskId < _downloadsPage.DownloadTasks.Count)
            {
                var taskVM = _downloadsPage.DownloadTasks[e.TaskId];
                
                // 如果槽位是新激活的，设置文件名并记录日志
                if (!taskVM.IsActive)
                {
                    taskVM.FileName = filename;
                    taskVM.IsActive = true;
                    _fileLastBytes[filename] = 0;
                    _downloadsPage.AppendLog($"[Worker {e.TaskId}] 开始下载: {filename}");
                }
                
                taskVM.Progress = e.ProgressPercentage;
            }

            // 更新总下载量（计算该文件的增量）
            if (_fileLastBytes.TryGetValue(filename, out var lastBytes))
            {
                var delta = e.BytesDownloaded - lastBytes;
                _totalBytesDownloaded += delta;
                _fileLastBytes[filename] = e.BytesDownloaded;
            }
            else
            {
                _fileLastBytes[filename] = e.BytesDownloaded;
            }

            // 更新活动任务数
            var activeTasks = _downloadsPage.DownloadTasks.Count(t => t.IsActive);
            _downloadsPage.UpdateActiveTasks($"{activeTasks} / {_downloadsPage.DownloadTasks.Count}");

            // 更新下载速度（每秒更新一次）
            var now = DateTime.Now;
            if ((now - _lastSpeedUpdate).TotalSeconds >= 1)
            {
                var elapsed = _downloadTimer.Elapsed.TotalSeconds;
                if (elapsed > 0)
                {
                    var speed = _totalBytesDownloaded / elapsed;
                    _downloadsPage.UpdateDownloadSpeed($"{FormatBytes((long)speed)}/s");
                }
                _lastSpeedUpdate = now;
            }
        });
    }

    private void OnFileVerified(object? sender, FileVerificationEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var filename = Path.GetFileName(e.Item.LocalPath);
            
            // 使用 TaskID 直接访问槽位
            if (e.TaskId >= 0 && e.TaskId < _downloadsPage.DownloadTasks.Count)
            {
                var taskVM = _downloadsPage.DownloadTasks[e.TaskId];
                taskVM.Progress = e.IsValid ? 100 : 0;
                taskVM.IsActive = false;
                taskVM.FileName = "等待中...";
                
                // 记录完成日志
                var status = e.IsValid ? "✓ 成功" : "✗ 失败";
                _downloadsPage.AppendLog($"[Worker {e.TaskId}] {status}: {filename}");
            }
            
            // 清理字节跟踪
            _fileLastBytes.Remove(filename);
        });
    }

    private void OnDownloadCompleted(object? sender, DownloadCompletedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _downloadTimer.Stop();
            
            if (e.Success)
            {
                _downloadsPage.UpdateConnectionStatus("COMPLETED", 
                    new SolidColorBrush(Color.FromRgb(0, 255, 136)));
                
                _downloadsPage.AppendLog($"========================================");
                _downloadsPage.AppendLog($"任务完成！");
                _downloadsPage.AppendLog($"成功: {e.SucceededCount} 个文件");
                _downloadsPage.AppendLog($"失败: {e.FailedCount} 个文件");
                _downloadsPage.AppendLog($"========================================");
                
                // 判断是验证还是下载
                string taskType = e.SucceededCount == 0 && e.FailedCount == 0 ? "验证" : "下载";
                string message;
                
                if (e.SucceededCount == 0 && e.FailedCount == 0)
                {
                    message = "所有文件验证通过！\n\n游戏文件完整，无需修复。";
                }
                else
                {
                    message = $"{taskType}完成！\n\n成功: {e.SucceededCount} 个文件\n失败: {e.FailedCount} 个文件";
                }
                
                MessageBox.Show(
                    message,
                    "完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                _downloadsPage.UpdateConnectionStatus("CANCELLED", 
                    new SolidColorBrush(Color.FromRgb(255, 68, 68)));
                _downloadsPage.AppendLog("任务已取消");
            }

            ResetUI();
        });
    }

    private void ResetUI()
    {
        _isDownloading = false;
        
        // 清空任务列表
        foreach (var task in _downloadsPage.DownloadTasks)
        {
            task.IsActive = false;
            task.FileName = "等待中...";
            task.Progress = 0;
        }
        _fileLastBytes.Clear();
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloadEngine?.Dispose();
        base.OnClosed(e);
    }
}
