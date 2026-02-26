using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StarDriver.UI.ViewModels;

/// <summary>
/// 主窗口视图模型
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private string _totalDownloaded = "0 B";
    private int _activeTasks;
    private int _maxTasks = 8;
    private string _downloadSpeed = "0 MB/s";
    private bool _isConnected = true;

    public ObservableCollection<DownloadTaskViewModel> DownloadTasks { get; } = new();

    public string TotalDownloaded
    {
        get => _totalDownloaded;
        set
        {
            _totalDownloaded = value;
            OnPropertyChanged();
        }
    }

    public int ActiveTasks
    {
        get => _activeTasks;
        set
        {
            _activeTasks = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveTasksText));
        }
    }

    public int MaxTasks
    {
        get => _maxTasks;
        set
        {
            _maxTasks = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveTasksText));
        }
    }

    public string ActiveTasksText => $"{ActiveTasks} / {MaxTasks}";

    public string DownloadSpeed
    {
        get => _downloadSpeed;
        set
        {
            _downloadSpeed = value;
            OnPropertyChanged();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConnectionStatus));
        }
    }

    public string ConnectionStatus => IsConnected ? "LIVE CONNECTION" : "DISCONNECTED";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
