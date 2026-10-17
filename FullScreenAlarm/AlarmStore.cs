using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace FullScreenAlarm;

/// <summary>アラーム一覧を LocalFolder/alarms.json に保存する(パッケージ版前提)。</summary>
internal static class AlarmStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(ApplicationData.Current.LocalFolder.Path, "alarms.json");

    public static List<AlarmRule> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<List<AlarmRule>>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch
        {
            return new(); // 壊れていたら空から始める
        }
    }

    public static void Save(IEnumerable<AlarmRule> rules)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(rules, Options));
        }
        catch
        {
            // 保存失敗でアプリは止めない(必要ならここでログ出力)
        }
    }
}