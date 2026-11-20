using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace RedControlUtility.Services;

/// <summary>Reduz o uso de RAM quando a janela está minimizada (coleta de lixo + trim do working set).</summary>
public static class MemoryService
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static void Trim()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var p = Process.GetCurrentProcess();
            EmptyWorkingSet(p.Handle);
        }
        catch
        {
        }
    }
}
