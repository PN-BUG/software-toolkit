using System.Windows;
using SoftwareToolkit.Services;

namespace SoftwareToolkit;

public partial class App : WpfApplication
{
    private Mutex? _instanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        LocalizationService.Initialize(new ConfigLoader().LoadUserState().Language);

        // 确保单实例运行（使用版本化互斥锁名，避免旧的僵尸进程阻塞）
        _instanceMutex = new Mutex(true, "SoftwareToolkit_SingleInstance_v2", out var createdNew);
        if (!createdNew)
        {
            WpfMessageBox.Show(
                LocalizationService.IsEnglish ? "SoftwareToolkit is already running.\nCheck the system tray icon." : "SoftwareToolkit 已在运行中。\n请查看系统托盘图标。",
                LocalizationService.IsEnglish ? "Notice" : "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
