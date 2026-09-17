using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SoftwareToolkit.Models;

/// <summary>
/// A process started from the tool library and its latest performance sample.
/// </summary>
public sealed class RunningToolProcess : INotifyPropertyChanged
{
    private double _cpuPercent;
    private long _memoryBytes;
    private TimeSpan _runningTime;

    internal RunningToolProcess(ToolDefinition tool, int processId, string processName, DateTime startedAt)
    {
        Tool = tool;
        ProcessId = processId;
        ProcessName = processName;
        StartedAt = startedAt;
    }

    public ToolDefinition Tool { get; }
    public string ToolName => Tool.Name;
    public ToolKind Kind => Tool.Kind;
    public int ProcessId { get; }
    public string ProcessName { get; }
    public DateTime StartedAt { get; }

    public double CpuPercent
    {
        get => _cpuPercent;
        internal set
        {
            if (Math.Abs(_cpuPercent - value) < 0.05) return;
            _cpuPercent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CpuText));
        }
    }

    public long MemoryBytes
    {
        get => _memoryBytes;
        internal set
        {
            if (_memoryBytes == value) return;
            _memoryBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MemoryText));
        }
    }

    public TimeSpan RunningTime
    {
        get => _runningTime;
        internal set
        {
            if (_runningTime == value) return;
            _runningTime = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RunningTimeText));
        }
    }

    public string CpuText => $"{CpuPercent:0.0}%";
    public string MemoryText => FormatBytes(MemoryBytes);
    public string RunningTimeText => RunningTime.TotalDays >= 1
        ? $"{(int)RunningTime.TotalDays}天 {RunningTime:hh\\:mm}"
        : RunningTime.ToString(@"hh\:mm\:ss");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d:0.0} MB";
        return $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
    }
}
