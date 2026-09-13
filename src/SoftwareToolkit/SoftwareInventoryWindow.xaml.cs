using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

public partial class SoftwareInventoryWindow : Window
{
    private readonly InstalledSoftwareScanner _scanner = new();
    private readonly SoftwareListService _listService = new();
    private readonly ObservableCollection<InstalledSoftware> _inventoryView = new();
    private readonly ObservableCollection<InstalledSoftware> _listView = new();
    private List<InstalledSoftware> _allSoftware = new();
    private bool _loaded;
    private bool _updating;

    public SoftwareInventoryWindow()
    {
        InitializeComponent();
        InventoryGrid.DataContext = _inventoryView;
        ListGrid.DataContext = _listView;
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        LocalizationService.Apply(this);
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e) => LocalizationService.Apply(this);

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        await ScanAsync();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async Task ScanAsync()
    {
        var saved = _listService.Load();
        ListTitleBox.Text = saved.Title;
        SetLoading(true);
        try
        {
            var scanned = await _scanner.ScanAsync();
            var savedById = saved.Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            var savedByName = saved.Items.GroupBy(item => Identity(item.Name, item.Publisher))
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var item in scanned)
            {
                if (!savedById.TryGetValue(item.Id, out var entry))
                    savedByName.TryGetValue(Identity(item.Name, item.Publisher), out entry);
                ApplySavedEntry(item, entry);
            }

            var scannedIds = scanned.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in saved.Items.Where(entry => !scannedIds.Contains(entry.Id)))
            {
                if (scanned.Any(item => Identity(item.Name, item.Publisher) == Identity(entry.Name, entry.Publisher))) continue;
                scanned.Add(FromSavedEntry(entry));
            }

            _allSoftware = scanned.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            RefreshViews();
            StatusText.Text = $"扫描完成：发现 {_allSoftware.Count(item => !item.Source.StartsWith("清单"))} 个软件，清单中有 {_listView.Count} 个";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"扫描软件失败：{ex.Message}", "软件清单", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "扫描失败；之前保存的清单没有被修改";
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool loading)
    {
        LoadingOverlay.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ScanButton.IsEnabled = !loading;
        Cursor = loading ? WpfCursors.Wait : null;
    }

    private void RefreshViews()
    {
        _updating = true;
        try
        {
            var query = SearchBox.Text?.Trim();
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allSoftware
                : _allSoftware.Where(item => Matches(item, query)).ToList();
            _inventoryView.Clear();
            foreach (var item in filtered) _inventoryView.Add(item);

            _listView.Clear();
            foreach (var item in _allSoftware.Where(item => item.IsIncluded)
                         .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
                _listView.Add(item);
        }
        finally
        {
            _updating = false;
        }

        InventoryCountText.Text = _inventoryView.Count.ToString();
        ListCountText.Text = _listView.Count.ToString();
        EmptyListPanel.Visibility = _listView.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Include_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        RefreshViews();
        Save();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded || _updating) return;
        RefreshViews();
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledSoftware item) return;
        if (!Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            WpfMessageBox.Show("下载链接必须是有效的 http 或 https 地址。", "无法打开链接",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
    }

    private void DownloadUrlBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledSoftware item) return;
        var automatic = InstalledSoftwareScanner.BuildAutomaticDownloadUrl(item);
        item.IsDownloadUrlAutomatic = string.Equals(item.DownloadUrl, automatic, StringComparison.OrdinalIgnoreCase);
        item.NotifyLinkModeChanged();
        Save();
    }

    private void ResetAutoLink_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledSoftware item) return;
        item.DownloadUrl = InstalledSoftwareScanner.BuildAutomaticDownloadUrl(item);
        item.IsDownloadUrlAutomatic = true;
        item.NotifyLinkModeChanged();
        Save();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledSoftware item) return;
        item.IsIncluded = false;
        RefreshViews();
        Save();
    }

    private void SelectVisible_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _inventoryView) item.IsIncluded = true;
        RefreshViews();
        Save();
        StatusText.Text = $"已将当前 {_inventoryView.Count} 个结果加入清单";
    }

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        if (_listView.Count == 0) return;
        if (WpfMessageBox.Show($"从清单中移除全部 {_listView.Count} 个软件？不会卸载软件。", "清空清单",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var item in _allSoftware) item.IsIncluded = false;
        RefreshViews();
        Save();
        StatusText.Text = "清单已清空";
    }

    private void CopyShareText_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureListNotEmpty()) return;
        Clipboard.SetText(SoftwareListService.BuildShareText(ListTitleBox.Text.Trim(), _allSoftware));
        StatusText.Text = "分享文本已复制到剪贴板";
    }

    private void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureListNotEmpty()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出可分享的软件清单",
            Filter = "网页文件 (*.html)|*.html",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = SanitizeFileName(ListTitleBox.Text.Trim())
        };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName,
            SoftwareListService.BuildShareHtml(ListTitleBox.Text.Trim(), _allSoftware),
            new System.Text.UTF8Encoding(false));
        Clipboard.SetText(dialog.FileName);
        StatusText.Text = $"分享页已导出，文件路径已复制：{dialog.FileName}";
    }

    private void ListTitleBox_LostFocus(object sender, RoutedEventArgs e) => Save();

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs) return;
        StatusText.Text = Tabs.SelectedIndex == 0
            ? "勾选软件即可加入清单"
            : "可直接修改下载链接；导出页面不包含安装路径等设备隐私信息";
    }

    private bool EnsureListNotEmpty()
    {
        if (_allSoftware.Any(item => item.IsIncluded)) return true;
        WpfMessageBox.Show("请先从“设备软件”中选择要加入清单的软件。", "清单为空",
            MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void Save()
    {
        if (!_loaded) return;
        try
        {
            _listService.Save(ListTitleBox.Text, _allSoftware);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoftwareInventory] 清单保存失败: {ex.Message}");
            StatusText.Text = "清单保存失败，请检查用户目录的写入权限";
        }
    }

    private static void ApplySavedEntry(InstalledSoftware item, SoftwareListEntry? entry)
    {
        item.IsIncluded = entry != null;
        item.DownloadUrl = entry?.IsDownloadUrlAutomatic == false && !string.IsNullOrWhiteSpace(entry.DownloadUrl)
            ? entry.DownloadUrl
            : InstalledSoftwareScanner.BuildAutomaticDownloadUrl(item);
        item.IsDownloadUrlAutomatic = entry?.IsDownloadUrlAutomatic ?? true;
        item.NotifyLinkModeChanged();
    }

    private static InstalledSoftware FromSavedEntry(SoftwareListEntry entry) => new()
    {
        Id = entry.Id,
        Name = entry.Name,
        Version = entry.Version,
        Publisher = entry.Publisher,
        Source = "清单（本机未发现）",
        PackageId = entry.PackageId,
        IsIncluded = true,
        DownloadUrl = entry.DownloadUrl,
        IsDownloadUrlAutomatic = entry.IsDownloadUrlAutomatic
    };

    private static bool Matches(InstalledSoftware item, string query) =>
        item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        (item.Publisher?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
        (item.Version?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
        (item.PackageId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static string Identity(string name, string? publisher) =>
        $"{name.Trim()}|{publisher?.Trim()}".ToUpperInvariant();

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "软件清单" : result;
    }
}
