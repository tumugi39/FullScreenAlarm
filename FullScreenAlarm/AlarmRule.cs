using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace FullScreenAlarm;

/// <summary>繰り返す曜日(月曜始まり)。</summary>
[Flags]
public enum RepeatDays
{
    None = 0,
    Mon = 1 << 0,
    Tue = 1 << 1,
    Wed = 1 << 2,
    Thu = 1 << 3,
    Fri = 1 << 4,
    Sat = 1 << 5,
    Sun = 1 << 6,
    Weekdays = Mon | Tue | Wed | Thu | Fri,
    Weekend = Sat | Sun,
    Every = Weekdays | Weekend,
}

/// <summary>1件のアラーム設定と「次に鳴る時刻」の計算ロジック。</summary>
public sealed class AlarmRule
{
    private static readonly string[] DayNames = { "月", "火", "水", "木", "金", "土", "日" };

    // 2024-01-01 は月曜。開始日が未指定のときの「N週ごと」の基準週
    private static readonly DateTime FallbackAnchor = new(2024, 1, 1);

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public TimeSpan Time { get; set; } = new(7, 0, 0);

    /// <summary>繰り返す曜日。None のときは1回きり。</summary>
    public RepeatDays Days { get; set; } = RepeatDays.None;

    /// <summary>何週ごとか(1 = 毎週, 2 = 隔週 …)。開始日を含む週が第0週。</summary>
    public int IntervalWeeks { get; set; } = 1;

    /// <summary>有効期間の開始日。N週ごとの基準、1回きりのときは実行日を兼ねる。</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>有効期間の終了日(この日を含む)。</summary>
    public DateOnly? EndDate { get; set; }

    public bool Silent { get; set; }

    /// <summary>次に鳴る時刻。実行時に計算するだけで保存しない。</summary>
    [JsonIgnore]
    public DateTime? NextFire { get; set; }

    // ───────── 次回時刻の計算 ─────────

    /// <summary><paramref name="after"/> より後で最初に鳴る時刻。無ければ null。</summary>
    public DateTime? GetNextOccurrence(DateTime after)
    {
        if (!Enabled) return null;

        // 1回きり: 日付が決まっていればその日、未指定なら今日
        if (Days == RepeatDays.None)
        {
            var date = (StartDate ?? DateOnly.FromDateTime(after)).ToDateTime(TimeOnly.MinValue);
            var at = date + Time;
            return at > after ? at : null;
        }

        var first = after.Date;
        if (StartDate is { } sd && sd.ToDateTime(TimeOnly.MinValue) > first)
        {
            first = sd.ToDateTime(TimeOnly.MinValue);
        }

        // N週ごとでも1周期ぶん + 1週間走査すれば必ず見つかる
        var span = 7 * Math.Max(1, IntervalWeeks) + 7;
        for (var i = 0; i < span; i++)
        {
            var date = first.AddDays(i);
            if (EndDate is { } ed && DateOnly.FromDateTime(date) > ed) return null;
            if (!Matches(date)) continue;

            var at = date + Time;
            if (at > after) return at;
        }
        return null;
    }

    /// <summary>その日が繰り返し条件に合うか。「第2・第4週」や祝日除外などはここに足す。</summary>
    private bool Matches(DateTime date)
    {
        if ((Days & ToFlag(date.DayOfWeek)) == 0) return false;

        var interval = Math.Max(1, IntervalWeeks);
        if (interval == 1) return true;

        var anchor = StartDate?.ToDateTime(TimeOnly.MinValue) ?? FallbackAnchor;
        var weeks = (MondayOf(date) - MondayOf(anchor)).Days / 7;
        return ((weeks % interval) + interval) % interval == 0;
    }

    private static RepeatDays ToFlag(DayOfWeek d) =>
        d == DayOfWeek.Sunday ? RepeatDays.Sun : (RepeatDays)(1 << ((int)d - 1));

    private static DateTime MondayOf(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    // ───────── 表示用 ─────────

    public string Describe()
    {
        string text;
        if (Days == RepeatDays.None)
        {
            text = StartDate is { } d ? $"{d:M/d} のみ" : "1回のみ";
        }
        else
        {
            var isPreset = Days is RepeatDays.Every or RepeatDays.Weekdays or RepeatDays.Weekend;
            var core = Days switch
            {
                RepeatDays.Every => "毎日",
                RepeatDays.Weekdays => "平日",
                RepeatDays.Weekend => "土日",
                _ => string.Join("・", Enumerable.Range(0, 7)
                        .Where(i => (Days & (RepeatDays)(1 << i)) != 0)
                        .Select(i => DayNames[i])),
            };

            if (IntervalWeeks > 1) text = $"{IntervalWeeks}週ごと {core}";
            else if (isPreset) text = core;
            else text = $"毎週 {core}";
        }

        if (EndDate is { } e) text += $" (~{e:M/d})";
        return text;
    }
}

/// <summary>ListView 表示用の行。</summary>
public sealed class AlarmRow
{
    public AlarmRow(AlarmRule rule) => Rule = rule;

    public AlarmRule Rule { get; }
    public string TimeText => Rule.Time.ToString(@"hh\:mm");
    public string RepeatText => Rule.Describe();
    public string Label => Rule.Label;
    public bool IsEnabled => Rule.Enabled;
}