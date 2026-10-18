using System;
using System.Diagnostics;
using System.IO;

namespace FullScreenAlarm;

/// <summary>アプリのデータ置き場。</summary>
internal static class DataPaths
{
    /// <summary>
    /// ドキュメント\FullScreenAlarm。パッケージ内の領域(LocalState や AppData)は
    /// アンインストールで一緒に消えるため、その外に置く。
    /// </summary>
    public static string Folder { get; } = Resolve();

    private static string Resolve()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs))
        {
            docs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var dir = Path.Combine(docs, "FullScreenAlarm");
        Directory.CreateDirectory(dir);
        Debug.WriteLine($"data folder: {dir}");
        return dir;
    }
}