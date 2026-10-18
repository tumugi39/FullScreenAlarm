using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace FullScreenAlarm;

/// <summary>アラーム一覧を alarms.json に保存する。</summary>
internal static class AlarmStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly string FilePath = ResolvePath();

    /// <summary>
    /// 保存先を決める。パッケージ版は LocalFolder、
    /// パッケージ無し(Unpackaged)で動かしたときは %LOCALAPPDATA%\FullScreenAlarm。
    /// </summary>
    private static string ResolvePath()
    {
        string dir;
        try
        {
            dir = ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            // パッケージ無しで実行すると ApplicationData は使えない
            dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FullScreenAlarm");
        }

        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "alarms.json");
        Debug.WriteLine($"alarms.json: {path}");
        return path;
    }

    public static List<AlarmRule> Load()
    {
        if (!File.Exists(FilePath)) return new();

        try
        {
            return JsonSerializer.Deserialize<List<AlarmRule>>(File.ReadAllText(FilePath), Options) ?? new();
        }
        catch (Exception ex)
        {
            // 読めなかったファイルは退避する(空のまま上書き保存して消してしまわないため)
            Debug.WriteLine($"alarms.json の読み込みに失敗: {ex}");
            try
            {
                File.Move(FilePath, FilePath + ".bak", overwrite: true);
            }
            catch (Exception moveEx)
            {
                Debug.WriteLine($"alarms.json の退避に失敗: {moveEx}");
            }
            return new();
        }
    }

    public static void Save(IEnumerable<AlarmRule> rules)
    {
        try
        {
            // 一時ファイルに書いてから置き換える(書き込み途中で終了しても元のファイルを壊さない)
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(rules, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"alarms.json の保存に失敗: {ex}");
        }
    }
}