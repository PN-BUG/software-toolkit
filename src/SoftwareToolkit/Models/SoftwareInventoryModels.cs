using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SoftwareToolkit.Models;

/// <summary>从当前 Windows 设备发现的软件条目。</summary>
public sealed class InstalledSoftware : INotifyPropertyChanged
{
    private bool _isIncluded;
    private string _downloadUrl = string.Empty;
    private bool _isDownloadUrlAutomatic = true;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? InstallDate { get; set; }
    public string? InstallLocation { get; set; }
    public string Source { get; set; } = "桌面应用";
    public string? PackageId { get; set; }
    public string? HomePageUrl { get; set; }

    [JsonIgnore]
    public string VersionDisplay => string.IsNullOrWhiteSpace(Version) ? "—" : Version;

    [JsonIgnore]
    public string PublisherDisplay => string.IsNullOrWhiteSpace(Publisher) ? "未知发布者" : Publisher;

    [JsonIgnore]
    public bool IsIncluded
    {
        get => _isIncluded;
        set => SetField(ref _isIncluded, value);
    }

    [JsonIgnore]
    public string DownloadUrl
    {
        get => _downloadUrl;
        set => SetField(ref _downloadUrl, value);
    }

    [JsonIgnore]
    public bool IsDownloadUrlAutomatic
    {
        get => _isDownloadUrlAutomatic;
        set => SetField(ref _isDownloadUrlAutomatic, value);
    }

    [JsonIgnore]
    public string LinkModeDisplay => IsDownloadUrlAutomatic ? "自动配置" : "手动修改";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyLinkModeChanged() => OnPropertyChanged(nameof(LinkModeDisplay));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>用户的软件清单持久化状态。</summary>
public sealed class SoftwareListState
{
    public string Title { get; set; } = "我的软件清单";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public List<SoftwareListEntry> Items { get; set; } = new();
}

public sealed class SoftwareListEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? PackageId { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public bool IsDownloadUrlAutomatic { get; set; } = true;
}
