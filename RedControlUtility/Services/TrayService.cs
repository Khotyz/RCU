using System.Runtime.InteropServices;

namespace RedControlUtility.Services;

public sealed class TrayService : IDisposable
{
    private const int WM_TRAY = 0x8001;
    private const uint WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
    private const uint MF_STRING = 0;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATA data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr hMenu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(IntPtr hMenu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr rect);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ExtractIconW(IntPtr hInst, string file, uint index);

    private const uint WM_SIZE = 0x0005;
    private const int SIZE_MINIMIZED = 1;
    private uint _taskbarCreated;

    public static IntPtr LoadIcon(string icoPath)
    {
        IntPtr h = IntPtr.Zero;
        if (System.IO.File.Exists(icoPath))
        {
            h = LoadImageW(IntPtr.Zero, icoPath, 1, 32, 32, 0x10);
        }

        if (h == IntPtr.Zero && Environment.ProcessPath is { } exe)
        {
            h = ExtractIconW(IntPtr.Zero, exe, 0);
            if (h == (IntPtr)1) h = IntPtr.Zero;
        }

        return h;
    }

    public event Action? Minimized;

    private readonly SubclassProc _proc;
    private IntPtr _hwnd;
    private IntPtr _icon;
    private string _tip = string.Empty;
    private bool _visible;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        _proc = WndProc;
    }

    public void Initialize(IntPtr hwnd, IntPtr hIcon, string tip)
    {
        _hwnd = hwnd;
        _icon = hIcon;
        _tip = tip.Length > 120 ? tip[..120] : tip;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        SetWindowSubclass(hwnd, _proc, (UIntPtr)1, UIntPtr.Zero);
    }

    private NOTIFYICONDATA Data() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_TRAY,
        hIcon = _icon,
        szTip = _tip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty
    };

    public void Show()
    {
        if (_visible || _hwnd == IntPtr.Zero) return;
        var d = Data();
        _visible = Shell_NotifyIconW(NIM_ADD, ref d);
    }

    public void Notify(string title, string text)
    {
        if (!_visible) return;
        var d = Data();
        d.uFlags = NIF_INFO;
        d.szInfoTitle = title.Length > 63 ? title[..63] : title;
        d.szInfo = text.Length > 255 ? text[..255] : text;
        d.dwInfoFlags = 1 | 0x10;
        Shell_NotifyIconW(NIM_MODIFY, ref d);
    }

    public void Hide()
    {
        if (!_visible) return;
        var d = Data();
        Shell_NotifyIconW(NIM_DELETE, ref d);
        _visible = false;
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_TRAY)
        {
            uint ev = (uint)((long)lParam & 0xFFFF);
            if (ev is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                OpenRequested?.Invoke();
            }
            else if (ev == WM_RBUTTONUP)
            {
                ShowMenu();
            }

            return IntPtr.Zero;
        }

        if (msg == WM_SIZE && (int)wParam == SIZE_MINIMIZED)
        {
            Minimized?.Invoke();
        }
        else if (_taskbarCreated != 0 && msg == _taskbarCreated && _visible)
        {
            _visible = false;
            Show();
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING, (UIntPtr)1, UiHelper.L("Tray.Open"));
            AppendMenuW(menu, MF_STRING, (UIntPtr)2, UiHelper.L("Tray.Exit"));
            GetCursorPos(out var p);
            SetForegroundWindow(_hwnd);
            int cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, p.X, p.Y, 0, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
            if (cmd == 1) OpenRequested?.Invoke();
            else if (cmd == 2) ExitRequested?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        Hide();
        if (_hwnd != IntPtr.Zero)
        {
            RemoveWindowSubclass(_hwnd, _proc, (UIntPtr)1);
            _hwnd = IntPtr.Zero;
        }
    }
}
