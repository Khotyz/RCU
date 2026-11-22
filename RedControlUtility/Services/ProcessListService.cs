using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RedControlUtility.Services;

public sealed class RunningProcess
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string? IconPath { get; set; }
}

public static class ProcessListService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FastEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FastFirst(IntPtr snapshot, ref FastEntry entry);

    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool FastNext(IntPtr snapshot, ref FastEntry entry);

    public static unsafe bool[] FindRunning(string[] names)
    {
        var found = new bool[names.Length];
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return found;
        try
        {
            var e = new FastEntry { Size = (uint)sizeof(FastEntry) };
            if (!FastFirst(snap, ref e)) return found;
            do
            {
                var span = new ReadOnlySpan<char>(e.ExeFile, 260);
                int end = span.IndexOf('\0');
                if (end >= 0) span = span.Slice(0, end);
                int dot = span.LastIndexOf('.');
                if (dot > 0) span = span.Slice(0, dot);
                for (int i = 0; i < names.Length; i++)
                {
                    if (!found[i] && span.Equals(names[i], StringComparison.OrdinalIgnoreCase)) found[i] = true;
                }
            }
            while (FastNext(snap, ref e));
        }
        finally
        {
            CloseHandle(snap);
        }

        return found;
    }

    public static HashSet<string> RunningNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return set;
        try
        {
            var e = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32FirstW(snap, ref e)) return set;
            do
            {
                set.Add(Path.GetFileNameWithoutExtension(e.ExeFile));
            }
            while (Process32NextW(snap, ref e));
        }
        finally
        {
            CloseHandle(snap);
        }

        return set;
    }

    private static readonly string[] SystemNames =
    {
        "explorer", "applicationframehost", "textinputhost", "systemsettings", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "widgets", "lockapp", "taskmgr", "dwm", "csrss", "winlogon", "runtimebroker", "msedgewebview2", "cmd", "conhost", "powershell", "windowsterminal",
        "redcontrolutility"
    };

    public static string? GetPath(int pid)
    {
        IntPtr h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    public static List<RunningProcess> List(bool onlyWithWindow)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var result = new List<RunningProcess>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id <= 4 || SystemNames.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                string title = string.Empty;
                try { title = p.MainWindowTitle; } catch { }
                bool hasWindow = false;
                try { hasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { }
                if (onlyWithWindow && !hasWindow) continue;
                string? path = GetPath(p.Id);
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(path)) continue;
                result.Add(new RunningProcess { Id = p.Id, Name = p.ProcessName, Title = title, ExecutablePath = path });
            }
            catch
            {
            }
            finally
            {
                p.Dispose();
            }
        }

        return result.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
