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
            var keepAliveTool = inventoryTools.SingleOrDefault(t => t.Id == "supabase-keepalive");
            Check(keepAliveTool is { Kind: ToolKind.Executable } && System.IO.File.Exists(keepAliveTool.Path), "Supabase keepalive tool is bundled and discoverable");
            var keepAliveDirectory = System.IO.Path.GetDirectoryName(keepAliveTool!.Path)!;
            Check(System.IO.File.Exists(System.IO.Path.Combine(keepAliveDirectory, "SupabaseKeepAlive.ps1")) &&
                  System.IO.File.Exists(System.IO.Path.Combine(keepAliveDirectory, "SupabaseKeepAliveWorker.ps1")),
                "Supabase keepalive UI and worker are bundled together");
            var keepAliveUi = System.IO.File.ReadAllText(System.IO.Path.Combine(keepAliveDirectory, "SupabaseKeepAlive.ps1"));
            var taskManagerUi = System.IO.File.ReadAllText(System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(keepAliveDirectory)!, "task-scheduler", "task-scheduler.ps1"));
            Check(keepAliveUi.Contains(@"$taskPath = '\SoftwareToolkit\'") && keepAliveUi.Contains("TaskManagerButton"),
                "Supabase keepalive plan is integrated with the shared task manager");
            Check(taskManagerUi.Contains("Description=$_.Description"),
                "task manager exposes descriptions for integrated tool tasks");
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

