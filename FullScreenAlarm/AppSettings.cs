using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FullScreenAlarm;

/// <summary>アプリ全体の設定。settings.json に保存する(アラーム一覧とは別ファイル)。</summary>
internal sealed class AppSettings
{
    /// <summary>
    /// ログイン時の自動スタートを希望しているか。
    /// OS 側の登録は再インストールでオフに戻るため、この値を元に復元する。
    /// </summary>
    public bool AutoStart { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static readonly string FilePath = Path.Combine(DataPaths.Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"settings.json の読み込みに失敗: {ex}");
        }
        return new();
    }

    public void Save()
    {
        try
        {
            // 一時ファイルに書いてから置き換える(書き込み途中で終了しても元のファイルを壊さない)
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"settings.json の保存に失敗: {ex}");
        }
    }
}