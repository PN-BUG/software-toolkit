using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SoftwareToolkit.Models;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

public partial class SettingsWindow : Window
{
    private readonly ConfigLoader _configLoader;
    private UserState _originalState;

    public SettingsWindow(ConfigLoader configLoader)
    {
        InitializeComponent();
        _configLoader = configLoader;
        _originalState = configLoader.LoadUserState();

        // 加载当前设置
        HotKeyBox.Text = _originalState.HotKey;
        MinimizeToTrayChk.IsChecked = _originalState.MinimizeToTray;
        AutoStartChk.IsChecked = _originalState.AutoStart;
        SelectLanguage(_originalState.Language);

        // 关于信息
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = version != null
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "1.0.0";

        ConfigPathText.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftwareToolkit", "userstate.json");

        Owner = WpfApplication.Current.MainWindow;
        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        Closed += (_, _) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        LocalizationService.Apply(this);
    }

    private void ResetHotKey_Click(object sender, RoutedEventArgs e)
    {
        HotKeyBox.Text = "Ctrl+Alt+T";
    }

    private void MinimizeOption_Changed(object sender, RoutedEventArgs e)
    {
        // 无需额外处理，保存时读取值即可
    }

    private void AutoStartOption_Changed(object sender, RoutedEventArgs e)
    {
        // 无需额外处理，保存时读取值即可
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 验证热键格式
        var hotKey = HotKeyBox.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(hotKey))
        {
            var (mods, key) = ParseHotKey(hotKey);
            if (mods == 0 || key == System.Windows.Input.Key.None)
            {
                WpfMessageBox.Show("热键格式无效，请使用如 Ctrl+Alt+T 的格式。\n\n" +
                    "支持的修饰键: Ctrl, Alt, Shift, Win\n" +
                    "支持的主键: A~Z, 0~9, F1~F12",
                    "热键格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // 保存设置
        var state = _configLoader.LoadUserState();
        state.HotKey = hotKey;
        state.MinimizeToTray = MinimizeToTrayChk.IsChecked ?? true;
        state.AutoStart = AutoStartChk.IsChecked ?? false;
        state.Language = SelectedLanguage();
        _configLoader.SaveUserState(state);
        LocalizationService.SetLanguage(state.Language);

        // 设置或删除开机自启注册表项
        try
        {
            var appPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(appPath))
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (state.AutoStart)
                        key.SetValue("SoftwareToolkit", $"\"{appPath}\"");
                    else
                        key.DeleteValue("SoftwareToolkit", false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoStart] 注册表操作失败: {ex.Message}");
        }

        WpfMessageBox.Show(LocalizationService.T("设置已保存。"), LocalizationService.T("保存成功"), MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }

    private string SelectedLanguage() =>
        (LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? LocalizationService.Chinese;

    private void SelectLanguage(string? language)
    {
        LanguageCombo.SelectedIndex = language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0;
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e) => LocalizationService.Apply(this);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static (int modifiers, System.Windows.Input.Key key) ParseHotKey(string hotKey)
    {
        const int MOD_ALT = 0x0001;
        const int MOD_CONTROL = 0x0002;
        const int MOD_SHIFT = 0x0004;
        const int MOD_WIN = 0x0008;

        int mods = 0;
        var key = System.Windows.Input.Key.None;

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
            else
            {
                var converter = new System.Windows.Input.KeyConverter();
                var converted = converter.ConvertFromString(trimmed);
                if (converted is System.Windows.Input.Key k)
                    key = k;
            }
        }

        return (mods, key);
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var result = WpfMessageBox.Show(
            "确定要彻底卸载 SoftwareToolkit 吗？\n\n" +
            "将执行以下操作：\n" +
            "  • 删除程序文件\n" +
            "  • 删除配置文件\n" +
            "  • 删除开机自启注册表项\n\n" +
            "此操作不可撤销！",
            "确认卸载", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        // 1. 删除开机自启注册表项
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue("SoftwareToolkit", false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Uninstall] 注册表清理失败: {ex.Message}");
        }

        // 2. 获取程序路径，验证安全性
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            WpfMessageBox.Show("无法获取程序路径，卸载失败。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var exeDir = Path.GetDirectoryName(exePath)!;
        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SoftwareToolkit");

        // 安全校验：确保 exeDir 下确实存在我们的 exe（防止误删其他目录）
        if (!File.Exists(Path.Combine(exeDir, "SoftwareToolkit.exe")))
        {
            WpfMessageBox.Show("程序目录验证失败，卸载已中止。\n请手动删除程序文件。",
                "安全错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 3. 先关闭托盘图标，避免残留
        if (Owner is MainWindow main)
        {
            try { main.TrayIcon?.Dispose(); } catch { /* 忽略 */ }
        }

        // 4. 生成 PowerShell 卸载脚本（比 bat 更安全，原生支持路径引号和错误处理）
        var scriptPath = Path.Combine(Path.GetTempPath(),
            $"uninstall_softwaretoolkit_{Guid.NewGuid():N}.ps1");

        var exeNameNoExt = Path.GetFileNameWithoutExtension(exePath);
        var escapedExeDir = exeDir.Replace("'", "''");
        var escapedUserData = userDataDir.Replace("'", "''");
        var escapedScriptPath = scriptPath.Replace("'", "''");

        // 使用普通字符串拼接，避免 C# 插值与 PowerShell 语法冲突
        var script =
            "$ErrorActionPreference = 'SilentlyContinue'\n" +
            "\n" +
            "Write-Host '正在卸载 SoftwareToolkit...' -ForegroundColor Yellow\n" +
            "\n" +
            "# 等待主进程完全退出\n" +
            "Start-Sleep -Seconds 3\n" +
            "\n" +
            "# 仅杀我们自己的进程（用路径精确匹配）\n" +
            "Get-Process -Name '" + exeNameNoExt + "' |\n" +
            "    Where-Object { $_.MainModule.FileName -like '*SoftwareToolkit*' } |\n" +
            "    Stop-Process -Force\n" +
            "\n" +
            "Start-Sleep -Seconds 2\n" +
            "\n" +
            "# 安全删除：仅当路径存在且包含 SoftwareToolkit 时才删除\n" +
            "$exeDir = '" + escapedExeDir + "'\n" +
            "$userDataDir = '" + escapedUserData + "'\n" +
            "\n" +
            "if (Test-Path $exeDir)\n" +
            "{\n" +
            "    # 二次验证目录内容\n" +
            "    if (Test-Path (Join-Path $exeDir 'SoftwareToolkit.exe') -or\n" +
            "        Test-Path (Join-Path $exeDir 'SoftwareToolkit.dll'))\n" +
            "    {\n" +
            "        Remove-Item $exeDir -Recurse -Force\n" +
            "        if (Test-Path $exeDir) {\n" +
            "            Write-Host '警告: 程序目录删除失败，可能有文件被占用' -ForegroundColor Red\n" +
            "        } else {\n" +
            "            Write-Host '程序文件已删除' -ForegroundColor Green\n" +
            "        }\n" +
            "    } else {\n" +
            "        Write-Host '跳过: 目录中未找到 SoftwareToolkit 文件' -ForegroundColor Yellow\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "if (Test-Path $userDataDir)\n" +
            "{\n" +
            "    Remove-Item $userDataDir -Recurse -Force\n" +
            "    if (Test-Path $userDataDir) {\n" +
            "        Write-Host '警告: 配置目录删除失败' -ForegroundColor Red\n" +
            "    } else {\n" +
            "        Write-Host '配置文件已删除' -ForegroundColor Green\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "# 注册表清理\n" +
            "$regPath = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run'\n" +
            "if (Get-ItemProperty -Path $regPath -Name 'SoftwareToolkit' -ErrorAction SilentlyContinue)\n" +
            "{\n" +
            "    Remove-ItemProperty -Path $regPath -Name 'SoftwareToolkit'\n" +
            "    Write-Host '注册表项已删除' -ForegroundColor Green\n" +
            "}\n" +
            "\n" +
            "Write-Host ''\n" +
            "Write-Host '卸载完成！' -ForegroundColor Cyan\n" +
            "Write-Host ''\n" +
            "\n" +
            "# 清理自身\n" +
            "Remove-Item '" + escapedScriptPath + "' -Force\n";

        File.WriteAllText(scriptPath, script);

        // 5. 启动卸载脚本（不需要提权，HKCU 和用户目录都有权限）
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"无法启动卸载脚本: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 6. 关闭当前应用
        WpfApplication.Current.Shutdown();
    }
}
