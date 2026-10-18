using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;

namespace FullScreenAlarm;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 多重起動の防止: 既に動いていればそちらにウィンドウ表示を頼んで、自分は終了する
        var main = AppInstance.FindOrRegisterForKey("FullScreenAlarm.Main");
        if (!main.IsCurrent)
        {
            await main.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            System.Diagnostics.Process.GetCurrentProcess().Kill();
            return;
        }
        main.Activated += (_, _) => _window?.DispatcherQueue.TryEnqueue(() => _window.ShowFromTray());

        _window = new MainWindow();

        // ログイン時の自動スタートで起動された場合は、ウィンドウを出さずトレイで待機する
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activation.Kind == ExtendedActivationKind.StartupTask)
        {
            _window.StartInTray();
        }
        else
        {
            _window.Activate();
        }
    }
}