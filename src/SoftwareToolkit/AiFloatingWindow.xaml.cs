using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

public partial class AiFloatingWindow : Window
{
    private static AiFloatingWindow? _instance;
    private readonly AiManagerService _service = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly DispatcherTimer _dockHideTimer = new() { Interval = TimeSpan.FromMilliseconds(820) };
    private readonly CancellationTokenSource _lifetime = new();
    private bool _refreshing;
    private AiFloatingSnapshot? _lastSnapshot;
    private DockEdge _dockEdge;
    private bool _isDockCollapsed;
    private double _expandedLeft;
    private double _expandedTop;
    private double _secondaryTaskNaturalWidth;

    private const double DockThreshold = 30;
    private const double PeekSize = 8;

    private enum DockEdge { None, Left, Right, Top }

    public AiFloatingWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
        _dockHideTimer.Tick += (_, _) =>
        {
            _dockHideTimer.Stop();
            if (_dockEdge != DockEdge.None && !IsMouseOver) CollapseDocked();
        };
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        LocalizationService.Apply(this);
    }

    public static void ShowOrActivate()
    {
        if (_instance is { IsVisible: true })
        {
            _instance.Activate();
            return;
        }
        _instance = new AiFloatingWindow();
        _instance.Show();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 22;
        Top = workArea.Bottom - Height - 22;
        _timer.Start();
        await RefreshAsync();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _dockHideTimer.Stop();
        LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        _lifetime.Cancel();
        await _service.DisposeAsync();
        _lifetime.Dispose();
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await _service.RefreshFloatingAsync(_lifetime.Token);
            RenderSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            RenderError(ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RenderSnapshot(AiFloatingSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        TaskNameText.Text = snapshot.TaskName;
        TaskNameText.ToolTip = snapshot.TaskName;
        TaskStatusText.Text = LocalizedStatus(snapshot.TaskStatus);
        RemainingQuotaText.Text = snapshot.RemainingPercent?.ToString("0.#", CultureInfo.InvariantCulture) ?? "--";
        RemainingQuotaText.ToolTip = snapshot.ResetsAt is { } reset
            ? LocalizationService.IsEnglish
                ? $"{snapshot.QuotaWindow}\nResets {reset.LocalDateTime:MMM d HH:mm}"
                : $"{snapshot.QuotaWindow}\n{reset.LocalDateTime:M月d日 HH:mm} 重置"
            : snapshot.QuotaWindow;
        ModelText.Text = snapshot.ModelName;
        ModelText.ToolTip = snapshot.ModelName;
        ReasoningText.Text = string.IsNullOrWhiteSpace(snapshot.ReasoningEffort)
            ? "--"
            : snapshot.ReasoningEffort.ToUpperInvariant();
        SyncAgeText.Text = snapshot.CapturedAt.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var tasks = snapshot.Tasks.Count > 0
            ? snapshot.Tasks
            : new List<AiThreadSummary>
            {
                new() { Title = snapshot.TaskName, Status = snapshot.TaskStatus, ModelName = snapshot.ModelName,
                    ReasoningEffort = snapshot.ReasoningEffort }
            };
        BuildSecondaryTasks(tasks);
        ResizeToTelemetry(tasks.Count);
        ApplyStatusVisual(snapshot.TaskStatus);
    }

    private void BuildSecondaryTasks(IReadOnlyList<AiThreadSummary> tasks)
    {
        SecondaryTasksPanel.Children.Clear();
        _secondaryTaskNaturalWidth = 0;
        foreach (var task in tasks.Skip(1).Take(2))
        {
            var color = Brush(StatusColor(task.Status));
            var row = new Grid { Height = 18 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new Ellipse { Width = 4, Height = 4, Fill = color, VerticalAlignment = VerticalAlignment.Center };
            var title = new TextBlock
            {
                Text = task.Title, Foreground = Brush("#B8C9C6"), FontSize = 8.5,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 5, 0)
            };
            Grid.SetColumn(title, 1);
            var status = new TextBlock
            {
                Text = LocalizedStatus(task.Status), Foreground = color, FontSize = 6.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(status, 2);
            row.Children.Add(dot);
            row.Children.Add(title);
            row.Children.Add(status);
            row.ToolTip = $"{task.Title}\n{LocalizedStatus(task.Status)} · {task.ModelName ?? "--"} {task.ReasoningEffort?.ToUpperInvariant()}";
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _secondaryTaskNaturalWidth = Math.Max(_secondaryTaskNaturalWidth, row.DesiredSize.Width);
            SecondaryTasksPanel.Children.Add(row);
        }
    }

    private void ResizeToTelemetry(int taskCount)
    {
        static double NaturalWidth(FrameworkElement element)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return element.DesiredSize.Width;
        }

        // Window/Shell borders, status rail and inner horizontal margins.
        const double chrome = 27;
        var top = 12 + NaturalWidth(BrandText) + 6 + NaturalWidth(SyncAgeText) + 36;
        var task = Math.Max(NaturalWidth(TaskStatusBadge) + 5 + NaturalWidth(TaskNameText),
            _secondaryTaskNaturalWidth);
        var reasoning = string.IsNullOrWhiteSpace(ReasoningText.Text) ? 0 : 4 + NaturalWidth(ReasoningText);
        var telemetry = 54 + 1 + 6 + NaturalWidth(ModelText) + reasoning;
        var targetWidth = Math.Clamp(Math.Ceiling(Math.Max(top, Math.Max(task, telemetry)) + chrome),
            MinWidth, MaxWidth);
        var targetHeight = 98 + Math.Clamp(taskCount - 1, 0, 2) * 18;
        if (Math.Abs(targetWidth - Width) < 0.5 && Math.Abs(targetHeight - Height) < 0.5) return;

        var workArea = SystemParameters.WorkArea;
        var oldRight = Left + Width;
        var oldBottom = Top + Height;
        Width = targetWidth;
        Height = targetHeight;
        switch (_dockEdge)
        {
            case DockEdge.Left:
                _expandedLeft = workArea.Left;
                _expandedTop = Math.Clamp(_expandedTop, workArea.Top, workArea.Bottom - Height);
                if (!_isDockCollapsed) { Left = _expandedLeft; Top = _expandedTop; }
                break;
            case DockEdge.Right:
                _expandedLeft = workArea.Right - Width;
                _expandedTop = Math.Clamp(_expandedTop, workArea.Top, workArea.Bottom - Height);
                if (!_isDockCollapsed) { Left = _expandedLeft; Top = _expandedTop; }
                break;
            case DockEdge.Top:
                _expandedLeft = Math.Clamp(_expandedLeft, workArea.Left, workArea.Right - Width);
                Left = _expandedLeft;
                Top = _isDockCollapsed ? workArea.Top - Height + PeekSize : workArea.Top;
                break;
            default:
                Left = Math.Clamp(oldRight - Width, workArea.Left, workArea.Right - Width);
                Top = Math.Clamp(oldBottom - Height, workArea.Top, workArea.Bottom - Height);
                break;
        }
    }

    private void RenderError(Exception ex)
    {
        TaskStatusText.Text = LocalizationService.IsEnglish ? "Connection issue" : "连接异常";
        TaskNameText.Text = ex is TimeoutException
            ? (LocalizationService.IsEnglish ? "Codex is responding slowly; retrying automatically" : "Codex 响应较慢，将自动重试")
            : (LocalizationService.IsEnglish ? "Codex status is temporarily unavailable" : "暂时无法读取 Codex 状态");
        TaskNameText.ToolTip = ex.Message;
        SyncAgeText.Text = "RETRY";
        ApplyStatusVisual("systemError");
    }

    private void ApplyStatusVisual(string status)
    {
        var color = StatusColor(status);
        var badge = status switch
        {
            "active" or "inProgress" => "#173A32",
            "waitingOnApproval" or "waitingOnUserInput" => "#3A311D",
            "failed" or "systemError" => "#3B2428",
            "interrupted" => "#302842",
            _ => "#26343A"
        };
        StatusRail.Background = Brush(color);
        StatusDot.Fill = Brush(color);
        PeekSignal.Fill = Brush(color);
        TaskStatusText.Foreground = Brush(color);
        TaskStatusBadge.Background = Brush(badge);
    }

    private static string StatusColor(string status) => status switch
    {
        "active" or "inProgress" => "#38D7B2",
        "waitingOnApproval" or "waitingOnUserInput" => "#F1C46C",
        "failed" or "systemError" => "#F2777A",
        "interrupted" => "#B99CE8",
        _ => "#71888A"
    };

    private void Shell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            _dockHideTimer.Stop();
            StopPositionAnimations();
            _isDockCollapsed = false;
            // 拖动期间先解除吸附，避免鼠标离开旧边缘时触发延迟收起。
            _dockEdge = DockEdge.None;
            PeekHandle.Visibility = Visibility.Collapsed;
            DragMove();
            DetectDockAfterDrag();
        }
    }

    private void DetectDockAfterDrag()
    {
        var workArea = SystemParameters.WorkArea;
        var distances = new (DockEdge Edge, double Distance)[]
        {
            (DockEdge.Left, Math.Abs(Left - workArea.Left)),
            (DockEdge.Right, Math.Abs(Left + Width - workArea.Right)),
            (DockEdge.Top, Math.Abs(Top - workArea.Top))
        };
        var nearest = distances.OrderBy(item => item.Distance).First();
        if (nearest.Distance > DockThreshold)
        {
            _dockEdge = DockEdge.None;
            PeekHandle.Visibility = Visibility.Collapsed;
            return;
        }

        _dockEdge = nearest.Edge;
        _expandedLeft = _dockEdge switch
        {
            DockEdge.Left => workArea.Left,
            DockEdge.Right => workArea.Right - Width,
            _ => Math.Clamp(Left, workArea.Left, workArea.Right - Width)
        };
        _expandedTop = _dockEdge == DockEdge.Top
            ? workArea.Top
            : Math.Clamp(Top, workArea.Top, workArea.Bottom - Height);
        ConfigurePeekHandle();
        AnimatePosition(_expandedLeft, _expandedTop, 130);
    }

    private void CollapseDocked(bool animate = true)
    {
        if (_dockEdge == DockEdge.None || _isDockCollapsed) return;
        var workArea = SystemParameters.WorkArea;
        var targetLeft = _dockEdge switch
        {
            DockEdge.Left => workArea.Left - Width + PeekSize,
            DockEdge.Right => workArea.Right - PeekSize,
            _ => _expandedLeft
        };
        var targetTop = _dockEdge == DockEdge.Top
            ? workArea.Top - Height + PeekSize
            : _expandedTop;

        _isDockCollapsed = true;
        ConfigurePeekHandle();
        PeekHandle.Visibility = Visibility.Visible;
        if (animate)
        {
            AnimatePosition(targetLeft, targetTop, 180, HideDockedShell);
        }
        else
        {
            SetPosition(targetLeft, targetTop);
            HideDockedShell();
        }
    }

    private void ExpandDocked(bool animate = true)
    {
        if (_dockEdge == DockEdge.None || !_isDockCollapsed) return;
        _isDockCollapsed = false;
        Shell.Opacity = 1;
        Shell.IsHitTestVisible = true;
        if (animate)
        {
            AnimatePosition(_expandedLeft, _expandedTop, 180, () => PeekHandle.Visibility = Visibility.Collapsed);
        }
        else
        {
            SetPosition(_expandedLeft, _expandedTop);
            PeekHandle.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfigurePeekHandle()
    {
        PeekHandle.ToolTip = LocalizationService.IsEnglish ? "Hover to expand" : "悬停展开";
        PeekHandle.Width = _dockEdge == DockEdge.Top ? 56 : PeekSize;
        PeekHandle.Height = _dockEdge == DockEdge.Top ? PeekSize : 56;
        PeekSignal.Width = _dockEdge == DockEdge.Top ? 34 : 2;
        PeekSignal.Height = _dockEdge == DockEdge.Top ? 2 : 34;
        PeekHandle.HorizontalAlignment = _dockEdge switch
        {
            DockEdge.Left => HorizontalAlignment.Right,
            DockEdge.Right => HorizontalAlignment.Left,
            _ => HorizontalAlignment.Center
        };
        PeekHandle.VerticalAlignment = _dockEdge == DockEdge.Top
            ? VerticalAlignment.Bottom
            : VerticalAlignment.Center;
        PeekHandle.CornerRadius = _dockEdge switch
        {
            DockEdge.Left => new CornerRadius(0, 6, 6, 0),
            DockEdge.Top => new CornerRadius(0, 0, 5, 5),
            _ => new CornerRadius(6, 0, 0, 6)
        };
    }

    private void HideDockedShell()
    {
        Shell.Opacity = 0;
        Shell.IsHitTestVisible = false;
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _dockHideTimer.Stop();
        if (_isDockCollapsed) ExpandDocked();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_dockEdge == DockEdge.None || _isDockCollapsed) return;
        _dockHideTimer.Stop();
        _dockHideTimer.Start();
    }

    private void PeekHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dockHideTimer.Stop();
        ExpandDocked();
        e.Handled = true;
    }

    private void AnimatePosition(double targetLeft, double targetTop, int milliseconds, Action? completed = null)
    {
        StopPositionAnimations();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        var leftAnimation = new DoubleAnimation(Left, targetLeft, duration) { EasingFunction = easing };
        var topAnimation = new DoubleAnimation(Top, targetTop, duration) { EasingFunction = easing };
        leftAnimation.Completed += (_, _) =>
        {
            SetPosition(targetLeft, targetTop);
            completed?.Invoke();
        };
        BeginAnimation(LeftProperty, leftAnimation);
        BeginAnimation(TopProperty, topAnimation);
    }

    private void StopPositionAnimations()
    {
        var currentLeft = Left;
        var currentTop = Top;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = currentLeft;
        Top = currentTop;
    }

    private void SetPosition(double left, double top)
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Left = left;
        Top = top;
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Content = Topmost ? "●" : "○";
        PinButton.ToolTip = LocalizationService.T(Topmost ? "取消置顶" : "保持置顶");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static SolidColorBrush Brush(string value) => new((Color)ColorConverter.ConvertFromString(value));

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        LocalizationService.Apply(this);
        if (_dockEdge != DockEdge.None) ConfigurePeekHandle();
        if (_lastSnapshot is { } snapshot) RenderSnapshot(snapshot);
    }

    private static string LocalizedStatus(string status) => status switch
    {
        "active" or "inProgress" => LocalizationService.IsEnglish ? "Running" : "运行中",
        "waitingOnApproval" => LocalizationService.IsEnglish ? "Awaiting approval" : "等待批准",
        "waitingOnUserInput" => LocalizationService.IsEnglish ? "Awaiting input" : "等待输入",
        "failed" or "systemError" => LocalizationService.IsEnglish ? "Failed" : "失败",
        "interrupted" => LocalizationService.IsEnglish ? "Interrupted" : "已中断",
        "idle" or "completed" => LocalizationService.IsEnglish ? "Completed" : "已完成",
        "notLoaded" => LocalizationService.IsEnglish ? "Unavailable" : "未加载",
        _ => LocalizationService.IsEnglish ? "Unknown" : "未知"
    };
}
