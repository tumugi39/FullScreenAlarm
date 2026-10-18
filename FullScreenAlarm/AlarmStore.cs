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
    private const string FileName = "alarms.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// 保存先: ドキュメント\FullScreenAlarm\alarms.json。
    /// パッケージ内の領域(LocalState や AppData)はアンインストールで一緒に消えるため、その外に置く。
    /// </summary>
    private static readonly string FilePath = ResolvePath();

    private static string ResolvePath()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs))
        {
            docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var dir = Path.Combine(docs, "FullScreenAlarm");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, FileName);
        Debug.WriteLine($"alarms.json: {path}");
        return path;
    }

    /// <summary>旧バージョンの保存先(パッケージの LocalState / AppData)。初回の引き継ぎ用。</summary>
    private static List<string> LegacyPaths()
    {
        var paths = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FullScreenAlarm", FileName),
        };

        try
        {
            paths.Add(Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName));
        }
        catch (Exception)
        {
            // パッケージ無しで実行すると ApplicationData は使えない
        }
        return paths;
    }

    /// <summary>読み込むファイルを決める。新しい保存先が優先で、無ければ旧保存先から引き継ぐ。</summary>
    private static string? FindSourceFile()
    {
        if (File.Exists(FilePath)) return FilePath;

        foreach (var legacy in LegacyPaths())
        {
            if (File.Exists(legacy)) return legacy;
        }
        return null;
    }

    public static List<AlarmRule> Load()
    {
        var source = FindSourceFile();
        if (source is null) return new();

        try
        {
            return JsonSerializer.Deserialize<List<AlarmRule>>(File.ReadAllText(source), Options) ?? new();
        }
        catch (Exception ex)
        {
            // 読めなかったファイルは退避する(空のまま上書き保存して消してしまわないため)
            Debug.WriteLine($"alarms.json の読み込みに失敗: {ex}");
            try
            {
                File.Move(source, source + ".bak", overwrite: true);
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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

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