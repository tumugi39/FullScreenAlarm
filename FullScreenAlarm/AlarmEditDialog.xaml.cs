using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FullScreenAlarm;

public sealed partial class AlarmEditDialog : ContentDialog
{
    private static readonly string[] DayLabels = { "月", "火", "水", "木", "金", "土", "日" };

    private readonly AlarmRule _rule;
    private readonly ToggleButton[] _dayButtons = new ToggleButton[7];

    public AlarmEditDialog(AlarmRule rule, bool isNew)
    {
        InitializeComponent();
        _rule = rule;

        Title = isNew ? "アラームを追加" : "アラームを編集";
        if (!isNew) SecondaryButtonText = "削除";

        for (var i = 0; i < 7; i++)
        {
            var button = new ToggleButton { Content = DayLabels[i], Width = 44 };
            _dayButtons[i] = button;
            DayPanel.Children.Add(button);
        }
        SetDays(rule.Days);

        LabelBox.Text = rule.Label;
        TimeBox.Time = rule.Time;
        IntervalBox.Value = rule.IntervalWeeks;
        StartPicker.Date = ToPickerDate(rule.StartDate);
        EndPicker.Date = ToPickerDate(rule.EndDate);
        SilentSwitch.IsOn = rule.Silent;

        PrimaryButtonClick += OnPrimaryButtonClick;
    }

    // ───────── 保存 ─────────

    /// <summary>入力を検証し、問題なければ元の AlarmRule に書き戻す(キャンセル時は触らない)。</summary>
    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var start = FromPickerDate(StartPicker.Date);
        var end = FromPickerDate(EndPicker.Date);
        var days = GetDays();
        var interval = double.IsNaN(IntervalBox.Value) ? 1 : (int)IntervalBox.Value;

        if (start is { } s && end is { } e && e < s)
        {
            ErrorText.Text = "終了日が開始日より前になっています";
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        if (days == RepeatDays.None)
        {
            // 1回きり: 実行日を確定させる(今日の時刻が過ぎていれば明日)
            start ??= DateOnly.FromDateTime(
                TimeBox.Time > DateTime.Now.TimeOfDay ? DateTime.Today : DateTime.Today.AddDays(1));
        }
        else if (interval > 1 && start is null)
        {
            // N週ごとは基準になる開始日が必要(未指定なら今日)
            start = DateOnly.FromDateTime(DateTime.Today);
        }

        _rule.Label = LabelBox.Text.Trim();
        _rule.Time = TimeBox.Time;
        _rule.Days = days;
        _rule.IntervalWeeks = Math.Clamp(interval, 1, 52);
        _rule.StartDate = start;
        _rule.EndDate = end;
        _rule.Silent = SilentSwitch.IsOn;
    }

    // ───────── 曜日ボタン ─────────

    private RepeatDays GetDays()
    {
        var days = RepeatDays.None;
        for (var i = 0; i < 7; i++)
        {
            if (_dayButtons[i].IsChecked == true) days |= (RepeatDays)(1 << i);
        }
        return days;
    }

    private void SetDays(RepeatDays days)
    {
        for (var i = 0; i < 7; i++)
        {
            _dayButtons[i].IsChecked = (days & (RepeatDays)(1 << i)) != 0;
        }
    }

    private void Weekdays_Click(object sender, RoutedEventArgs e) => SetDays(RepeatDays.Weekdays);
    private void Weekend_Click(object sender, RoutedEventArgs e) => SetDays(RepeatDays.Weekend);
    private void Every_Click(object sender, RoutedEventArgs e) => SetDays(RepeatDays.Every);
    private void Clear_Click(object sender, RoutedEventArgs e) => SetDays(RepeatDays.None);

    // ───────── DateOnly <-> CalendarDatePicker ─────────

    private static DateTimeOffset? ToPickerDate(DateOnly? d) =>
        d is { } v ? new DateTimeOffset(v.ToDateTime(TimeOnly.MinValue)) : null;

    private static DateOnly? FromPickerDate(DateTimeOffset? d) =>
        d is { } v ? DateOnly.FromDateTime(v.Date) : null;
}