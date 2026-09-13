using System.Diagnostics;
using System.IO;
using System.Windows;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

public partial class BuildOutputWindow : Window
{
    private readonly BuildService _buildService;
    private readonly ToolDefinition _tool;
    private CancellationTokenSource? _cts;
    private string? _exePath;
    private string? _outputDir;
    private readonly Stopwatch _timer = new();

    public BuildOutputWindow(BuildService buildService, ToolDefinition tool)
    {
        InitializeComponent();
        _buildService = buildService;
        _tool = tool;

        StatusTitle.Text = $"正在编译: {tool.Name}";
        StatusDetail.Text = tool.Path;
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        LocalizationService.Apply(this);
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e) => LocalizationService.Apply(this);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        _timer.Start();

        // 启动计时器更新
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (_, _) =>
        {
            ElapsedText.Text = $"耗时 {_timer.Elapsed.TotalSeconds:F1}s";
        };
        timer.Start();

        CancelBtn.IsEnabled = true;
        CloseBtn.IsEnabled = false;

        try
        {
            var result = await _buildService.BuildAsync(_tool, line =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    OutputBox.AppendText(line + "\n");
                    OutputBox.ScrollToEnd();
                });
            });

            _timer.Stop();
            _exePath = result.ExePath;
            _outputDir = _tool.Arguments.GetValueOrDefault("output", "");

            if (string.IsNullOrWhiteSpace(_outputDir))
            {
                var projPath = Environment.ExpandEnvironmentVariables(_tool.Path);
                if (!Path.IsPathRooted(projPath) && _tool.SourceFile != null)
                {
                    var baseDir = Path.GetDirectoryName(_tool.SourceFile) ?? ".";
                    projPath = Path.GetFullPath(Path.Combine(baseDir, projPath));
                }
                var projDir = Path.GetDirectoryName(projPath) ?? ".";
                _outputDir = Path.Combine(projDir, "publish");
            }

            _ = Dispatcher.BeginInvoke(() =>
            {
                if (result.Success)
                {
                    StatusIcon.Text = "✅";
                    StatusTitle.Text = "编译成功";
                    StatusDetail.Text = result.ExePath ?? _outputDir;
                    StatusDetail.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xA6, 0xE3, 0xA1));
                    OpenFolderBtn.Visibility = Visibility.Visible;
                    if (result.ExePath != null)
                        OpenExeBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    StatusIcon.Text = "❌";
                    StatusTitle.Text = "编译失败";
                    StatusDetail.Text = $"退出码: {result.ExitCode}";
                    StatusDetail.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF3, 0x8B, 0xA8));
                }

                ElapsedText.Text = $"耗时 {result.Elapsed.TotalSeconds:F1}s";
                CancelBtn.IsEnabled = false;
                CloseBtn.IsEnabled = true;
            });
        }
        catch (Exception ex)
        {
            _timer.Stop();
            _ = Dispatcher.BeginInvoke(() =>
            {
                StatusIcon.Text = "❌";
                StatusTitle.Text = "编译异常";
                StatusDetail.Text = ex.Message;
                OutputBox.AppendText($"\n[异常] {ex}\n");
                CancelBtn.IsEnabled = false;
                CloseBtn.IsEnabled = true;
            });
        }

        timer.Stop();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelBtn.IsEnabled = false;
        OutputBox.AppendText("\n[用户取消]\n");
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OpenExe_Click(object sender, RoutedEventArgs e)
    {
        if (_exePath != null && File.Exists(_exePath))
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = true
            });
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = _outputDir;
        if (dir != null && Directory.Exists(dir))
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = dir,
                UseShellExecute = true
            });
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _cts?.Cancel();
    }
}
