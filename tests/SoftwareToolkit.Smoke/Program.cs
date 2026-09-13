using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SoftwareToolkit;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args)!;
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
        try
        {
            if (args.Contains("--scan-inventory", StringComparer.OrdinalIgnoreCase))
            {
                var scanned = Task.Run(() => new SoftwareToolkit.Services.InstalledSoftwareScanner().ScanAsync())
                    .GetAwaiter().GetResult();
                Check(scanned.Count > 0, $"installed software scan returns results ({scanned.Count})");
                Check(scanned.All(item => !string.IsNullOrWhiteSpace(item.Name)),
                    "installed software scan returns named records");
                return 0;
            }
            if (args.Contains("--probe-ai", StringComparer.OrdinalIgnoreCase))
            {
                var probe = Task.Run(async () =>
                {
                    var serviceType = typeof(MainWindow).Assembly.GetType("SoftwareToolkit.Services.AiManagerService")!;
                    var service = Activator.CreateInstance(serviceType)!;
                    try
                    {
                        var floatingRefresh = serviceType.GetMethod("RefreshFloatingAsync")!;
                        var floatingTask = (Task)floatingRefresh.Invoke(service, new object[] { CancellationToken.None })!;
                        await floatingTask;
                        var floating = (AiFloatingSnapshot)floatingTask.GetType().GetProperty("Result")!.GetValue(floatingTask)!;
                        var refresh = serviceType.GetMethod("RefreshAsync")!;
                        var refreshTask = (Task)refresh.Invoke(service, new object[] { CancellationToken.None })!;
                        await refreshTask;
                        var snapshot = (AiManagerSnapshot)refreshTask.GetType().GetProperty("Result")!.GetValue(refreshTask)!;
                        return (snapshot, floating);
                    }
                    finally
                    {
                        await ((IAsyncDisposable)service).DisposeAsync();
                    }
                }).GetAwaiter().GetResult();
                var snapshot = probe.snapshot;
                Check(snapshot.Limits.Count > 0, $"Codex live usage probe returns {snapshot.Limits.Count} quota windows");
                Check(snapshot.CapturedAt > DateTimeOffset.Now.AddMinutes(-1), "Codex live usage probe returns a fresh snapshot");
                Check(probe.floating.RemainingPercent is >= 0 and <= 100, "Codex floating probe returns remaining quota");
                Check(!string.IsNullOrWhiteSpace(probe.floating.TaskName) &&
                      probe.floating.TaskName != "暂无最近任务" && probe.floating.TaskName != "未命名任务",
                    $"Codex floating probe returns task {probe.floating.TaskName}");
                Check(probe.floating.TaskStatus is not "notLoaded" and not "unknown",
                    $"Codex floating probe returns status {probe.floating.TaskStatus}");
                Check(probe.floating.Tasks.Count >= 2,
                    $"Codex floating probe returns {probe.floating.Tasks.Count} visible tasks");
                Check(!string.IsNullOrWhiteSpace(probe.floating.ModelName) && probe.floating.ModelName != "模型未知",
                    $"Codex floating probe returns model {probe.floating.ModelName}");
                Console.WriteLine($"INFO: optional data has {snapshot.DailyUsage.Count} daily buckets and {snapshot.Threads.Count} threads");
                Console.WriteLine($"INFO: thread statuses are {string.Join(", ", snapshot.Threads.GroupBy(item => item.Status).Select(group => $"{group.Key}:{group.Count()}"))}");
                Console.WriteLine($"INFO: floating task={probe.floating.TaskName}, status={probe.floating.TaskStatus}, model={probe.floating.ModelName}, effort={probe.floating.ReasoningEffort ?? "--"}");
                return 0;
            }
            typeof(MainWindow).GetField("_allTools", flags)!.SetValue(window, new List<ToolDefinition>
            {
                new() { Id = "test-a", Name = "Alpha 编辑器", Category = "开发", Description = "文本与代码编辑", Tags = new() { "Editor" } },
                new() { Id = "test-b", Name = "Beta 网络工具", Category = "网络", Description = "网络连接检查", IsPinned = true },
                new() { Id = "test-c", Name = "Gamma 文件管理", Category = "系统", Description = "整理常用文件", LastUsed = DateTime.Now }
            });
            Call("BuildCategoryTree");
            Call("ApplyFilters");
            var list = (ListBox)window.FindName("ToolCardsListBox");
            var search = (TextBox)window.FindName("SearchBox");
            var all = (CheckBox)window.FindName("BatchSelectAllCheck");
            var selected = Field<HashSet<string>>("_batchSelectedIds");
            Check(Field<System.Windows.Forms.NotifyIcon>("TrayIcon").ContextMenuStrip?.Items
                    .Cast<System.Windows.Forms.ToolStripItem>().Any(item => item.Text == "AI 悬浮监控") == true,
                "tray menu exposes AI floating monitor");
            Check(list.Items.Count == 3, "all tools are displayed");
            var panel = list.ItemsPanel;
            search.Text = "EDITOR";
            Call("ApplyFilters");
            Check(list.Items.Count == 1, "case-insensitive tag search");
            Check(ReferenceEquals(panel, list.ItemsPanel), "search reuses layout panel");
            search.Text = "no-match";
            Call("ApplyFilters");
            var empty = (Border)window.FindName("EmptyOverlay");
            Check(empty.Visibility == Visibility.Visible && empty.IsHitTestVisible, "empty state supports clicks");
            search.Clear(); Call("ApplyFilters");
            Call("BatchModeBtn_Click", window, new RoutedEventArgs());
            all.IsChecked = true;
            Check(selected.Count == 3, "select all selects every tool");
            var cb = new CheckBox { DataContext = list.Items[0] };
            Call("BatchCheckBox_Unchecked", cb, new RoutedEventArgs());
            Check(selected.Count == 2 && all.IsChecked == false, "unchecking one does not clear remaining selections");
            Call("BatchCheckBox_Checked", cb, new RoutedEventArgs());
            Check(selected.Count == 3 && all.IsChecked == true, "checking last tool synchronizes select all");
            Call("BatchCancel_Click", window, new RoutedEventArgs());
            Check(selected.Count == 0, "cancel clears selection");
            Call("ToggleLayout_Click", window, new RoutedEventArgs());
            list.ItemsPanel.Seal();
            Check(list.ItemsPanel.LoadContent() is VirtualizingStackPanel, "list view uses virtualization");
            Call("ToggleLayout_Click", window, new RoutedEventArgs());
            Check(list.Items.Count == 3, "layout toggle preserves results");
            var source = new TextBlock { DataContext = list.Items[1] };
            var click = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = source
            };
            Call("ToolCardsListBox_PreviewMouseLeftButtonUp", list, click);
            Check(ReferenceEquals(list.SelectedItem, source.DataContext), "single click selects a tool without launching");
            Check(((Border)window.FindName("SelectionPanel")).Visibility == Visibility.Visible, "selection exposes details and actions");
            var sort = (ComboBox)window.FindName("SortBox");
            sort.SelectedIndex = 3;
            Check(((ToolDefinition)list.Items[0]).IsPinned, "favorites-first sorting");
            Check(ReferenceEquals(list.SelectedItem, source.DataContext), "sorting preserves selected tool");
            sort.SelectedIndex = 2;
            Check(((ToolDefinition)list.Items[0]).Id == "test-c", "recent-use sorting");
            sort.SelectedIndex = 1;
            Check(((ToolDefinition)list.Items[0]).Id == "test-a", "name sorting");
            Call("BatchModeBtn_Click", window, new RoutedEventArgs());
            all.IsChecked = true;
            search.Text = "Alpha"; Call("ApplyFilters");
            Check(Field<bool>("_batchMode") && selected.SetEquals(new[] { "test-a" }), "filtering retains visible batch selection");
            search.Clear(); Call("ApplyFilters");
            Check(selected.Count == 1, "clearing search does not reselect hidden tools");
            Check(((Border)window.FindName("SelectionPanel")).Visibility == Visibility.Collapsed, "batch mode hides single-tool inspector");
            Call("BatchCancel_Click", window, new RoutedEventArgs());
            Check(!((Button)window.FindName("BatchDeleteButton")).IsEnabled, "destructive action disabled with no selection");
            var menuTool = (ToolDefinition)list.Items[0];
            var menu = (ContextMenu)Call("BuildCardContextMenu", menuTool);
            MenuItem ActionItem(ContextMenu context, string label) => context.Items.OfType<MenuItem>().Single(i => (string)i.Header == label);
            Check((string)menu.Tag == menuTool.Name, "context menu identifies its target");
            Check((string)((MenuItem)menu.Items[0]).Header == "启动工具", "launch is the first context action");
            Check((string)ActionItem(menu, "从工具箱移除…").Tag == "Danger", "remove action has danger styling");
            Check(!ActionItem(menu, "打开所在位置").IsEnabled, "missing path disables open location");
            var urlMenu = (ContextMenu)Call("BuildCardContextMenu", new ToolDefinition { Name = "Web", Kind = ToolKind.Url, Path = "https://example.com" });
            Check(ActionItem(urlMenu, "复制链接").IsEnabled && !ActionItem(urlMenu, "打开所在位置").IsEnabled, "web tools offer copy link without a local folder action");
            var commandMenu = (ContextMenu)Call("BuildCardContextMenu", new ToolDefinition { Name = "Command", Kind = ToolKind.Command, Path = "echo hello" });
            Check(ActionItem(commandMenu, "复制命令").IsEnabled, "command tools have an explicit copy-command action");
            var locationMethod = typeof(MainWindow).GetMethod("ResolveToolLocation", BindingFlags.Static | BindingFlags.NonPublic)!;
            string? Location(ToolDefinition tool) => (string?)locationMethod.Invoke(null, new object[] { tool });
            var assemblyPath = typeof(Program).Assembly.Location;
            Check(Location(new ToolDefinition { Path = System.IO.Path.GetFileName(assemblyPath), SourceFile = System.IO.Path.Combine(AppContext.BaseDirectory, "tools.json") }) == assemblyPath, "local paths resolve relative to their manifest");
            Check(Location(new ToolDefinition { Path = "%windir%\\System32\\kernel32.dll" }) != null, "environment variables resolve to existing files");
            Check(Location(new ToolDefinition { Path = "Z:\\missing-tool-123.exe" }) == null, "unavailable locations fail safely");
            var executableResolver = typeof(ToolLauncher).GetMethod("ResolveSystemToolPath", BindingFlags.Static | BindingFlags.NonPublic)!;
            string ExecutablePath(string path, string? sourceFile = null) =>
                (string)executableResolver.Invoke(null, new object?[] { path, sourceFile })!;
            var windowsDirectory = System.IO.Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.System))!;
            var explorerPath = System.IO.Path.Combine(windowsDirectory, "explorer.exe");
            Check(string.Equals(ExecutablePath("%windir%\\System32\\explorer.exe"), explorerPath, StringComparison.OrdinalIgnoreCase),
                "invalid legacy System32 path falls back to the Windows executable");
            Check(System.IO.File.Exists(ExecutablePath("notepad.exe", System.IO.Path.Combine(AppContext.BaseDirectory, "tools", "sample", "manifest.json"))),
                "bare executable names fall back to Windows and PATH");
            var originalPath = Environment.GetEnvironmentVariable("PATH");
            var pathProbeDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "software-toolkit-path-probe-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(pathProbeDirectory);
            try
            {
                var pathProbe = System.IO.Path.Combine(pathProbeDirectory, "portable-tool-probe.cmd");
                System.IO.File.WriteAllText(pathProbe, "@exit /b 0");
                Environment.SetEnvironmentVariable("PATH", pathProbeDirectory + System.IO.Path.PathSeparator + originalPath);
                Check(string.Equals(ExecutablePath("portable-tool-probe"), pathProbe, StringComparison.OrdinalIgnoreCase),
                    "extensionless executable aliases resolve through PATH and PATHEXT");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
                System.IO.Directory.Delete(pathProbeDirectory, true);
            }
            ActionItem(menu, "选择多个工具").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(Field<bool>("_batchMode") && selected.Contains(menuTool.Id!), "context menu enters batch mode with the target selected");
            var batchMenu = (ContextMenu)Call("BuildCardContextMenu", menuTool);
            Check(batchMenu.Items.OfType<MenuItem>().All(i => (string)i.Header != "启动工具") && ActionItem(batchMenu, "移除所选工具…").IsEnabled, "batch context actions apply to the selection");
            Call("BatchCancel_Click", window, new RoutedEventArgs());

            var parsedWinget = SoftwareToolkit.Services.InstalledSoftwareScanner.ParseWingetList(
                "名称                 ID                       版本\n" +
                "-------------------- ------------------------------ ----------\n" +
                "Visual Studio Code   Microsoft.VisualStudioCode     1.2.3\n");
            Check(parsedWinget.Count == 1 && parsedWinget[0].Id == "Microsoft.VisualStudioCode", "locale-neutral winget table parsing");
            var inventoryItem = new InstalledSoftware
            {
                Id = "sample", Name = "A&B Editor", Publisher = "Example <Org>", Version = "1.0",
                InstallLocation = @"C:\Private", PackageId = "Example.Editor", IsIncluded = true
            };
            inventoryItem.DownloadUrl = SoftwareToolkit.Services.InstalledSoftwareScanner.BuildAutomaticDownloadUrl(inventoryItem);
            Check(inventoryItem.DownloadUrl.Contains("Example.Editor"), "automatic download link uses matched package id");
            var shareHtml = SoftwareToolkit.Services.SoftwareListService.BuildShareHtml("开发清单", new[] { inventoryItem });
            Check(shareHtml.Contains("A&amp;B Editor") && !shareHtml.Contains(@"C:\Private"), "shared HTML escapes content and excludes private install paths");
            var inventoryTools = new SoftwareToolkit.Services.ConfigLoader(System.IO.Path.GetFullPath("src/SoftwareToolkit")).LoadAllTools();
            Check(inventoryTools.Any(t => t.Id == "software-inventory" && t.Kind == ToolKind.BuiltIn && t.Path == "software-inventory"),
                "software inventory manifest preserves its built-in id");
            Check(inventoryTools.Any(t => t.Id == "ai-manager" && t.Kind == ToolKind.BuiltIn && t.Path == "ai-manager"),
                "AI manager manifest preserves its built-in id");
            var fakeLocalAppData = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "software-toolkit-codex-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                var fakeVersionDirectory = System.IO.Path.Combine(fakeLocalAppData, "OpenAI", "Codex", "bin", "version-hash");
                System.IO.Directory.CreateDirectory(fakeVersionDirectory);
                var fakeCodex = System.IO.Path.Combine(fakeVersionDirectory, "codex.exe");
                System.IO.File.WriteAllBytes(fakeCodex, Array.Empty<byte>());
                var clientType = typeof(MainWindow).Assembly.GetType("SoftwareToolkit.Services.CodexAppServerClient")!;
                var resolveCodex = clientType.GetMethod("ResolveCodexExecutableFrom", BindingFlags.Static | BindingFlags.NonPublic)!;
                var resolvedCodex = (string?)resolveCodex.Invoke(null, new object?[]
                {
                    null, string.Empty, fakeLocalAppData, System.IO.Path.Combine(fakeLocalAppData, "Roaming"), fakeLocalAppData
                });
                Check(string.Equals(resolvedCodex, fakeCodex, StringComparison.OrdinalIgnoreCase),
                    "Codex CLI is found from the desktop version directory without PATH");
                var extractJson = clientType.GetMethod("ExtractJsonMessages", BindingFlags.Static | BindingFlags.NonPublic)!;
                var extracted = ((IEnumerable<string>)extractJson.Invoke(null, new object[]
                    { "startup log {\"id\":1,\"result\":{\"text\":\"brace } in string\"}}event {\"method\":\"ready\",\"params\":{}}" })!).ToList();
                Check(extracted.Count == 2 && extracted.All(json =>
                {
                    using var document = System.Text.Json.JsonDocument.Parse(json);
                    return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
                }), "Codex stdout parser tolerates logs and adjacent JSON messages");
            }
            finally
            {
                if (System.IO.Directory.Exists(fakeLocalAppData))
                    System.IO.Directory.Delete(fakeLocalAppData, recursive: true);
            }
            var aiManagerWindow = new AiManagerWindow();
            Check(aiManagerWindow.FindName("RemainingText") is TextBlock &&
                  aiManagerWindow.FindName("MainUsageBar") is ProgressBar &&
                  aiManagerWindow.FindName("WarningPercentBox") is TextBox &&
                  aiManagerWindow.FindName("PausePercentBox") is TextBox &&
                  aiManagerWindow.FindName("ThreadList") is ListBox &&
                  aiManagerWindow.FindName("FloatingWindowButton") is Button,
                "AI manager exposes quota, prediction policy, and task controls");
            var floatingWindow = new AiFloatingWindow();
            Check(floatingWindow.Width == 200 && floatingWindow.MinWidth == 180 &&
                  floatingWindow.MaxWidth == 260 && floatingWindow.Height == 98,
                "AI floating monitor uses an adaptive 180-260px compact layout");
            Check(floatingWindow.FindName("TaskStatusText") is TextBlock &&
                  floatingWindow.FindName("TaskNameText") is TextBlock &&
                  floatingWindow.FindName("RemainingQuotaText") is TextBlock &&
                  floatingWindow.FindName("ModelText") is TextBlock &&
                  floatingWindow.FindName("PeekHandle") is Border &&
                  floatingWindow.Topmost && !floatingWindow.ShowInTaskbar,
                "AI floating monitor exposes task, quota, and model telemetry");
            var dockEdgeField = typeof(AiFloatingWindow).GetField("_dockEdge", flags)!;
            var dockEdgeType = dockEdgeField.FieldType;
            object? CallFloating(string name, params object[] callArgs) =>
                typeof(AiFloatingWindow).GetMethod(name, flags)!.Invoke(floatingWindow, callArgs);
            dockEdgeField.SetValue(floatingWindow, Enum.Parse(dockEdgeType, "Right"));
            typeof(AiFloatingWindow).GetField("_expandedLeft", flags)!.SetValue(floatingWindow, SystemParameters.WorkArea.Right - floatingWindow.Width);
            typeof(AiFloatingWindow).GetField("_expandedTop", flags)!.SetValue(floatingWindow, SystemParameters.WorkArea.Top + 40);
            CallFloating("CollapseDocked", false);
            Check(Math.Abs(floatingWindow.Left - (SystemParameters.WorkArea.Right - 8)) < 0.1 &&
                  ((Border)floatingWindow.FindName("PeekHandle")).Visibility == Visibility.Visible &&
                  ((Border)floatingWindow.FindName("Shell")).Opacity == 0,
                "edge docking collapses to a clean 8px translucent hover handle");
            CallFloating("ExpandDocked", false);
            Check(Math.Abs(floatingWindow.Left - (SystemParameters.WorkArea.Right - floatingWindow.Width)) < 0.1 &&
                  ((Border)floatingWindow.FindName("PeekHandle")).Visibility == Visibility.Collapsed,
                "hover expansion restores the full floating monitor position");
            dockEdgeField.SetValue(floatingWindow, Enum.Parse(dockEdgeType, "Top"));
            typeof(AiFloatingWindow).GetField("_expandedLeft", flags)!.SetValue(floatingWindow, SystemParameters.WorkArea.Left + 80);
            typeof(AiFloatingWindow).GetField("_expandedTop", flags)!.SetValue(floatingWindow, SystemParameters.WorkArea.Top);
            CallFloating("CollapseDocked", false);
            var topPeek = (Border)floatingWindow.FindName("PeekHandle");
            var topSignal = (System.Windows.Shapes.Rectangle)floatingWindow.FindName("PeekSignal");
            Check(Math.Abs(floatingWindow.Top - (SystemParameters.WorkArea.Top - floatingWindow.Height + 8)) < 0.1 &&
                  topPeek.Width == 56 && topPeek.Height == 8 && topSignal.Width == 34 && topSignal.Height == 2,
                "top docking uses a horizontal 56x8 hover handle");
            CallFloating("ExpandDocked", false);
            var floatingSnapshot = new AiFloatingSnapshot
            {
                TaskName = "实现 AI 管理悬浮监控",
                TaskStatus = "inProgress",
                TaskStatusText = "推理中",
                ModelName = "GPT-6-Astra",
                ReasoningEffort = "high",
                RemainingPercent = 73.5,
                QuotaWindow = "Codex 总额度 · 7 天窗口",
                ResetsAt = DateTimeOffset.Now.AddDays(2),
                CapturedAt = DateTimeOffset.Now
            };
            typeof(AiFloatingWindow).GetMethod("RenderSnapshot", flags)!.Invoke(floatingWindow, new object[] { floatingSnapshot });
            Check(((TextBlock)floatingWindow.FindName("TaskNameText")).Text == floatingSnapshot.TaskName &&
                  ((TextBlock)floatingWindow.FindName("RemainingQuotaText")).Text == "73.5" &&
                  ((TextBlock)floatingWindow.FindName("ModelText")).Text == floatingSnapshot.ModelName,
                "AI floating monitor renders a telemetry snapshot");
            var compactWidth = floatingWindow.Width;
            var longSnapshot = new AiFloatingSnapshot
            {
                TaskName = new string('长', 64), TaskStatus = "inProgress", ModelName = "GPT-6-Astra",
                ReasoningEffort = "high", RemainingPercent = 73.5, CapturedAt = DateTimeOffset.Now,
                Tasks = new List<AiThreadSummary>
                {
                    new() { Title = new string('长', 64), Status = "inProgress", ModelName = "GPT-6-Astra" },
                    new() { Title = "修复顶部收起样式", Status = "completed", ModelName = "GPT-5.6-Sol" },
                    new() { Title = "验证额度读取", Status = "interrupted", ModelName = "GPT-5.6-Sol" }
                }
            };
            typeof(AiFloatingWindow).GetMethod("RenderSnapshot", flags)!.Invoke(floatingWindow, new object[] { longSnapshot });
            Check(floatingWindow.Width > compactWidth && floatingWindow.Width == floatingWindow.MaxWidth,
                "AI floating monitor expands for long text and respects its maximum width");
            Check(((StackPanel)floatingWindow.FindName("SecondaryTasksPanel")).Children.Count == 2 &&
                  floatingWindow.Height == 134,
                "AI floating monitor shows up to three tasks and adapts its height");
            typeof(AiFloatingWindow).GetMethod("RenderSnapshot", flags)!.Invoke(floatingWindow, new object[] { floatingSnapshot });
            LocalizationService.SetLanguage(LocalizationService.English);
            Check(((TextBlock)floatingWindow.FindName("TaskStatusText")).Text == "Running" &&
                  ((TextBlock)floatingWindow.FindName("RemainingQuotaText")).Text == "73.5",
                "English switch updates the floating monitor without losing telemetry");
            var settingsPanel = new SettingsPanel(new ConfigLoader(System.IO.Path.GetFullPath("src/SoftwareToolkit")));
            LocalizationService.Apply(settingsPanel);
            Check(settingsPanel.FindName("LanguageCombo") is ComboBox &&
                  settingsPanel.FindName("HotKeyHint") is TextBlock englishHint && englishHint.Text.StartsWith("Supported:"),
                "settings expose Chinese and English language selection");
            LocalizationService.SetLanguage(LocalizationService.Chinese);
            Check(((TextBlock)floatingWindow.FindName("TaskStatusText")).Text == "运行中",
                "switching back to Chinese updates open windows immediately");
            if (args.Length > 0)
            {
                var floatingRoot = (FrameworkElement)floatingWindow.Content;
                var floatingWidth = (int)floatingWindow.Width;
                var floatingHeight = (int)floatingWindow.Height;
                floatingRoot.Measure(new Size(floatingWidth, floatingHeight));
                floatingRoot.Arrange(new Rect(0, 0, floatingWidth, floatingHeight));
                floatingRoot.UpdateLayout();
                var floatingBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(floatingWidth, floatingHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                floatingBitmap.Render(floatingRoot);
                var floatingEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                floatingEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(floatingBitmap));
                using var floatingFile = System.IO.File.Create(System.IO.Path.ChangeExtension(args[0], null) + "-ai-floating.png");
                floatingEncoder.Save(floatingFile);
            }
            floatingWindow.Close();
            if (args.Length > 0)
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                var snapshot = new AiManagerSnapshot
                {
                    CapturedAt = DateTimeOffset.Now,
                    LifetimeTokens = 13_192_399_899,
                    ResetCredits = 2,
                    Limits = new List<AiLimitWindow>
                    {
                        new() { LimitId = "codex", Name = "Codex 总额度", WindowName = "7 天窗口", UsedPercent = 68, WindowDurationMinutes = 10080, ResetsAt = DateTimeOffset.Now.AddDays(3), IsPrimary = true },
                        new() { LimitId = "spark", Name = "GPT-5.3-Codex-Spark", WindowName = "5 小时窗口", UsedPercent = 42, WindowDurationMinutes = 300, ResetsAt = DateTimeOffset.Now.AddHours(2), IsPrimary = true }
                    },
                    DailyUsage = Enumerable.Range(0, 7).Select(i => new AiDailyUsage { Date = today.AddDays(i - 6), Tokens = 80_000_000 + i * 37_000_000L }).ToList(),
                    Threads = new List<AiThreadSummary>
                    {
                        new() { Id = "thr_1", Title = "重构图片下载器并验证发布包", Status = "active", UpdatedAt = DateTimeOffset.Now },
                        new() { Id = "thr_2", Title = "修复局域网共享页面的移动端布局", Status = "idle", UpdatedAt = DateTimeOffset.Now.AddHours(-2) },
                        new() { Id = "thr_3", Title = "检查 Unity 项目的渲染性能", Status = "notLoaded", UpdatedAt = DateTimeOffset.Now.AddDays(-1) }
                    }
                };
                typeof(AiManagerWindow).GetMethod("RenderSnapshot", flags)!.Invoke(aiManagerWindow, new object[] { snapshot });
                var managerRoot = (FrameworkElement)aiManagerWindow.Content;
                managerRoot.Measure(new Size(1180, 790));
                managerRoot.Arrange(new Rect(0, 0, 1180, 790));
                managerRoot.UpdateLayout();
                var managerBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1180, 790, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                managerBitmap.Render(managerRoot);
                var managerEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                managerEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(managerBitmap));
                using var managerFile = System.IO.File.Create(System.IO.Path.ChangeExtension(args[0], null) + "-ai-manager.png");
                managerEncoder.Save(managerFile);
            }
            aiManagerWindow.Close();
            var inventoryWindow = new SoftwareInventoryWindow();
            Check(inventoryWindow.FindName("InventoryGrid") is DataGrid && inventoryWindow.FindName("ListGrid") is DataGrid, "software inventory window exposes device and curated lists");
            inventoryWindow.Close();
            if (args.Length > 0)
            {
                // Render the actual bundled tools; fixtures above are only for behavioral checks.
                var actualTools = new SoftwareToolkit.Services.ConfigLoader(System.IO.Path.GetFullPath("src/SoftwareToolkit")).LoadAllTools();
                typeof(MainWindow).GetField("_allTools", flags)!.SetValue(window, actualTools);
                sort.SelectedIndex = 0;
                Call("BuildCategoryTree"); Call("ApplyFilters");
                var root = (FrameworkElement)window.Content;
                void Render(string suffix, int width, int height)
                {
                    root.Measure(new Size(width, height));
                    root.Arrange(new Rect(0, 0, width, height));
                    root.UpdateLayout();
                    root.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = System.IO.File.Create(System.IO.Path.ChangeExtension(args[0], null) + suffix + ".png");
                    encoder.Save(file);
                }
                Render("", 1220, 790);
                var firstCard = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
                var thirdCard = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(2);
                Check(Math.Abs(firstCard.TranslatePoint(new Point(), list).Y - thirdCard.TranslatePoint(new Point(), list).Y) < 1, "wide window fits three cards per row");
                var cardWidth = (double)window.Resources["ToolCardWidth"];
                list.SelectedIndex = 0;
                Render("-selected", 1220, 790);
                Call("BatchModeBtn_Click", window, new RoutedEventArgs());
                selected.Add(SoftwareToolkit.Services.ConfigLoader.GetEffectiveId((ToolDefinition)list.Items[0]));
                selected.Add(SoftwareToolkit.Services.ConfigLoader.GetEffectiveId((ToolDefinition)list.Items[1]));
                Call("RefreshVisualBatchStates"); Call("UpdateBatchBar");
                Render("-batch", 1220, 790);
                Call("BatchCancel_Click", window, new RoutedEventArgs());
                Render("-compact", 860, 620);
                Check((double)window.Resources["ToolCardWidth"] != cardWidth, "cards adapt to window width");
                var secondCard = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(1);
                Check(Math.Abs(firstCard.TranslatePoint(new Point(), list).Y - secondCard.TranslatePoint(new Point(), list).Y) < 1, "compact window fits two cards per row");
                Call("ToggleLayout_Click", window, new RoutedEventArgs());
                Render("-list", 1220, 790);
                search.Text = "no-match-for-preview"; Call("ApplyFilters");
                Render("-empty", 860, 620);
                var previewTool = actualTools.FirstOrDefault(t => t.Name == "MouseInc") ?? actualTools[0];
                var previewMenu = (ContextMenu)Call("BuildCardContextMenu", previewTool);
                previewMenu.Visibility = Visibility.Visible;
                previewMenu.ApplyTemplate();
                previewMenu.Measure(new Size(290, double.PositiveInfinity));
                previewMenu.Arrange(new Rect(new Point(), previewMenu.DesiredSize));
                previewMenu.UpdateLayout();
                Check(previewMenu.ActualHeight > 200, "context menu template renders all action groups");
                var menuBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(previewMenu.ActualWidth), (int)Math.Ceiling(previewMenu.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                menuBitmap.Render(previewMenu);
                var menuEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                menuEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(menuBitmap));
                using var menuFile = System.IO.File.Create(System.IO.Path.ChangeExtension(args[0], null) + "-context-menu.png");
                menuEncoder.Save(menuFile);

                var inventoryPreview = new SoftwareInventoryWindow();
                typeof(SoftwareInventoryWindow).GetField("_loaded", flags)!.SetValue(inventoryPreview, true);
                typeof(SoftwareInventoryWindow).GetField("_allSoftware", flags)!.SetValue(inventoryPreview, new List<InstalledSoftware>
                {
                    new() { Id = "vscode", Name = "Visual Studio Code", Publisher = "Microsoft", Version = "1.99.0", Source = "本机 · 64 位", PackageId = "Microsoft.VisualStudioCode", IsIncluded = true, DownloadUrl = "https://code.visualstudio.com/" },
                    new() { Id = "7zip", Name = "7-Zip", Publisher = "Igor Pavlov", Version = "24.09", Source = "本机 · 64 位", PackageId = "7zip.7zip", IsIncluded = true, DownloadUrl = "https://www.7-zip.org/" },
                    new() { Id = "powertoys", Name = "Microsoft PowerToys", Publisher = "Microsoft", Version = "0.90.0", Source = "当前用户", PackageId = "Microsoft.PowerToys", DownloadUrl = "https://github.com/microsoft/PowerToys/releases" },
                    new() { Id = "terminal", Name = "Windows Terminal", Publisher = "Microsoft", Version = "1.22", Source = "Microsoft Store / MSIX", PackageId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe", DownloadUrl = "https://apps.microsoft.com/" }
                });
                typeof(SoftwareInventoryWindow).GetMethod("RefreshViews", flags)!.Invoke(inventoryPreview, null);
                inventoryPreview.Show();
                inventoryPreview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                inventoryPreview.UpdateLayout();
                var inventoryBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(inventoryPreview.ActualWidth), (int)Math.Ceiling(inventoryPreview.ActualHeight),
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                inventoryBitmap.Render(inventoryPreview);
                var inventoryEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                inventoryEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(inventoryBitmap));
                using var inventoryFile = System.IO.File.Create(System.IO.Path.ChangeExtension(args[0], null) + "-inventory.png");
                inventoryEncoder.Save(inventoryFile);
                ((TabControl)inventoryPreview.FindName("Tabs")).SelectedIndex = 1;
                inventoryPreview.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                inventoryPreview.UpdateLayout();
                var listBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(inventoryPreview.ActualWidth), (int)Math.Ceiling(inventoryPreview.ActualHeight),
                    96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                listBitmap.Render(inventoryPreview);
                var listEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                listEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(listBitmap));
                using var listFile = System.IO.File.Create(System.IO.Path.ChangeExtension(args[0], null) + "-inventory-list.png");
                listEncoder.Save(listFile);
                inventoryPreview.Close();
            }
            Console.WriteLine("All interaction smoke checks passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Field<System.Windows.Forms.NotifyIcon>("TrayIcon").Dispose();
            Field<System.Windows.Threading.DispatcherTimer>("_searchTimer").Stop();
            app.Shutdown();
        }
    }
}

