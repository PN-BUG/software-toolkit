using System.Windows;

namespace SoftwareToolkit;

public partial class App : WpfApplication
{
    private Mutex? _instanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 确保单实例运行（使用版本化互斥锁名，避免旧的僵尸进程阻塞）
        _instanceMutex = new Mutex(true, "SoftwareToolkit_SingleInstance_v2", out var createdNew);
        if (!createdNew)
        {
            WpfMessageBox.Show("SoftwareToolkit 已在运行中。\n请查看系统托盘图标。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
