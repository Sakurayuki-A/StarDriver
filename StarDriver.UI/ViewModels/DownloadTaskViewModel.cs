using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StarDriver.UI.ViewModels;

/// <summary>
/// 单个下载任务的视图模型
/// </summary>
public class DownloadTaskViewModel : INotifyPropertyChanged
{
    private string _fileName = "Waiting...";
    private double _progress;
    private string _status = "Waiting...";
    private bool _isActive;
    private int _slotIndex;

    public string FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            OnPropertyChanged();
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string ProgressText => $"{Progress:F0}%";

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            OnPropertyChanged();
        }
    }

    public int SlotIndex
    {
        get => _slotIndex;
        set
        {
            _slotIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AnimationDelay));
        }
    }

    // 计算动画延迟：从下往上，每个槽位延迟0.3秒
    // 索引7（最下面）延迟0s，索引6延迟0.3s，...，索引0（最上面）延迟2.1s
    public double AnimationDelay => (7 - _slotIndex) * 0.3;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
