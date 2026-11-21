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
