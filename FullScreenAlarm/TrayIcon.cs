using System;
using System.Runtime.InteropServices;

namespace FullScreenAlarm;

/// <summary>
/// 通知領域(システムトレイ)のアイコン。WinUI 3 には標準部品が無いので Shell_NotifyIcon を直接呼ぶ。
/// ウィンドウをサブクラス化して、アイコン上のマウス操作を受け取る。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public event Action? OpenRequested;
    public event Action? ExitRequested;

    private const uint WM_APP_TRAY = 0x8000 + 1; // WM_APP + 1
    private const uint WM_NULL = 0x0000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;

    private const uint MF_STRING = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private static readonly IntPtr IDI_APPLICATION = new(32512);

    private const int CmdOpen = 1;
    private const int CmdExit = 2;
    private static readonly UIntPtr SubclassId = new(1u);

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc; // GC されないようフィールドで保持する
    private readonly uint _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private string _tip;
    private bool _disposed;

    public TrayIcon(IntPtr hwnd, string iconPath, string tooltip)
    {
        _hwnd = hwnd;
        _tip = Truncate(tooltip);

        // 指定の .ico を読み込む。無ければ Windows 標準のアプリアイコンで代用
        _icon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON,
            GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
        if (_icon != IntPtr.Zero)
        {
            _ownsIcon = true;
        }
        else
        {
            _icon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
        }

        _proc = WndProc;
        SetWindowSubclass(_hwnd, _proc, SubclassId, UIntPtr.Zero);
        Add();
    }

    // ───────── 公開メソッド ─────────

    public void SetTooltip(string text)
    {
        _tip = Truncate(text);
        var data = NewData(NIF_TIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var data = NewData(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        RemoveWindowSubclass(_hwnd, _proc, SubclassId);
        if (_ownsIcon) DestroyIcon(_icon);
    }

    // ───────── 内部処理 ─────────

    private static string Truncate(string s) => s.Length > 127 ? s[..127] : s; // szTip は 128 文字まで

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        if (msg == WM_APP_TRAY)
        {
            // 旧形式のコールバック: lParam にマウスメッセージが入る
            var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouse == WM_LBUTTONUP) OpenRequested?.Invoke();
            else if (mouse == WM_RBUTTONUP) ShowMenu();
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreatedMessage)
        {
            Add(); // エクスプローラーが再起動してアイコンが消えたときの再登録
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, new UIntPtr((uint)CmdOpen), "開く");
        AppendMenu(menu, MF_STRING, new UIntPtr((uint)CmdExit), "終了");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd); // これが無いとメニュー外クリックでメニューが閉じない
        var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        if (cmd == CmdOpen) OpenRequested?.Invoke();
        else if (cmd == CmdExit) ExitRequested?.Invoke();
    }

    // ───────── Win32 ─────────

    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadImageW")]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr iconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr refData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}