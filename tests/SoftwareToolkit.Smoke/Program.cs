using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SoftwareToolkit;
using SoftwareToolkit.Models;

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
            Check(window.FindName("RunningToolsList") is ItemsControl &&
                  ((Border)window.FindName("RunningToolsEmpty")).Visibility == Visibility.Visible,
                "home dashboard exposes the running-tool process list and empty state");
            using (var monitor = new SoftwareToolkit.Services.RunningToolMonitor())
            {
                monitor.Track(new ToolDefinition { Name = "Smoke process" }, System.Diagnostics.Process.GetCurrentProcess());
                monitor.Refresh();
                Check(monitor.Items.Count == 1 && monitor.Items[0].ProcessId == Environment.ProcessId &&
                      monitor.Items[0].MemoryBytes > 0,
                    "running-tool monitor samples live process performance");
            }
            var dashboardMonitor = Field<SoftwareToolkit.Services.RunningToolMonitor>("_runningToolMonitor");
            dashboardMonitor.Track(new ToolDefinition { Name = "Dashboard smoke process" }, System.Diagnostics.Process.GetCurrentProcess());
            Call("RefreshRunningTools");
            window.Measure(new Size(1280, 900));
            window.Arrange(new Rect(0, 0, 1280, 900));
            window.UpdateLayout();
            Check(((ItemsControl)window.FindName("RunningToolsList")).Items.Count == 1,
                "running dashboard renders read-only process properties without crashing");
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
            Check(inventoryTools.Any(t => t.Id == "software-inventory" && t.Kind == ToolKind.BuiltIn), "software inventory manifest is discoverable");
            var lanShareTool = inventoryTools.SingleOrDefault(t => t.Id == "lan-share");
            Check(lanShareTool is { Kind: ToolKind.Executable, RunAsAdmin: true } && System.IO.File.Exists(lanShareTool.Path),
                "LAN share tool is bundled and requests LAN listener privileges");
            var lanShareDirectory = System.IO.Path.GetDirectoryName(lanShareTool!.Path)!;
            var lanShareHtml = System.IO.File.ReadAllText(System.IO.Path.Combine(lanShareDirectory, "index.html"));
            var lanShareServer = System.IO.File.ReadAllText(System.IO.Path.Combine(lanShareDirectory, "lan-share.ps1"));
            Check(lanShareHtml.Contains("btnDiscover") && lanShareHtml.Contains("connectRequestModal") &&
                  lanShareHtml.Contains("/connect/request") && lanShareHtml.Contains("/connect/respond"),
                "LAN share UI exposes discovery and connection requests");
            Check(lanShareServer.Contains("/api/connect/events") && lanShareServer.Contains("/api/connect/deliver") &&
                  lanShareServer.Contains("lan-share-device.json"),
                "LAN share server supports connection handshakes and stable device identity");
            Check(lanShareHtml.Contains("height:100dvh") && lanShareHtml.Contains("safe-area-inset-bottom") &&
                  lanShareHtml.Contains("scroll-snap-type:x proximity"),
                "LAN share UI adapts to mobile viewports and safe areas");
            Check(lanShareHtml.Contains("*::-webkit-scrollbar") &&
                  lanShareHtml.Contains("scrollbar-width:thin") &&
                  lanShareHtml.Contains("--scroll-thumb-hover") &&
                  lanShareHtml.Contains("scrollbar-gutter:stable"),
                "LAN share uses themed scrollbars across desktop, mobile, and dark mode");
            Check(lanShareHtml.Contains("localTransfers") && lanShareHtml.Contains("X-Transfer-Id") &&
                  lanShareHtml.Contains("transfer-section-title") && lanShareServer.Contains("New-TransferRecord") &&
                  lanShareServer.Contains("Copy-StreamWithProgress"),
                "LAN share transfer list includes live upload, chat-file, and download activity");
            Check(lanShareHtml.Contains("chatAttachmentPanel") && lanShareHtml.Contains("chatDropOverlay") &&
                  lanShareHtml.Contains("pendingChatFiles") && lanShareHtml.Contains("id=\"chatFileInput\" style=\"display:none\" multiple") &&
                  lanShareHtml.Contains("X-Message-Id") && lanShareHtml.Contains("deliveryStatus:\"sending\"") &&
                  lanShareHtml.Contains("stopImmediatePropagation") &&
                  lanShareServer.Contains("existingMessage") && lanShareServer.Contains("duplicateFileMessage"),
                "LAN chat supports queued multi-file previews, drag-and-drop, optimistic status, enter-to-send, and idempotency");
            var lanBridgeTool = inventoryTools.SingleOrDefault(t => t.Id == "lan-connection-bridge");
            Check(lanBridgeTool is { Kind: ToolKind.Executable } && System.IO.File.Exists(lanBridgeTool.Path),
                "Android LAN connection bridge is independently discoverable");
            var lanBridgeDirectory = System.IO.Path.GetDirectoryName(lanBridgeTool!.Path)!;
            var lanBridgeApk = System.IO.Path.Combine(lanBridgeDirectory, "DandelionLanding.apk");
            var lanBridgeDownloadPage = System.IO.File.ReadAllText(System.IO.Path.Combine(lanShareDirectory, "bridge.html"));
            var lanBridgeManifest = System.IO.File.ReadAllText(System.IO.Path.Combine(lanBridgeDirectory, "android-src", "AndroidManifest.xml"));
            var lanBridgeService = System.IO.File.ReadAllText(System.IO.Path.Combine(lanBridgeDirectory, "android-src", "java", "com", "softwaretoolkit", "lanbridge", "BridgeService.java"));
            Check(System.IO.File.Exists(lanBridgeApk) && new System.IO.FileInfo(lanBridgeApk).Length > 10_000 &&
                  lanBridgeManifest.Contains("BridgeService") && lanBridgeManifest.Contains("POST_NOTIFICATIONS") &&
                  lanBridgeManifest.Contains("BOOT_COMPLETED"),
                "Android bridge APK bundles foreground listening, notifications, and boot recovery");
            Check(lanBridgeTool.Name == "蒲公英降落台" && lanShareHtml.Contains("btnBridge") &&
                  lanShareHtml.Contains("/bridge") && lanShareHtml.Contains("bridgeId") &&
                  lanShareServer.Contains("/api/bridge-apk") && lanShareServer.Contains("originUrl") &&
                  lanBridgeService.Contains("connect-notification"),
                "LAN share integrates optional bridge discovery, download, and browser handoff");
            Check(lanBridgeDownloadPage.Contains("下载到当前设备") &&
                  lanBridgeDownloadPage.Contains("扫码下载到另一台设备") &&
                  lanBridgeDownloadPage.Contains("DandelionLanding.apk") &&
                  lanBridgeDownloadPage.Contains("localDownloadUrl=location.origin") &&
                  lanBridgeDownloadPage.Contains("info.lanUrl"),
                "Dandelion Landing uses a dedicated local and QR download page");
            Check(lanShareServer.Contains("GetFileName($scriptLocation) -eq 'lan-share'") &&
                  lanShareServer.Contains("GetFileName($toolsDirectory) -eq 'tools'") &&
                  !lanShareServer.Contains("$SharePath = (Get-Location).Path"),
                "LAN share default root is stable when elevated outside System32");
            var keepAliveTool = inventoryTools.SingleOrDefault(t => t.Id == "supabase-keepalive");
            Check(keepAliveTool is { Kind: ToolKind.Executable } && System.IO.File.Exists(keepAliveTool.Path), "Supabase keepalive tool is bundled and discoverable");
            var keepAliveDirectory = System.IO.Path.GetDirectoryName(keepAliveTool!.Path)!;
            Check(System.IO.File.Exists(System.IO.Path.Combine(keepAliveDirectory, "SupabaseKeepAlive.ps1")) &&
                  System.IO.File.Exists(System.IO.Path.Combine(keepAliveDirectory, "SupabaseKeepAliveWorker.ps1")),
                "Supabase keepalive UI and worker are bundled together");
            var keepAliveUi = System.IO.File.ReadAllText(System.IO.Path.Combine(keepAliveDirectory, "SupabaseKeepAlive.ps1"));
            var taskSchedulerDirectory = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(keepAliveDirectory)!, "task-scheduler");
            var taskManagerUi = System.IO.File.ReadAllText(System.IO.Path.Combine(taskSchedulerDirectory, "task-scheduler.ps1"));
            Check(keepAliveUi.Contains(@"$taskPath = '\SoftwareToolkit\'") && keepAliveUi.Contains("TaskManagerButton"),
                "Supabase keepalive plan is integrated with the shared task manager");
            Check(taskManagerUi.Contains("Description=$_.Description"),
                "task manager exposes descriptions for integrated tool tasks");
            var keepAliveManifest = System.IO.File.ReadAllText(System.IO.Path.Combine(keepAliveDirectory, "manifest.json"));
            Check(taskManagerUi.Contains("PresetBox") &&
                  taskManagerUi.Contains("SoftwareToolkit 任务") &&
                  !taskManagerUi.Contains("（包括 Supabase 保活）"),
                "task manager offers a preset selector without a Supabase-specific heading");
            Check(keepAliveManifest.Contains("schedulePresets") && keepAliveManifest.Contains("Supabase KeepAlive") &&
                  taskManagerUi.Contains("Load-Presets"),
                "Supabase keepalive publishes a discoverable scheduled-task preset");
            Check(keepAliveManifest.Contains("\"program\": \"SupabaseKeepAliveWorker.ps1\"") &&
                  taskManagerUi.Contains("$manifestPath.DirectoryName"),
                "scheduled-task presets resolve bundled programs relative to their tool directory");
            var taskScheduler = inventoryTools.Single(t => t.Id == "task-scheduler");
            Check(System.IO.File.Exists(taskScheduler.Path) &&
                  taskScheduler.Path.EndsWith("powershell.exe", StringComparison.OrdinalIgnoreCase) &&
                  taskScheduler.Path.Contains("WindowsPowerShell", StringComparison.OrdinalIgnoreCase) &&
                  taskScheduler.Args?.Contains("-WindowStyle Hidden") == true,
                "task manager resolves the system PowerShell and launches without a companion CMD window");
            Check(System.IO.File.Exists(System.IO.Path.Combine(taskSchedulerDirectory, "task-scheduler.vbs")) &&
                  System.IO.File.ReadAllText(System.IO.Path.Combine(taskSchedulerDirectory, "task-scheduler.bat"))
                      .Contains("wscript.exe", StringComparison.OrdinalIgnoreCase),
                "task manager includes a standalone elevated launcher without a persistent console");
            Check(keepAliveManifest.Contains("logPath") && taskManagerUi.Contains("LogButton") &&
                  taskManagerUi.Contains("Get-PresetForTask"),
                "task manager discovers and opens logs declared by tool presets");
            var keepAliveWorker = System.IO.File.ReadAllText(System.IO.Path.Combine(keepAliveDirectory, "SupabaseKeepAliveWorker.ps1"));
            Check(keepAliveManifest.Contains("argumentsTemplate") && keepAliveManifest.Contains("configPath") &&
                  taskManagerUi.Contains("ConfigPathBox") && taskManagerUi.Contains("BrowseConfigButton") &&
                  taskManagerUi.Contains("ArgumentsPanel") && taskManagerUi.Contains("$argumentsPanel.Visibility='Collapsed'"),
                "Supabase preset uses one configuration picker and hides its generated command argument");
            Check(keepAliveWorker.Contains("'password'") && keepAliveWorker.Contains("'apikey'") &&
                  keepAliveWorker.Contains("LegacyPlaintext"),
                "Supabase worker accepts original plaintext configuration with an explicit warning");
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

