using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

public partial class MainWindow
{
    private ContextMenu CreateWorkbenchMenu(string title) => new()
    {
        Tag = title,
        Style = (Style)FindResource("WorkbenchContextMenu")
    };

    private MenuItem MenuAction(string label, string glyph, Action action, bool enabled = true, bool danger = false)
    {
        var item = new MenuItem
        {
            Header = label,
            Icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 },
            IsEnabled = enabled,
            Tag = danger ? "Danger" : null,
            Style = (Style)FindResource("WorkbenchMenuItem")
        };
        item.Click += (_, e) =>
        {
            e.Handled = true;
            try { action(); }
            catch (Exception ex)
            {
                StatusText.Text = $"{label.TrimEnd('…')}失败";
                WpfMessageBox.Show(ex.Message, "操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        return item;
    }

    private ContextMenu BuildCardContextMenu(ToolDefinition tool)
    {
        if (_batchMode) return BuildBatchContextMenu();
        var menu = CreateWorkbenchMenu(tool.Name);
        menu.Items.Add(MenuAction("启动工具", "\uE768", () => ToolCard_ClickLaunch(tool)));
        menu.Items.Add(MenuAction(tool.IsPinned ? "取消收藏" : "收藏工具", tool.IsPinned ? "\uE735" : "\uE734",
            () => Favorite_Click(new Button { Tag = tool }, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("编辑工具…", "\uE70F", () => EditBtn_Click(new Button { Tag = tool }, new RoutedEventArgs())));
        var location = ResolveToolLocation(tool);
        var open = MenuAction("打开所在位置", "\uE838", () => OpenToolLocation(tool), location != null);
        if (location == null) open.ToolTip = "找不到可打开的本地文件或目录";
        menu.Items.Add(open);
        var copyLabel = tool.Kind switch
        {
            ToolKind.Command => "复制命令",
            ToolKind.Url when location == null => "复制链接",
            ToolKind.BuiltIn => "复制工具标识",
            _ => "复制路径"
        };
        menu.Items.Add(MenuAction(copyLabel, "\uE8C8", () =>
        {
            Clipboard.SetText(location ?? tool.Path);
            StatusText.Text = $"已复制: {tool.Name}";
        }, !string.IsNullOrWhiteSpace(tool.Path)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("选择多个工具", "\uE762", () =>
        {
            _batchMode = true;
            _batchSelectedIds.Add(ConfigLoader.GetEffectiveId(tool));
            UpdateBatchModeUI();
            RefreshVisualBatchStates();
        }));
        menu.Items.Add(MenuAction("从工具箱移除…", "\uE74D", () => DeleteTool(tool), danger: true));
        return menu;
    }

    private ContextMenu BuildBatchContextMenu()
    {
        var count = _batchSelectedIds.Count;
        var menu = CreateWorkbenchMenu($"已选择 {count} 个工具");
        menu.Items.Add(MenuAction("收藏所选工具", "\uE734", () => BatchPin_Click(this, new RoutedEventArgs()), count > 0));
        menu.Items.Add(MenuAction("全选当前结果", "\uE762", () => BatchSelectAll_Checked(this, new RoutedEventArgs()), _filteredTools.Count > 0));
        menu.Items.Add(MenuAction("清空选择", "\uE894", () => BatchSelectAll_Unchecked(this, new RoutedEventArgs()), count > 0));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("移除所选工具…", "\uE74D", () => BatchDelete_Click(this, new RoutedEventArgs()), count > 0, danger: true));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("退出多选", "\uE711", () => BatchCancel_Click(this, new RoutedEventArgs())));
        return menu;
    }

    private void ShowToolContextMenu(ToolDefinition tool, FrameworkElement target, PlacementMode placement)
    {
        ToolCardsListBox.SelectedItem = tool;
        if (_batchMode)
        {
            _batchSelectedIds.Add(ConfigLoader.GetEffectiveId(tool));
            RefreshVisualBatchStates();
            UpdateBatchBar();
        }
        var menu = BuildCardContextMenu(tool);
        menu.PlacementTarget = target;
        menu.Placement = placement;
        menu.IsOpen = true;
    }

    private static string? ResolveToolLocation(ToolDefinition tool)
    {
        if (tool.Kind == ToolKind.Command || string.IsNullOrWhiteSpace(tool.Path)) return null;
        var value = Environment.ExpandEnvironmentVariables(tool.Path.Trim().Trim('"'));
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) return null;
        if (uri?.IsFile == true) value = uri.LocalPath;
        try
        {
            var sourceDir = string.IsNullOrWhiteSpace(tool.SourceFile) ? AppContext.BaseDirectory
                : Path.GetDirectoryName(Path.GetFullPath(tool.SourceFile)) ?? AppContext.BaseDirectory;
            var path = Path.GetFullPath(value, sourceDir);
            if (File.Exists(path) || Directory.Exists(path)) return path;
            if (Path.IsPathRooted(value) || value.IndexOfAny(new[] { '\\', '/' }) >= 0) return null;
            var searchDirs = new[] { Environment.GetFolderPath(Environment.SpecialFolder.System) }
                .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            foreach (var directory in searchDirs.Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                foreach (var extension in Path.HasExtension(value) ? new[] { "" } : new[] { ".exe", ".cmd", ".bat" })
                {
                    var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim().Trim('"')), value + extension);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[ContextMenu] Invalid tool location: {ex.Message}");
        }
        return null;
    }

    private static void OpenToolLocation(ToolDefinition tool)
    {
        var location = ResolveToolLocation(tool) ?? throw new FileNotFoundException("工具文件已移动或不存在，请先编辑工具路径。");
        if (Directory.Exists(location))
            Process.Start(new ProcessStartInfo { FileName = location, UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{location}\"", UseShellExecute = true });
    }
}
