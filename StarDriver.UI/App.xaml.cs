using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using StarDriver.UI.Views;

namespace StarDriver.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static AppSettings Settings { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 添加全局异常处理
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"[App] 未处理异常: {ex?.Message}");
            System.Diagnostics.Debug.WriteLine($"[App] 堆栈: {ex?.StackTrace}");
            System.Windows.MessageBox.Show($"应用程序遇到未处理的异常:\n\n{ex?.Message}\n\n{ex?.StackTrace}", 
                "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"[App] Dispatcher未处理异常: {args.Exception.Message}");
            System.Diagnostics.Debug.WriteLine($"[App] 堆栈: {args.Exception.StackTrace}");
            System.Windows.MessageBox.Show($"应用程序遇到未处理的异常:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", 
                "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        // 加载应用设置
        Settings = AppSettings.Load();

        // 显示主窗口（会根据首次运行状态显示欢迎页面或主页）
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 保存设置
        Settings?.Save();
        base.OnExit(e);
    }
}

