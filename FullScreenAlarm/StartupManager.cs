using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace FullScreenAlarm;

/// <summary>ログイン時の自動スタート(MSIX の StartupTask)の読み書き。</summary>
internal static class StartupManager
{
    // Package.appxmanifest の StartupTask の TaskId と一致させること
    private const string TaskId = "AlarmStartup";

    /// <summary>現在の状態。パッケージ版でない・manifest 未設定などの場合は null。</summary>
    public static async Task<StartupTaskState?> GetStateAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>有効/無効を切り替え、切り替え後の実際の状態を返す。</summary>
    public static async Task<StartupTaskState?> SetEnabledAsync(bool enable)
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enable)
            {
                return await task.RequestEnableAsync();
            }

            task.Disable();
            return task.State;
        }
        catch
        {
            return null;
        }
    }
}