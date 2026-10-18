using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System.Display;
using WinRT.Interop;

namespace FullScreenAlarm;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan SnoozeSpan = TimeSpan.FromMinutes(5);

    private readonly DispatcherQueueTimer _timer;
    private readonly DisplayRequest _displayRequest = new();
    private readonly IntPtr _hwnd;
    private readonly List<AlarmRule> _rules = AlarmStore.Load();
    private readonly AppSettings _settings = AppSettings.Load();

    private MediaPlayer? _player;
    private AlarmRule? _ringingRule;
    private DateTime? _snoozeUntil;
    private ContentDialog? _openDialog;
    private TrayIcon? _tray;
    private bool _isRinging;
    private bool _exiting;
    private bool _hiddenBeforeRing;
    private bool _updatingAutoStart;

    public MainWindow()
    {
        InitializeComponent();

        // タイトルバー領域までコンテンツを広げ、AppTitleBar をドラッグ領域にする
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // UI スレッドの DispatcherQueue から1秒周期のタイマーを作る(IsRepeating は既定で true)
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);

        _hwnd = WindowNative.GetWindowHandle(this);

        // システムトレイ(アイコンは Assets/alarm.ico。無ければ標準アイコン)
        _tray = new TrayIcon(_hwnd, Path.Combine(AppContext.BaseDirectory, "Assets", "alarm.ico"), "全画面アラーム");
        _tray.OpenRequested += () => DispatcherQueue.TryEnqueue(ShowFromTray);
        _tray.ExitRequested += () => DispatcherQueue.TryEnqueue(ExitApp);

        // 設定画面は通常サイズのウィンドウ
        AppWindow.Resize(new SizeInt32(640, 700));

        // 保存済みアラームの次回時刻を計算(期限切れは無効化される)
        var now = DateTime.Now;
        foreach (var rule in _rules) Reschedule(rule, now);
        AlarmStore.Save(_rules);
        RefreshList();
        UpdateStatus();

        // 起動中は画面を消灯させない(= アイドルスリープも防ぐ)
        _displayRequest.RequestActive();

        AppWindow.Closing += AppWindow_Closing;

        _timer.Tick += OnTick;
        _timer.Start();
        UpdateClock(now);

        // 自動スタートの現在状態を反映(非同期)
        _ = InitAutoStartAsync();
    }

    // ───────── 鳴動モード(全画面)の出入り ─────────

    private void EnterRingMode()
    {
        // トレイに格納中でも表示する
        AppWindow.Show();

        // 最小化されていたら復元してから全画面へ
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p)
        {
            p.Restore();
        }

        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        Activate();
        NativeMethods.BringToFrontTopmost(_hwnd);
    }

    private void ExitRingMode()
    {
        NativeMethods.ClearTopmost(_hwnd);
        AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

        // 鳴動前にトレイへ格納されていたなら、止めた後は元のトレイ格納に戻す
        if (_hiddenBeforeRing)
        {
            HideToTray();
        }
    }

    // ───────── 終了制御 ─────────

    /// <summary>
    /// 鳴動中は閉じる操作(Alt+F4 等)を無効化。通常の「閉じる」はトレイへ格納し、
    /// 終了はトレイメニューの「終了」から行う。
    /// </summary>
    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isRinging)
        {
            args.Cancel = true;
            return;
        }

        if (!_exiting)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }

        Cleanup();
    }

    private void Cleanup()
    {
        _tray?.Dispose();
        _timer.Stop();
        _player?.Dispose();
        _displayRequest.RequestRelease();
    }

    // ───────── 時計 / アラーム判定 ─────────

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        var now = DateTime.Now;
        UpdateClock(now);
        if (_isRinging) return;

        // スヌーズ明け
        if (_snoozeUntil is { } until && now >= until && _ringingRule is { } snoozed)
        {
            _snoozeUntil = null;
            StartRinging(snoozed);
            return;
        }

        // 時刻が来たアラーム(同時に複数あっても鳴動は1回)
        var due = _rules.Where(r => r.NextFire is { } at && at <= now).ToList();
        if (due.Count == 0) return;

        foreach (var rule in due) Reschedule(rule, now);
        AlarmStore.Save(_rules);
        RefreshList();
        UpdateStatus();

        StartRinging(due[0]);
    }

    /// <summary>次回時刻を再計算。次回が無い(期限切れ・過去日の1回きり)なら無効化する。</summary>
    private static void Reschedule(AlarmRule rule, DateTime now)
    {
        rule.NextFire = rule.GetNextOccurrence(now);
        if (rule.Enabled && rule.NextFire is null)
        {
            rule.Enabled = false;
        }
    }

    private void UpdateClock(DateTime now)
    {
        ClockText.Text = now.ToString("HH:mm:ss");
        DateText.Text = now.ToString("yyyy年M月d日 dddd");
    }

    private void UpdateStatus()
    {
        var times = _rules
            .Where(r => r.Enabled && r.NextFire.HasValue)
            .Select(r => r.NextFire!.Value)
            .ToList();
        if (_snoozeUntil is { } s) times.Add(s);

        AlarmStatusText.Text = times.Count == 0
            ? "有効なアラームなし"
            : $"次のアラーム: {times.Min():M/d (ddd) HH:mm}";

        _tray?.SetTooltip($"アラーム - {AlarmStatusText.Text}");
    }

    // ───────── 設定 UI ─────────

    private void RefreshList()
    {
        AlarmList.ItemsSource = _rules
            .OrderBy(r => r.Time)
            .Select(r => new AlarmRow(r))
            .ToList();
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        // 初期値は1分後(動作確認しやすいように)
        var t = DateTime.Now.AddMinutes(1);
        await EditAsync(new AlarmRule { Time = new TimeSpan(t.Hour, t.Minute, 0) }, isNew: true);
    }

    private async void AlarmList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AlarmRow row)
        {
            await EditAsync(row.Rule, isNew: false);
        }
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: AlarmRule rule } box) return;

        rule.Enabled = box.IsChecked == true;
        Reschedule(rule, DateTime.Now);
        box.IsChecked = rule.Enabled; // 次回が無く自動で無効化された場合に表示を合わせる

        AlarmStore.Save(_rules);
        UpdateStatus();
    }

    private async Task EditAsync(AlarmRule rule, bool isNew)
    {
        var dialog = new AlarmEditDialog(rule, isNew) { XamlRoot = Root.XamlRoot };
        _openDialog = dialog;
        var result = await dialog.ShowAsync();
        _openDialog = null;

        switch (result)
        {
            case ContentDialogResult.Primary:
                if (isNew) _rules.Add(rule);
                Reschedule(rule, DateTime.Now);
                break;
            case ContentDialogResult.Secondary:
                _rules.Remove(rule);
                break;
            default:
                return; // キャンセル、または鳴動で閉じられた
        }

        AlarmStore.Save(_rules);
        RefreshList();
        UpdateStatus();
    }

    // ───────── システムトレイ ─────────

    /// <summary>ログイン時の自動スタートで起動されたとき: ウィンドウを出さずトレイで待機する。</summary>
    public void StartInTray() => HideToTray();

    private void HideToTray() => AppWindow.Hide();

    /// <summary>トレイから(または2つ目の起動要求で)ウィンドウを表示して前面に出す。</summary>
    public void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p)
        {
            p.Restore();
        }
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    // ───────── 自動スタート ─────────

    private async Task InitAutoStartAsync()
    {
        var state = await StartupManager.GetStateAsync();

        // 再インストール直後など: OS 側がオフでも、保存してあった希望に合わせて有効化し直す
        if (state == StartupTaskState.Disabled && _settings.AutoStart)
        {
            state = await StartupManager.SetEnabledAsync(true);
        }

        // OS 側の状態が確定しているとき(Windows の設定で変更された場合を含む)は、保存する希望もそれに合わせる
        if (state is StartupTaskState.Enabled or StartupTaskState.DisabledByUser)
        {
            SaveAutoStartPreference(state);
        }

        ApplyAutoStartState(state);
    }

    /// <summary>実際の状態を「自動スタートを希望しているか」として保存する。</summary>
    private void SaveAutoStartPreference(StartupTaskState? state)
    {
        if (state is null) return; // 取得・変更に失敗したときは保存済みの希望を変えない

        var wanted = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        if (_settings.AutoStart == wanted) return;

        _settings.AutoStart = wanted;
        _settings.Save();
    }

    private async void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingAutoStart) return;

        var state = await StartupManager.SetEnabledAsync(AutoStartToggle.IsOn);
        SaveAutoStartPreference(state); // 拒否された場合も含め、実際の結果を保存する
        ApplyAutoStartState(state);
    }

    /// <summary>スタートアップの状態をトグルとヒント文に反映する(拒否された場合も実際の状態に合わせる)。</summary>
    private void ApplyAutoStartState(StartupTaskState? state)
    {
        _updatingAutoStart = true;
        AutoStartToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        _updatingAutoStart = false;

        // 切り替えられるのは Disabled / Enabled のときだけ
        AutoStartToggle.IsEnabled = state is StartupTaskState.Disabled or StartupTaskState.Enabled;
        AutoStartHint.Text = state switch
        {
            null => "自動スタートはパッケージ版でのみ使えます",
            StartupTaskState.DisabledByUser => "Windows の設定 > アプリ > スタートアップ で許可すると使えます",
            StartupTaskState.DisabledByPolicy => "ポリシーにより無効化されています",
            StartupTaskState.EnabledByPolicy => "ポリシーにより有効化されています",
            _ => "",
        };
    }

    // ───────── 鳴動 ─────────

    private void StartRinging(AlarmRule rule)
    {
        _openDialog?.Hide(); // 編集ダイアログが開いていたら閉じる(鳴動を優先)

        _ringingRule = rule;
        _isRinging = true;
        RingTimeText.Text = rule.Time.ToString(@"hh\:mm");
        RingLabelText.Text = rule.Label;

        SettingsPanel.Visibility = Visibility.Collapsed;
        RingPanel.Visibility = Visibility.Visible;
        RingPulse.Begin();

        _hiddenBeforeRing = !AppWindow.IsVisible;
        EnterRingMode();

        // 無音モードのアラームは再生しない
        if (!rule.Silent)
        {
            _player ??= new MediaPlayer
            {
                IsLoopingEnabled = true,
                Source = MediaSource.CreateFromUri(new Uri("ms-appx:///Assets/alarm.mp3")),
            };
            _player.Play();
        }
    }

    private void StopRinging(bool snooze)
    {
        _player?.Pause();
        _isRinging = false;
        _snoozeUntil = snooze ? DateTime.Now + SnoozeSpan : null;

        RingPulse.Stop();
        RingPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;

        ExitRingMode();
        UpdateStatus();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => StopRinging(snooze: false);

    private void SnoozeButton_Click(object sender, RoutedEventArgs e) => StopRinging(snooze: true);
}