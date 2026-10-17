using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace FullScreenAlarm;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        // ログイン時の自動スタートで起動された場合は、ウィンドウを出さず最小化しておく
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activation.Kind == ExtendedActivationKind.StartupTask)
        {
            _window.StartMinimized();
        }
    }
}