using System.Runtime.InteropServices;

namespace RedControlUtility.Services;

public sealed record OverlayItem(string Name, string Value, bool On);

public sealed class OverlayService : IDisposable
{
    private const string ClassName = "RcuOverlayWindow";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WM_DESTROY = 2, WM_PAINT = 0xF, WM_ERASEBKGND = 0x14, WM_TIMER = 0x113, WM_NCHITTEST = 0x84;
    private const uint LWA_ALPHA = 2, SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private const int SW_SHOWNOACTIVATE = 4;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int W, H; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgb;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WNDCLASSEXW c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int cx, uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr h, UIntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr h, UIntPtr id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref RECT r, IntPtr brush);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(int h, int w, int esc, int ori, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool TextOutW(IntPtr dc, int x, int y, string s, int n);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out SIZE size);
    [DllImport("gdi32.dll")] private static extern bool Ellipse(IntPtr dc, int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);

    private static bool _classRegistered;
    private static readonly WndProc Proc = WindowProc;
    private static OverlayService? _current;

    private IntPtr _hwnd;
    private string _title = "";
    private IReadOnlyList<OverlayItem> _items = Array.Empty<OverlayItem>();
    private int _w, _h, _pad, _rowH, _titleH, _nameW;
    private double _scale = 1;
    private IntPtr _fontTitle, _font;

    private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));
    private static uint Of(Windows.UI.Color c) => Rgb(c.R, c.G, c.B);
    private static uint Bg => Of(ThemeService.Colors.Surface);
    private static uint Text => Of(ThemeService.Colors.Text);
    private static uint Muted => Of(ThemeService.Colors.Muted);
    private static uint Accent => Of(ThemeService.Colors.AccentText);
    private static readonly uint Green = Rgb(108, 203, 95);

    public static OverlayService Shared { get; } = new();

    public bool IsShowing => _hwnd != IntPtr.Zero;

    public void Show(string title, IReadOnlyList<OverlayItem> items, double seconds, int corner = 0)
    {
        Close();
        if (items.Count == 0) return;
        _title = title;
        _items = items;
        _scale = Math.Max(1.0, GetDpiForSystem() / 96.0);

        EnsureClass();
        Layout();

        IntPtr mon = MonitorFromWindow(GetForegroundWindow(), 2);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        int x = 24, y = 24, m = (int)(24 * _scale);
        if (GetMonitorInfoW(mon, ref mi))
        {
            var wa = mi.rcWork;
            x = corner switch { 1 or 3 => wa.R - _w - m, 4 => wa.L + (wa.R - wa.L - _w) / 2, _ => wa.L + m };
            y = corner switch { 2 or 3 => wa.B - _h - m, _ => wa.T + m };
        }

        _hwnd = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE,
            ClassName, "", WS_POPUP, x, y, _w, _h, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) return;

        _current = this;
        SetLayeredWindowAttributes(_hwnd, 0, 236, LWA_ALPHA);
        SetWindowRgn(_hwnd, CreateRoundRectRgn(0, 0, _w + 1, _h + 1, (int)(16 * _scale), (int)(16 * _scale)), true);
        ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetTimer(_hwnd, new UIntPtr(1), (uint)Math.Clamp(seconds * 1000, 500, 120000), IntPtr.Zero);
    }

    public void Close()
    {
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        FreeFonts();
        _hwnd = IntPtr.Zero;
    }

    public void Dispose() => Close();

    private static void EnsureClass()
    {
        if (_classRegistered) return;
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = Marshal.StringToHGlobalUni(ClassName)
        };
        RegisterClassExW(ref wc);
        _classRegistered = true;
    }

    private IntPtr MakeFont(int px, int weight) =>
        CreateFontW(-(int)Math.Round(px * _scale), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5  , 0, "Segoe UI");

    private void FreeFonts()
    {
        if (_fontTitle != IntPtr.Zero) DeleteObject(_fontTitle);
        if (_font != IntPtr.Zero) DeleteObject(_font);
        _fontTitle = _font = IntPtr.Zero;
    }

    private void Layout()
    {
        FreeFonts();
        _fontTitle = MakeFont(14, 700);
        _font = MakeFont(13, 400);
        _pad = (int)(16 * _scale);
        _rowH = (int)(24 * _scale);
        _titleH = (int)(32 * _scale);

        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            IntPtr old = SelectObject(dc, _fontTitle);
            GetTextExtentPoint32W(dc, _title, _title.Length, out var tsz);
            SelectObject(dc, _font);
            int nameW = 0, valW = 0;
            foreach (var it in _items)
            {
                GetTextExtentPoint32W(dc, it.Name, it.Name.Length, out var n);
                GetTextExtentPoint32W(dc, it.Value, it.Value.Length, out var v);
                nameW = Math.Max(nameW, n.W);
                valW = Math.Max(valW, v.W);
            }

            SelectObject(dc, old);
            _nameW = nameW;
            int dot = (int)(18 * _scale), gap = (int)(28 * _scale);
            _w = Math.Max(tsz.W, dot + nameW + gap + valW) + _pad * 2;
            _h = _pad * 2 + _titleH + _items.Count * _rowH - (int)(4 * _scale);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private void Paint(IntPtr hdc)
    {
        var all = new RECT { L = 0, T = 0, R = _w, B = _h };
        IntPtr bg = CreateSolidBrush(Bg);
        FillRect(hdc, ref all, bg);
        DeleteObject(bg);

        var bar = new RECT { L = 0, T = 0, R = _w, B = Math.Max(2, (int)(3 * _scale)) };
        IntPtr ab = CreateSolidBrush(Accent);
        FillRect(hdc, ref bar, ab);
        DeleteObject(ab);

        SetBkMode(hdc, 1  );
        IntPtr old = SelectObject(hdc, _fontTitle);
        SetTextColor(hdc, Text);
        TextOutW(hdc, _pad, _pad, _title, _title.Length);

        SelectObject(hdc, _font);
        int y = _pad + _titleH;
        int dotSize = (int)(8 * _scale);
        IntPtr nullPen = GetStockObject(8  );
        IntPtr oldPen = SelectObject(hdc, nullPen);
        foreach (var it in _items)
        {
            uint color = it.On ? Green : Muted;
            IntPtr br = CreateSolidBrush(color);
            IntPtr oldBr = SelectObject(hdc, br);
            int cy = y + (_rowH - (int)(4 * _scale)) / 2;
            Ellipse(hdc, _pad, cy - dotSize / 2, _pad + dotSize, cy + dotSize / 2 + 1);
            SelectObject(hdc, oldBr);
            DeleteObject(br);

            SetTextColor(hdc, Text);
            TextOutW(hdc, _pad + (int)(18 * _scale), y, it.Name, it.Name.Length);
            GetTextExtentPoint32W(hdc, it.Value, it.Value.Length, out var vs);
            SetTextColor(hdc, color);
            TextOutW(hdc, _w - _pad - vs.W, y, it.Value, it.Value.Length);
            y += _rowH;
        }

        SelectObject(hdc, oldPen);
        SelectObject(hdc, old);
    }

    private static IntPtr WindowProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
    {
        switch (msg)
        {
            case WM_PAINT:
            {
                IntPtr hdc = BeginPaint(h, out var ps);
                _current?.Paint(hdc);
                EndPaint(h, ref ps);
                return IntPtr.Zero;
            }
            case WM_ERASEBKGND:
                return new IntPtr(1);
            case WM_NCHITTEST:
                return new IntPtr(-1);
            case WM_TIMER:
                KillTimer(h, new UIntPtr(1));
                DestroyWindow(h);
                return IntPtr.Zero;
            case WM_DESTROY:
                if (_current != null && _current._hwnd == h) { _current.FreeFonts(); _current._hwnd = IntPtr.Zero; }
                return IntPtr.Zero;
        }

        return DefWindowProcW(h, msg, w, l);
    }
}
