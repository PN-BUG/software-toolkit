using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

[RequiresUnreferencedCode("工具启动和插件系统依赖运行时反射")]
public partial class MainWindow : Window
{
    private readonly ConfigLoader _configLoader;
    private readonly ToolLauncher _toolLauncher;
    internal readonly System.Windows.Forms.NotifyIcon TrayIcon = new() { Text = "SoftwareToolkit" };
    private List<ToolDefinition> _allTools = new();
    private readonly ObservableCollection<ToolDefinition> _filteredTools = new();

    private int _hotKeyId = 9001;
    private bool _realClose;
    private bool _isListView; // 布局模式: false=卡片, true=列表

    // 批量操作
    private bool _batchMode;
    private bool _syncingBatchSelection;
    private bool? _renderedListView;
    private readonly System.Windows.Threading.DispatcherTimer _searchTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };
    private readonly HashSet<string> _batchSelectedIds = new();

    // 静态命令用于 XAML 绑定
    public static readonly ICommand ShowWindowCommand = new RoutedCommand();

    // 常量
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;

    // 任务栏图标相关
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private System.Drawing.Icon? _taskbarIcon; // 保持引用防止 GC 回收导致图标失效

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void SetTaskbarIcon(IntPtr hwnd)
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/icon.ico", UriKind.Absolute);
            var iconStream = Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
            {
                _taskbarIcon = new System.Drawing.Icon(iconStream);
                SendMessage(hwnd, WM_SETICON, ICON_SMALL, _taskbarIcon.Handle);
                SendMessage(hwnd, WM_SETICON, ICON_BIG, _taskbarIcon.Handle);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskbarIcon] 设置任务栏图标失败: {ex.Message}");
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplyFilters(); };
        PreviewKeyDown += Window_PreviewKeyDown;
        Closed += (_, _) =>
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            _searchTimer.Stop();
            UnregisterHotKey();
            TrayIcon.Visible = false;
            TrayIcon.Icon?.Dispose();
            TrayIcon.ContextMenuStrip?.Dispose();
            TrayIcon.Dispose();
            _taskbarIcon?.Dispose();
        };

        _configLoader = new ConfigLoader();
        _toolLauncher = new ToolLauncher(_configLoader, IntPtr.Zero);
        var trayMenu = new System.Windows.Forms.ContextMenuStrip();
        trayMenu.Items.Add("显示主窗口", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        trayMenu.Items.Add("AI 悬浮监控", null, (_, _) => Dispatcher.Invoke(AiFloatingWindow.ShowOrActivate));
        trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => TrayExit_Click(this, new RoutedEventArgs())));
        TrayIcon.ContextMenuStrip = trayMenu;
        TrayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);

        // 绑定 ShowWindowCommand
        CommandBindings.Add(new CommandBinding(ShowWindowCommand, (s, e) => ShowFromTray()));

        // 从嵌入式资源加载托盘图标
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/icon.ico", UriKind.Absolute);
            var iconStream = Application.GetResourceStream(iconUri)?.Stream;
            if (iconStream != null)
            {
                TrayIcon.Icon = new System.Drawing.Icon(iconStream);
                TrayIcon.Visible = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TrayIcon] 加载图标失败: {ex.Message}");
        }

        // 在窗口句柄创建后设置任务栏图标
        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetTaskbarIcon(hwnd);
        };

        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        ApplyLocalization(reloadTools: false);
    }

    // ====== 窗口事件 ======

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        helper.EnsureHandle();

        LoadTools();
        BuildCategoryTree();
        RefreshToolCards();
        LocalizationService.Apply(this);
        RegisterHotKey();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        var state = _configLoader.LoadUserState();
        if (state.MinimizeToTray && !_realClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            var state = _configLoader.LoadUserState();
            if (state.MinimizeToTray)
                Hide();
        }
    }

    // ====== 托盘事件 ======

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void TrayShow_Click(object sender, RoutedEventArgs e) => ShowFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _realClose = true;
        UnregisterHotKey();
        WpfApplication.Current.Shutdown();
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e) => ApplyLocalization(reloadTools: true);

    private void ApplyLocalization(bool reloadTools)
    {
        LocalizationService.Apply(this);
        if (TrayIcon.ContextMenuStrip is { Items.Count: >= 4 } menu)
        {
            menu.Items[0].Text = LocalizationService.T("显示主窗口");
            menu.Items[1].Text = LocalizationService.T("AI 悬浮监控");
            menu.Items[3].Text = LocalizationService.T("退出");
        }
        if (!reloadTools || !IsLoaded) return;
        LoadTools();
        BuildCategoryTree();
        ApplyFilters();
        LocalizationService.Apply(this);
    }

    // ====== 数据加载 ======

    private void LoadTools()
    {
        _allTools = _configLoader.LoadAllTools();
        _filteredTools.Clear();
        foreach (var t in _allTools)
            _filteredTools.Add(t);

        StatusText.Text = LocalizationService.IsEnglish
            ? $"Loaded {_allTools.Count} tools"
            : $"已加载 {_allTools.Count} 个工具";
    }

    private void BuildCategoryTree()
    {
        // 清空旧条目（防止刷新后重复添加）
        CategoryTree.Items.Clear();

        // 重新添加静态条目
        AddStaticTreeItem("📋", LocalizationService.T("全部工具"), "__all__", true);
        AddStaticTreeItem("⭐", LocalizationService.T("我的收藏"), "__pinned__");
        AddStaticTreeItem("🕐", LocalizationService.T("最近使用"), "__recent__");

        // 分隔线
        CategoryTree.Items.Add(new Separator
        {
            Background = new SolidColorBrush(WpfColor.FromRgb(0xDA, 0xE5, 0xDD)),
            Margin = new Thickness(8, 16, 8, 16)
        });

        // 动态分类
        var categories = new SortedDictionary<string, HashSet<string>>();

        foreach (var tool in _allTools)
        {
            var parts = tool.Category.Split('/', '\\', '>');
            var current = "";
            for (int i = 0; i < parts.Length; i++)
            {
                current = i == 0 ? parts[i] : current + "/" + parts[i];
                if (!categories.ContainsKey(current))
                    categories[current] = new HashSet<string>();

                if (i < parts.Length - 1)
                {
                    var child = current + "/" + parts[i + 1];
                    categories[current].Add(child);
                }
            }
        }

        foreach (var root in categories.Keys.Where(k => !k.Contains('/')).OrderBy(k => k))
        {
            var item = CreateCategoryItem(root, categories);
            CategoryTree.Items.Add(item);
        }
    }

    private void AddStaticTreeItem(string icon, string text, string tag, bool isSelected = false)
    {
        var item = new TreeViewItem
        {
            Tag = tag,
            IsSelected = isSelected
        };
        var header = new StackPanel { Orientation = WpfOrientation.Horizontal };
        // Foreground 绑定到 TreeViewItem.Foreground，这样 Style 的 IsSelected Trigger 改色时
        // header 内的 TextBlock 会跟随变色（WPF ContentPresenter 边界不自动继承，需显式绑定）
        var iconFgBinding = new WpfBinding("Foreground") { RelativeSource = new WpfRelativeSource(WpfRelativeSourceMode.FindAncestor, typeof(TreeViewItem), 1) };
        var iconTb = new TextBlock
        {
            Text = icon,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 14
        };
        iconTb.SetBinding(TextBlock.ForegroundProperty, iconFgBinding);
        header.Children.Add(iconTb);

        var textTb = new TextBlock
        {
            Text = text,
            FontSize = 14
        };
        var textFgBinding = new WpfBinding("Foreground") { RelativeSource = new WpfRelativeSource(WpfRelativeSourceMode.FindAncestor, typeof(TreeViewItem), 1) };
        textTb.SetBinding(TextBlock.ForegroundProperty, textFgBinding);
        header.Children.Add(textTb);

        item.Header = header;
        CategoryTree.Items.Add(item);
    }

    private TreeViewItem CreateCategoryItem(string key, SortedDictionary<string, HashSet<string>> categories)
    {
        var name = key.Contains('/') ? key[(key.LastIndexOf('/') + 1)..] : key;
        var displayName = LocalizationService.T(name);
        var count = _allTools.Count(t =>
            t.Category == key || t.Category.StartsWith(key + "/"));

        var hasChildren = categories.TryGetValue(key, out var children);

        // 分类头：图标 + 名称 + 数量
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Foreground 绑定到 TreeViewItem.Foreground，选中时跟随 Style Trigger 变色
        // （WPF ContentPresenter 边界不自动继承 Foreground，必须显式绑定）
        var fgBinding = new WpfBinding("Foreground") { RelativeSource = new WpfRelativeSource(WpfRelativeSourceMode.FindAncestor, typeof(TreeViewItem), 1) };

        var iconText = new TextBlock
        {
            Text = GetCategoryIcon(name),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 13,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        iconText.SetBinding(TextBlock.ForegroundProperty, fgBinding);
        Grid.SetColumn(iconText, 0);
        header.Children.Add(iconText);

        var nameText = new TextBlock
        {
            Text = $"{displayName} ({count})",
            FontSize = 13,
            VerticalAlignment = WpfVerticalAlignment.Center
        };
        var nameFgBinding = new WpfBinding("Foreground") { RelativeSource = new WpfRelativeSource(WpfRelativeSourceMode.FindAncestor, typeof(TreeViewItem), 1) };
        nameText.SetBinding(TextBlock.ForegroundProperty, nameFgBinding);
        Grid.SetColumn(nameText, 1);
        header.Children.Add(nameText);

        // 添加子分类按钮（悬停显示）
        var addSubBtn = new Button
        {
            Content = "➕",
            FontSize = 11,
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            ToolTip = "添加子分类",
            VerticalAlignment = WpfVerticalAlignment.Center,
            Opacity = 0.5
        };
        // Foreground 绑定到 TreeViewItem.Foreground，选中时跟随变色
        var btnFgBinding = new WpfBinding("Foreground") { RelativeSource = new WpfRelativeSource(WpfRelativeSourceMode.FindAncestor, typeof(TreeViewItem), 1) };
        addSubBtn.SetBinding(Control.ForegroundProperty, btnFgBinding);
        addSubBtn.MouseEnter += (s, e) => addSubBtn.Opacity = 1.0;
        addSubBtn.MouseLeave += (s, e) => addSubBtn.Opacity = 0.5;
        addSubBtn.Click += (s, e) =>
        {
            e.Handled = true;
            AddSubCategory(key);
        };
        Grid.SetColumn(addSubBtn, 2);
        header.Children.Add(addSubBtn);

        var item = new TreeViewItem
        {
            Header = header,
            Tag = key
        };

        var contextMenu = CreateWorkbenchMenu(LocalizationService.IsEnglish
            ? $"{displayName} · {count} tools"
            : $"{name} · {count} 个工具");
        contextMenu.Items.Add(MenuAction("查看此分类", "\uE8A9", () => item.IsSelected = true));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(MenuAction("重命名分类…", "\uE70F", () => RenameCategory(key)));
        contextMenu.Items.Add(MenuAction("添加子分类…", "\uE8F4", () => AddSubCategory(key)));
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(MenuAction("移除此分类的工具…", "\uE74D", () => DeleteCategory(key), danger: true));
        contextMenu.Opened += (_, _) => item.IsSelected = true;
        item.ContextMenu = contextMenu;

        if (hasChildren)
        {
            foreach (var child in (children ?? []).OrderBy(c => c))
            {
                item.Items.Add(CreateCategoryItem(child, categories));
            }
        }

        // 关键：手动把 CategoryTreeItem 样式应用到自身。
        // WPF TreeView 的 ItemContainerStyle 只作用于顶层项，嵌套子项不会自动继承，
        // 必须显式设置 Style，否则子分类选中时显示系统默认蓝色。
        if (CategoryTree.TryFindResource("CategoryTreeItem") is Style treeItemStyle)
        {
            item.Style = treeItemStyle;
        }

        return item;
    }

    private static string GetCategoryIcon(string name) => name switch
    {
        "开发" => "💻",
        "工具" => "🔧",
        "网络" => "🌐",
        "图形" => "🎨",
        "音频" => "🎵",
        "文档" => "📄",
        "游戏" => "🎮",
        "系统" => "⚙️",
        "安全" => "🔒",
        "数据" => "📊",
        _ => "📁"
    };

    // ====== 工具卡片渲染 ======

    [RequiresUnreferencedCode("插件 DLL 反射加载无法被裁剪器静态分析")]
    private void RefreshToolCards()
    {
        if (_filteredTools.Count == 0)
        {
            EmptyOverlay.Visibility = Visibility.Visible;
            ToolCardsListBox.ItemsSource = null;
            return;
        }
        EmptyOverlay.Visibility = Visibility.Collapsed;

        if (_renderedListView != _isListView)
        {
        // 只在切换布局时重建面板，搜索保留现有容器。
        ToolCardsListBox.ItemTemplate = _isListView
            ? (DataTemplate)FindResource("ListTemplate")
            : (DataTemplate)FindResource("CardTemplate");

        // 切换 ItemsPanel：卡片用 WrapPanel，列表用 VirtualizingStackPanel 铺满宽度
        ToolCardsListBox.ItemsPanel = _isListView
            ? new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)))
            : CreateCardWrapPanelTemplate();
        _renderedListView = _isListView;
        }

        ToolCardsListBox.ItemsSource = _filteredTools;
    }

    private static ItemsPanelTemplate CreateCardWrapPanelTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(WrapPanel));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        factory.SetValue(WrapPanel.ItemHeightProperty, 206.0);
        factory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        var template = new ItemsPanelTemplate(factory);
        template.Seal();
        return template;
    }





    private void ToggleLayout_Click(object sender, RoutedEventArgs e)
    {
        _isListView = !_isListView;
        RefreshToolCards();

        // 更新按钮状态
        if (sender is WpfButton btn)
        {
            btn.Content = _isListView
                ? (LocalizationService.IsEnglish ? "Card view" : "切换卡片")
                : LocalizationService.T("切换列表");
            btn.ToolTip = LocalizationService.IsEnglish
                ? (_isListView ? "Switch to card view" : "Switch to list view")
                : (_isListView ? "切换到卡片布局" : "切换到列表布局");
        }
        StatusText.Text = LocalizationService.IsEnglish
            ? (_isListView ? "Switched to list view" : "Switched to card view")
            : (_isListView ? "已切换到列表布局" : "已切换到卡片布局");
    }



    private static Style CreateIconButtonStyle()
    {
        var style = new Style(typeof(WpfButton));
        style.Setters.Add(new Setter(Control.BackgroundProperty, WpfBrushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, WpfCursors.Hand));
        var template = new ControlTemplate(typeof(WpfButton));
        var fef = new FrameworkElementFactory(typeof(Border));
        fef.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        fef.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        fef.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = fef;
        style.Setters.Add(new Setter(Control.TemplateProperty, template));

        var trigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true
        };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty,
            new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF2, 0xF5))));
        style.Triggers.Add(trigger);

        return style;
    }

    // ====== 事件处理 ======



    [RequiresUnreferencedCode("插件 DLL 反射加载无法被裁剪器静态分析")]
    private async void ToolCard_ClickLaunch(ToolDefinition tool)
    {
        try
        {
            StatusText.Text = $"正在启动: {tool.Name}";
            await _toolLauncher.LaunchAsync(tool);
            tool.UsageCount++;
            tool.LastUsed = DateTime.Now;
            ApplyFilters();
            StatusText.Text = $"已启动: {tool.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ 启动失败: {tool.Name}";
            WpfMessageBox.Show(
                $"工具 \"{tool.Name}\" 启动失败。\n\n" +
                $"路径: {tool.Path}\n" +
                $"错误: {ex.Message}",
                "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is ToolDefinition tool)
        {
            var editor = new EditToolWindow(_configLoader, tool);
            editor.Owner = this;
            if (editor.ShowDialog() == true)
            {
                // 重新加载并刷新
                LoadTools();
                BuildCategoryTree();
                RefreshToolCards();
                StatusText.Text = $"✅ 已更新: {tool.Name}";
            }
        }
    }

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.Tag is ToolDefinition tool)
        {
            var id = ConfigLoader.GetEffectiveId(tool);
            var pinned = _configLoader.TogglePin(id);
            tool.IsPinned = pinned;
            btn.Content = pinned ? "📌" : "📍";
            btn.Foreground = pinned
                ? new SolidColorBrush(WpfColor.FromRgb(0x89, 0xB4, 0xFA))
                : (WpfBrush)FindResource("TextSecondary");
            btn.ToolTip = pinned ? "取消置顶" : "置顶";
            // 刷新视图以反映收藏状态变化
            ApplyFilters();
        }
    }

    // ====== 卡片右键菜单 ======

    private void DeleteTool(ToolDefinition tool)
    {
        var result = WpfMessageBox.Show(
            $"将 \"{tool.Name}\" 从工具箱移除？\n\n只移除工具条目，原软件文件会保留。",
            "移除工具", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            _configLoader.DeleteTool(tool);
            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            StatusText.Text = $"已从工具箱移除: {tool.Name}";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"删除失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CategoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ApplyFilters();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        if (CategoryTree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } all) all.IsSelected = true;
        ApplyFilters();
        SearchBox.Focus();
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized && ToolCardsListBox != null) ApplyFilters();
    }

    private void ToolList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Reserve scrollbar space, then distribute the available width evenly across columns.
        var available = Math.Max(260, e.NewSize.Width - 32);
        var columns = Math.Max(1, (int)(available / 270));
        Resources["ToolCardWidth"] = Math.Max(240, Math.Floor(available / columns) - 12);
    }

    private void ToolList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionPanel();

    private void UpdateSelectionPanel()
    {
        if (SelectionPanel == null) return;
        var tool = ToolCardsListBox.SelectedItem as ToolDefinition;
        SelectionPanel.Visibility = tool != null && !_batchMode ? Visibility.Visible : Visibility.Collapsed;
        if (tool == null) return;
        SelectedNameText.Text = tool.Name;
        SelectedPathText.Text = string.IsNullOrWhiteSpace(tool.Path) ? tool.Description : tool.Path;
        SelectedPathText.ToolTip = SelectedPathText.Text;
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_batchMode) return;
        if (sender is Button { Tag: ToolDefinition tool }) ToolCard_ClickLaunch(tool);
        e.Handled = true;
    }

    private void SelectedLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (!_batchMode && ToolCardsListBox.SelectedItem is ToolDefinition tool) ToolCard_ClickLaunch(tool);
    }

    private void SelectedEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ToolCardsListBox.SelectedItem is ToolDefinition tool)
            EditBtn_Click(new Button { Tag = tool }, e);
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_batchMode) return;
        if (sender is Button { Tag: ToolDefinition tool })
        {
            tool.IsPinned = _configLoader.TogglePin(ConfigLoader.GetEffectiveId(tool));
            ApplyFilters();
            StatusText.Text = tool.IsPinned ? $"已收藏 {tool.Name}" : $"已取消收藏 {tool.Name}";
        }
        e.Handled = true;
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_batchMode) return;
        if (sender is Button { Tag: ToolDefinition tool } button)
        {
            ShowToolContextMenu(tool, button, System.Windows.Controls.Primitives.PlacementMode.Bottom);
        }
        e.Handled = true;
    }

    private static bool IsButtonSource(object source)
    {
        for (var node = source as DependencyObject; node != null;)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (node is ListBoxItem) return false;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    private void ToolList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_batchMode || IsButtonSource(e.OriginalSource)) return;
        if (GetToolFromEvent(e) is { } tool) ToolCard_ClickLaunch(tool);
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (SettingsOverlay.Visibility == Visibility.Visible) return;
        if (e.Key == Key.Enter && IsButtonSource(e.OriginalSource)) return;
        if (ToolCardsListBox.IsKeyboardFocusWithin &&
            (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)))
        {
            if (ToolCardsListBox.SelectedItem is ToolDefinition menuTool)
            {
                ToolCardsListBox.ScrollIntoView(menuTool);
                ToolCardsListBox.UpdateLayout();
                var target = ToolCardsListBox.ItemContainerGenerator.ContainerFromItem(menuTool) as FrameworkElement ?? ToolCardsListBox;
                ShowToolContextMenu(menuTool, target, System.Windows.Controls.Primitives.PlacementMode.Bottom);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        else if (_batchMode && ToolCardsListBox.IsKeyboardFocusWithin && e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            BatchSelectAll_Checked(this, new RoutedEventArgs());
        else if (_batchMode && ToolCardsListBox.IsKeyboardFocusWithin && e.Key == Key.Space && !IsButtonSource(e.OriginalSource))
        {
            if (ToolCardsListBox.SelectedItem is ToolDefinition selectedTool)
            {
                var id = ConfigLoader.GetEffectiveId(selectedTool);
                if (!_batchSelectedIds.Remove(id)) _batchSelectedIds.Add(id);
                RefreshVisualBatchStates();
                UpdateBatchBar();
            }
        }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Clear();
        else if (e.Key == Key.Escape && _batchMode) { _batchMode = false; UpdateBatchModeUI(); }
        else if (e.Key == Key.F5) RefreshBtn_Click(this, e);
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocusWithin)
        {
            _searchTimer.Stop();
            ApplyFilters();
            if (_filteredTools.Count > 0)
            {
                ToolCardsListBox.SelectedIndex = 0;
                ToolCardsListBox.ScrollIntoView(_filteredTools[0]);
                ToolCardsListBox.UpdateLayout();
                (ToolCardsListBox.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            }
        }
        else if (e.Key == Key.Enter && !_batchMode &&
                 (SearchBox.IsKeyboardFocusWithin || ToolCardsListBox.IsKeyboardFocusWithin))
        {
            if (SearchBox.IsKeyboardFocusWithin) { _searchTimer.Stop(); ApplyFilters(); }
            var tool = ToolCardsListBox.SelectedItem as ToolDefinition ?? _filteredTools.FirstOrDefault();
            if (tool != null) ToolCard_ClickLaunch(tool);
        }
        else return;
        e.Handled = true;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.Background = new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xFF, 0xFF));
        SearchBorder.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0x16, 0x7D, 0x6B));
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.Background = new SolidColorBrush(WpfColor.FromRgb(0xF5, 0xF8, 0xF6));
        SearchBorder.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xDF, 0xE8, 0xE2));
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        LoadTools();
        BuildCategoryTree();
        ApplyFilters();
        StatusText.Text = LocalizationService.IsEnglish
            ? $"Refreshed — {_allTools.Count} tools"
            : $"已刷新 - {_allTools.Count} 个工具";
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsPanel();
    }

    private void ShowSettingsPanel()
    {
        var panel = new SettingsPanel(_configLoader);
        panel.CloseRequested += (s, e) => HideSettingsPanel();
        panel.Saved += (s, e) =>
        {
            RegisterHotKey();
            ApplyFilters();
        };
        SettingsPanelHost.Content = panel;
        SettingsOverlay.Visibility = Visibility.Visible;

        // 滑入动画
        var slideIn = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250));
        SettingsSlidePanel.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        SettingsBackdrop.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void HideSettingsPanel()
    {
        var slideOut = new System.Windows.Media.Animation.DoubleAnimation(480, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        slideOut.Completed += (s, e) =>
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SettingsPanelHost.Content = null;
        };
        SettingsSlidePanel.RenderTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        SettingsBackdrop.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void SettingsBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HideSettingsPanel();
    }

    // ====== 添加分类 / 添加工具 ======

    private void AddCategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        // 使用简易输入对话框
        var inputBox = new Window
        {
            Title = "添加分类",
            Width = 360,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF2, 0xF5)),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = "新建工具分类",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x2E)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "输入分类名称（支持多级，如 开发/数据库）",
            FontSize = 12,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x58, 0x5B, 0x70)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var textBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(textBox);

        var btnRow = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
        var cancelBtn = new Button
        {
            Content = "取消",
            Width = 70, Height = 30,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xD0, 0xD0, 0xD0)),
            Background = WpfBrushes.White,
            Cursor = WpfCursors.Hand,
            FontSize = 13
        };
        var okBtn = new Button
        {
            Content = "创建",
            Width = 70, Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(WpfColor.FromRgb(0x89, 0xB4, 0xFA)),
            Foreground = WpfBrushes.White,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };

        cancelBtn.Click += (s, args) => inputBox.Close();
        okBtn.Click += (s, args) =>
        {
            var cat = textBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(cat))
            {
                WpfMessageBox.Show("请输入分类名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 新建一个带此分类的空白工具
            var tool = new ToolDefinition
            {
                Name = $"[{cat}] 新建工具",
                Description = $"分类 \"{cat}\" 的工具，请点击编辑修改",
                Category = cat,
                Kind = ToolKind.Executable,
                Path = "notepad.exe",
                Tags = new List<string>()
            };
            _configLoader.AddTool(tool);
            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            inputBox.Close();
            StatusText.Text = $"✅ 已创建分类: {cat}";
        };

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        stack.Children.Add(btnRow);
        inputBox.Content = stack;
        inputBox.ShowDialog();
    }

    private void AddToolBtn_Click(object sender, RoutedEventArgs e)
    {
        var editor = new EditToolWindow(_configLoader);
        editor.Owner = this;
        if (editor.ShowDialog() == true)
        {
            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            StatusText.Text = $"✅ 已添加新工具";
        }
    }



    // ====== 拖放文件到窗口 ======

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;

        var file = files[0];
        var ext = Path.GetExtension(file).ToLowerInvariant();

        // 支持的可识别文件类型
        var supportedExts = new[] { ".exe", ".bat", ".cmd", ".ps1", ".py", ".msi",
                                    ".url", ".lnk", ".html", ".htm", ".dll" };
        if (!supportedExts.Contains(ext))
        {
            WpfMessageBox.Show(
                $"不支持的文件类型: {ext}\n\n支持的类型: {string.Join(", ", supportedExts)}",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 弹出添加窗口
        var editor = new EditToolWindow(_configLoader, file);
        editor.Owner = this;
        if (editor.ShowDialog() == true)
        {
            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            StatusText.Text = $"✅ 已添加新工具";
        }
    }

    // ====== 过滤逻辑 ======

    private void ApplyFilters()
    {
        if (_allTools == null || SearchBox == null || CategoryTree == null || ToolCardsListBox == null)
            return;

        var selectedId = (ToolCardsListBox.SelectedItem as ToolDefinition) is { } selected
            ? ConfigLoader.GetEffectiveId(selected) : null;

        var searchText = SearchBox.Text?.Trim() ?? "";
        var selectedTag = (CategoryTree.SelectedItem as TreeViewItem)?.Tag as string;

        var filtered = _allTools.AsEnumerable();

        if (!string.IsNullOrEmpty(selectedTag))
        {
            filtered = selectedTag switch
            {
                "__all__" => filtered,
                "__pinned__" => filtered.Where(t => t.IsPinned),
                "__recent__" => filtered.Where(t => t.LastUsed.HasValue)
                    .OrderByDescending(t => t.LastUsed),
                _ => filtered.Where(t =>
                    t.Category == selectedTag || t.Category.StartsWith(selectedTag + "/"))
            };
        }

        if (!string.IsNullOrEmpty(searchText))
        {
            var lower = searchText.ToLowerInvariant();
            filtered = filtered.Where(t =>
                t.Name.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                (t.Description?.Contains(lower, StringComparison.OrdinalIgnoreCase) ?? false) ||
                t.Tags.Any(tag => tag.Contains(lower, StringComparison.OrdinalIgnoreCase)) ||
                t.Category.Contains(lower, StringComparison.OrdinalIgnoreCase)
            );
        }

        filtered = SortBox.SelectedIndex switch
        {
            1 => filtered.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => filtered.OrderByDescending(t => t.LastUsed).ThenBy(t => t.Name),
            3 => filtered.OrderByDescending(t => t.IsPinned).ThenBy(t => t.Name),
            _ when selectedTag != "__recent__" => filtered.OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.UsageCount).ThenBy(t => t.Name),
            _ => filtered
        };
        var results = filtered.ToList();
        _batchSelectedIds.IntersectWith(results.Select(ConfigLoader.GetEffectiveId));
        _filteredTools.Clear();
        foreach (var t in results)
            _filteredTools.Add(t);

        // 应用自定义排序（仅对 __all__ 视图生效）
        if (SortBox.SelectedIndex == 0) ApplyCustomOrder();

        RefreshToolCards();

        ToolCardsListBox.SelectedItem = _filteredTools.FirstOrDefault(t => ConfigLoader.GetEffectiveId(t) == selectedId);
        UpdateBatchBar();
        UpdateSelectionPanel();

        // 更新空状态文本（我的收藏等特殊视图的提示）
        if (EmptySubText != null && !string.IsNullOrEmpty(searchText))
        {
            EmptySubText.Text = LocalizationService.IsEnglish
                ? $"No tools match “{searchText}”. Try another keyword or clear the search."
                : $"没有匹配“{searchText}”的工具，试试其他关键词或清空搜索。";
        }
        else if (EmptySubText != null && selectedTag == "__pinned__")
        {
            EmptySubText.Text = LocalizationService.IsEnglish ? "Select ☆ on a tool to keep it here." : "点击工具右上角的 ☆，把常用工具收藏到这里。";
        }
        else if (EmptySubText != null && selectedTag == "__recent__")
        {
            EmptySubText.Text = LocalizationService.IsEnglish ? "Launch or double-click a tool and it will appear here." : "点击启动按钮或双击工具，使用记录会显示在这里。";
        }
        else if (EmptySubText != null)
        {
            EmptySubText.Text = LocalizationService.IsEnglish ? "Drop a file or add a tool, or clear filters to see the full library." : "拖入文件或点击添加工具，也可以清除筛选查看整个工具库。";
        }

        StatusText.Text = LocalizationService.IsEnglish
            ? $"Showing {_filteredTools.Count} / {_allTools.Count} tools"
            : $"显示 {_filteredTools.Count} / {_allTools.Count} 个工具";
        ResultCountText.Text = LocalizationService.IsEnglish
            ? $"{_filteredTools.Count} tools   /   {_allTools.Count(t => t.IsPinned)} favorites   ·   Select for details, double-click to launch"
            : $"{_filteredTools.Count} 个工具   /   {_allTools.Count(t => t.IsPinned)} 个收藏   ·   选中查看详情，双击快速启动";
        CollectionTitle.Text = selectedTag switch
        {
            "__pinned__" => LocalizationService.T("我的收藏"),
            "__recent__" => LocalizationService.T("最近使用"),
            null or "__all__" => LocalizationService.T("全部工具"),
            _ => selectedTag.Replace("/", " / ")
        };
    }

    /// <summary>
    /// 应用用户保存的自定义排序。
    /// </summary>
    private void ApplyCustomOrder()
    {
        var selectedTag = (CategoryTree.SelectedItem as TreeViewItem)?.Tag as string;
        if (selectedTag != "__all__" && selectedTag != null) return;

        var order = _configLoader.LoadToolOrder();
        if (order == null || order.Count == 0) return;

        // 按自定义顺序重排，未在 order 中的排在后面
        var ordered = _filteredTools
            .OrderBy(t =>
            {
                var idx = order.IndexOf(ConfigLoader.GetEffectiveId(t));
                return idx < 0 ? int.MaxValue : idx;
            })
            .ThenBy(t => t.Name)
            .ToList();

        _filteredTools.Clear();
        foreach (var t in ordered)
            _filteredTools.Add(t);
    }

    // ====== 全局热键 ======

    private void RegisterHotKey()
    {
        // 先注销已有注册，避免重复
        UnregisterHotKey();

        var state = _configLoader.LoadUserState();
        var (modifiers, key) = ParseHotKey(state.HotKey);

        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;

        if (hwnd != IntPtr.Zero && key != Key.None)
        {
            var result = RegisterHotKey(hwnd, _hotKeyId, modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
            if (!result)
            {
                Debug.WriteLine("[HotKey] RegisterHotKey 失败，快捷键可能被其他应用占用");
            }
            var source = HwndSource.FromHwnd(hwnd);
            // 避免重复添加 Hook
            source?.RemoveHook(WndProc);
            source?.AddHook(WndProc);
        }
    }

    private void UnregisterHotKey()
    {
        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;
        if (hwnd != IntPtr.Zero)
            UnregisterHotKey(hwnd, _hotKeyId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _hotKeyId)
        {
            if (Visibility == Visibility.Visible)
                Hide();
            else
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static (int modifiers, Key key) ParseHotKey(string hotKey)
    {
        int mods = 0;
        Key key = Key.None;

        if (string.IsNullOrWhiteSpace(hotKey)) return (mods, key);

        var parts = hotKey.Split('+');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                mods |= MOD_CONTROL;
            else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                mods |= MOD_ALT;
            else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                mods |= MOD_SHIFT;
            else if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase))
                mods |= MOD_WIN;
            else if (Enum.TryParse<Key>(trimmed, true, out var k))
                key = k;
        }

        return (mods, key);
    }

    // ====== 分类管理 ======

    /// <summary>
    /// 重命名分类（更新所有工具的此分类名称）。
    /// </summary>
    private void RenameCategory(string key)
    {
        var name = key.Contains('/') ? key[(key.LastIndexOf('/') + 1)..] : key;

        var inputBox = new Window
        {
            Title = "重命名分类",
            Width = 360,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF2, 0xF5)),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = "重命名分类",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x2E)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"将 \"{name}\" 重命名为：",
            FontSize = 12,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x58, 0x5B, 0x70)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var textBox = new TextBox
        {
            Text = name,
            FontSize = 14,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(textBox);

        var btnRow = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
        var cancelBtn = new Button
        {
            Content = "取消",
            Width = 70, Height = 30,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xD0, 0xD0, 0xD0)),
            Background = WpfBrushes.White,
            Cursor = WpfCursors.Hand,
            FontSize = 13
        };
        var okBtn = new Button
        {
            Content = "确定",
            Width = 70, Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(WpfColor.FromRgb(0x89, 0xB4, 0xFA)),
            Foreground = WpfBrushes.White,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };

        cancelBtn.Click += (s, args) => inputBox.Close();
        okBtn.Click += (s, args) =>
        {
            var newName = textBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                WpfMessageBox.Show("请输入分类名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 在分类路径中替换最后一级名称
            var parentPath = key.Contains('/') ? key[..key.LastIndexOf('/')] : "";
            var newKey = string.IsNullOrEmpty(parentPath) ? newName : $"{parentPath}/{newName}";

            // 更新所有工具的此分类
            foreach (var tool in _allTools.Where(t => t.Category == key || t.Category.StartsWith(key + "/")))
            {
                if (tool.Category == key)
                    tool.Category = newKey;
                else
                    tool.Category = newKey + tool.Category[key.Length..];

                // 保存修改
                _configLoader.SaveTool(tool);
            }

            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            inputBox.Close();
            StatusText.Text = $"✅ 已重命名分类: {name} → {newName}";
        };

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        stack.Children.Add(btnRow);
        inputBox.Content = stack;
        inputBox.ShowDialog();
    }

    /// <summary>
    /// 添加子分类。
    /// </summary>
    private void AddSubCategory(string parentKey)
    {
        var parentName = parentKey.Contains('/') ? parentKey[(parentKey.LastIndexOf('/') + 1)..] : parentKey;

        var inputBox = new Window
        {
            Title = "添加子分类",
            Width = 360,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF2, 0xF5)),
            FontFamily = new FontFamily("Microsoft YaHei UI")
        };

        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = $"在 \"{parentName}\" 下添加子分类",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x1E, 0x1E, 0x2E)),
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "输入子分类名称：",
            FontSize = 12,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(0x58, 0x5B, 0x70)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var textBox = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(textBox);

        var btnRow = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
        var cancelBtn = new Button
        {
            Content = "取消",
            Width = 70, Height = 30,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xD0, 0xD0, 0xD0)),
            Background = WpfBrushes.White,
            Cursor = WpfCursors.Hand,
            FontSize = 13
        };
        var okBtn = new Button
        {
            Content = "创建",
            Width = 70, Height = 30,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(WpfColor.FromRgb(0x89, 0xB4, 0xFA)),
            Foreground = WpfBrushes.White,
            BorderThickness = new Thickness(0),
            Cursor = WpfCursors.Hand,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        };

        cancelBtn.Click += (s, args) => inputBox.Close();
        okBtn.Click += (s, args) =>
        {
            var subName = textBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(subName))
            {
                WpfMessageBox.Show("请输入子分类名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newKey = $"{parentKey}/{subName}";

            // 创建含子分类的示例工具
            var tool = new ToolDefinition
            {
                Name = $"[{subName}] 新建工具",
                Description = $"分类 \"{newKey}\" 的工具，请点击编辑修改",
                Category = newKey,
                Kind = ToolKind.Executable,
                Path = "notepad.exe",
                Tags = new List<string>()
            };
            _configLoader.AddTool(tool);
            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            inputBox.Close();
            StatusText.Text = $"✅ 已创建子分类: {newKey}";
        };

        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(okBtn);
        stack.Children.Add(btnRow);
        inputBox.Content = stack;
        inputBox.ShowDialog();
    }

    /// <summary>
    /// 删除分类（删除此分类下所有工具）。
    /// </summary>
    private void DeleteCategory(string key)
    {
        var name = key.Contains('/') ? key[(key.LastIndexOf('/') + 1)..] : key;
        var count = _allTools.Count(t => t.Category == key || t.Category.StartsWith(key + "/"));

        var result = WpfMessageBox.Show(
            $"移除分类 \"{name}\" 下的 {count} 个工具条目？\n\n原软件文件会保留。",
            "移除分类下的工具", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var toDelete = _allTools.Where(t => t.Category == key || t.Category.StartsWith(key + "/")).ToList();
            foreach (var tool in toDelete)
            {
                _configLoader.DeleteTool(tool);
            }

            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            StatusText.Text = $"🗑️ 已删除分类: {name}（{count} 个工具）";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"删除分类失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ====== 批量操作 ======

    private void BatchModeBtn_Click(object sender, RoutedEventArgs e)
    {
        _batchMode = !_batchMode;
        UpdateBatchModeUI();
    }

    private void UpdateBatchModeUI()
    {
        if (BatchModeBtn != null)
        {
            BatchModeBtn.Content = _batchMode ? "退出多选" : "多选";
            BatchModeBtn.ToolTip = _batchMode ? "退出批量模式" : "批量操作模式";
            BatchModeBtn.Foreground = _batchMode
                ? new SolidColorBrush(WpfColor.FromRgb(0x16, 0x7D, 0x6B))
                : (WpfBrush)FindResource("TextPrimary");
        }

        BatchBar.Visibility = _batchMode ? Visibility.Visible : Visibility.Collapsed;

        // 通过 ListBox.Tag 驱动复选框可见性（DataTemplate 中绑定）
        ToolCardsListBox.Tag = _batchMode ? "Batch" : null;

        if (!_batchMode)
        {
            _batchSelectedIds.Clear();
            RefreshVisualBatchStates();
        }

        UpdateBatchBar();
        UpdateSelectionPanel();
    }

    private void UpdateBatchBar()
    {
        if (BatchCountText == null) return;
        BatchCountText.Text = LocalizationService.IsEnglish
            ? $"{_batchSelectedIds.Count} selected"
            : $"已选择 {_batchSelectedIds.Count} 个";
        BatchPinButton.IsEnabled = BatchDeleteButton.IsEnabled = _batchSelectedIds.Count > 0;
        _syncingBatchSelection = true;
        BatchSelectAllCheck.IsChecked = _batchSelectedIds.Count == _filteredTools.Count && _filteredTools.Count > 0;
        _syncingBatchSelection = false;
    }

    private void BatchSelectAll_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingBatchSelection) return;
        foreach (var tool in _filteredTools)
        {
            _batchSelectedIds.Add(ConfigLoader.GetEffectiveId(tool));
        }
        RefreshVisualBatchStates();
        UpdateBatchBar();
    }

    private void BatchSelectAll_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_syncingBatchSelection) return;
        _batchSelectedIds.Clear();
        RefreshVisualBatchStates();
        UpdateBatchBar();
    }

    /// <summary>
    /// 刷新 ListBox 虚拟化容器中所有可见复选框的选中状态。
    /// </summary>
    private void RefreshVisualBatchStates()
    {
        _syncingBatchSelection = true;
        SyncBatchChildren(ToolCardsListBox);
        _syncingBatchSelection = false;
    }

    private void SyncBatchChildren(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is CheckBox cb && cb.DataContext is ToolDefinition tool)
                cb.IsChecked = _batchSelectedIds.Contains(ConfigLoader.GetEffectiveId(tool));
            else SyncBatchChildren(child);
        }
    }

    private void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_batchSelectedIds.Count == 0)
        {
            WpfMessageBox.Show("请先选择要删除的工具。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = WpfMessageBox.Show(
            $"将选中的 {_batchSelectedIds.Count} 个工具从工具箱移除？\n\n原软件文件会保留。",
            "批量移除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var toDelete = _allTools.Where(t => _batchSelectedIds.Contains(ConfigLoader.GetEffectiveId(t))).ToList();
            foreach (var tool in toDelete)
            {
                _configLoader.DeleteTool(tool);
            }

            _batchSelectedIds.Clear();
            LoadTools();
            BuildCategoryTree();
            ApplyFilters();
            StatusText.Text = $"🗑️ 已批量删除 {toDelete.Count} 个工具";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"批量删除失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BatchPin_Click(object sender, RoutedEventArgs e)
    {
        if (_batchSelectedIds.Count == 0)
        {
            WpfMessageBox.Show("请先选择要置顶的工具。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedCount = _batchSelectedIds.Count;
        foreach (var tool in _allTools.Where(t => _batchSelectedIds.Contains(ConfigLoader.GetEffectiveId(t))))
        {
            var id = ConfigLoader.GetEffectiveId(tool);
            if (!tool.IsPinned)
            {
                _configLoader.TogglePin(id);
                tool.IsPinned = true;
            }
        }

        ApplyFilters();
        StatusText.Text = $"已收藏 {selectedCount} 个工具";
    }

    private void BatchCancel_Click(object sender, RoutedEventArgs e)
    {
        _batchMode = false;
        UpdateBatchModeUI();
    }

    // ====== 虚拟化列表事件 ======

    /// <summary>
    /// 从路由事件源获取 ToolDefinition。
    /// </summary>
    private static ToolDefinition? GetToolFromEvent(RoutedEventArgs e)
    {
        return (e.OriginalSource as FrameworkElement)?.DataContext as ToolDefinition;
    }

    private void ToolCardsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsButtonSource(e.OriginalSource)) return;
        var tool = GetToolFromEvent(e);
        if (tool == null) return;

        if (_batchMode)
        {
            // 批量模式下切换选中状态
            var id = ConfigLoader.GetEffectiveId(tool);
            if (!_batchSelectedIds.Remove(id))
            {
                _batchSelectedIds.Add(id);
            }
            RefreshVisualBatchStates();
            UpdateBatchBar();
            e.Handled = true;
            return;
        }

        ToolCardsListBox.SelectedItem = tool;
    }

    private void ToolCardsListBox_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var tool = GetToolFromEvent(e);
        if (tool == null) return;

        var container = ToolCardsListBox.ItemContainerGenerator.ContainerFromItem(tool) as FrameworkElement;
        if (container != null)
        {
            ShowToolContextMenu(tool, container, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
        }
        e.Handled = true;
    }

    private void BatchCheckBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is ToolDefinition tool)
        {
            _syncingBatchSelection = true;
            cb.IsChecked = _batchSelectedIds.Contains(ConfigLoader.GetEffectiveId(tool));
            _syncingBatchSelection = false;
        }
    }

    private void BatchCheckBox_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        BatchCheckBox_Loaded(sender, new RoutedEventArgs());
    }

    private void BatchCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingBatchSelection) return;
        if (sender is CheckBox cb && cb.DataContext is ToolDefinition tool)
        {
            _batchSelectedIds.Add(ConfigLoader.GetEffectiveId(tool));
            UpdateBatchBar();
        }
    }

    private void BatchCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_syncingBatchSelection) return;
        if (sender is CheckBox cb && cb.DataContext is ToolDefinition tool)
        {
            _batchSelectedIds.Remove(ConfigLoader.GetEffectiveId(tool));
            UpdateBatchBar();
        }
    }

    // ====== Win32 API ======

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// 简单的 BooleanToVisibility 转换器。
/// </summary>
public class BooleanToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
    {
        if (value is bool b && b) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter,
        System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
