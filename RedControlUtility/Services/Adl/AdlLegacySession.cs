using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RedControlUtility.Models;

namespace RedControlUtility.Services.Adl;

public sealed unsafe partial class AdlLegacySession : IDisposable
{
    private const int AdapterInfoSize = 1572;
    private const int BufSize = 512;

    private IntPtr _module;
    private IntPtr _ctx;
    private int _adapter = -1;

    private delegate* unmanaged[Cdecl]<IntPtr, int> _destroy;
    private delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, int> _perfStatus;
    private delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int> _temperature;
    private delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, int> _fan5;

    public string AdapterName { get; private set; } = string.Empty;

    private AdlLegacySession()
    {
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static IntPtr Alloc(int size) => Marshal.AllocHGlobal(Math.Max(1, size));

    public static List<(string Name, int Bdf)> ReadPciLocations()
    {
        var list = new List<(string Name, int Bdf)>();
        string path = Path.Combine(Environment.SystemDirectory, "atiadlxx.dll");
        if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out IntPtr module)) return list;
        try
        {
            IntPtr Exp(string n) => NativeLibrary.TryGetExport(module, n, out IntPtr p) ? p : IntPtr.Zero;
            IntPtr pCreate = Exp("ADL2_Main_Control_Create"), pDestroy = Exp("ADL2_Main_Control_Destroy");
            IntPtr pNum = Exp("ADL2_Adapter_NumberOfAdapters_Get"), pInfo = Exp("ADL2_Adapter_AdapterInfo_Get");
            if (pCreate == IntPtr.Zero || pDestroy == IntPtr.Zero || pNum == IntPtr.Zero || pInfo == IntPtr.Zero) return list;

            IntPtr ctx = IntPtr.Zero;
            var alloc = (IntPtr)(delegate* unmanaged[Stdcall]<int, IntPtr>)&Alloc;
            if (((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr*, int>)pCreate)(alloc, 1, &ctx) != 0 || ctx == IntPtr.Zero) return list;
            try
            {
                int n = 0;
                if (((delegate* unmanaged[Cdecl]<IntPtr, int*, int>)pNum)(ctx, &n) != 0 || n <= 0 || n > 64) return list;
                int total = n * AdapterInfoSize;
                IntPtr buf = Marshal.AllocHGlobal(total + BufSize);
                try
                {
                    new Span<byte>((void*)buf, total + BufSize).Clear();
                    if (((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int>)pInfo)(ctx, buf, total) != 0) return list;
                    for (int i = 0; i < n; i++)
                    {
                        byte* b = (byte*)buf + i * AdapterInfoSize;
                        int bus = *(int*)(b + 264), dev = *(int*)(b + 268), func = *(int*)(b + 272), vendor = *(int*)(b + 276);
                        string name = Marshal.PtrToStringAnsi((IntPtr)(b + 280), 256).Split('\0')[0];
                        if (vendor != 0x1002 && vendor != 1002 && !name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) continue;
                        int bdf = (bus << 8) | (dev << 3) | func;
                        if (!list.Any(x => x.Bdf == bdf)) list.Add((name, bdf));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
            finally
            {
                if (pDestroy != IntPtr.Zero) ((delegate* unmanaged[Cdecl]<IntPtr, int>)pDestroy)(ctx);
            }
        }
        catch
        {
        }

        return list;
    }

    public static AdlLegacySession? Open(int gpuIndex, out string report) => Open(gpuIndex, out report, null, null);

    internal static AdlLegacySession? OpenForGpu(GpuInfo gpu, int? pciLocation, out string report)
    {
        if (GpuArchitectureDetector.TryParseDeviceId(gpu.DeviceId, out int deviceId) ||
            GpuArchitectureDetector.TryParseDeviceId(gpu.PnpString, out deviceId))
            return Open(0, out report, deviceId, pciLocation);
        report = "[ADL] PCI device ID unavailable.";
        return null;
    }

    private static AdlLegacySession? Open(int gpuIndex, out string report, int? deviceId, int? pciLocation)
    {
        var log = new StringBuilder();
        report = string.Empty;
        string path = Path.Combine(Environment.SystemDirectory, "atiadlxx.dll");
        if (!File.Exists(path))
        {
            report = UiHelper.L("Diag.AdlDllMissing");
            return null;
        }

        if (!NativeLibrary.TryLoad(path, out IntPtr module))
        {
            report = "[ADL] Could not load atiadlxx.dll.";
            return null;
        }

        var s = new AdlLegacySession { _module = module };
        try
        {
            IntPtr Exp(string n) => NativeLibrary.TryGetExport(module, n, out IntPtr p) ? p : IntPtr.Zero;

            IntPtr pCreate = Exp("ADL2_Main_Control_Create");
            IntPtr pDestroy = Exp("ADL2_Main_Control_Destroy");
            IntPtr pNum = Exp("ADL2_Adapter_NumberOfAdapters_Get");
            IntPtr pInfo = Exp("ADL2_Adapter_AdapterInfo_Get");
            IntPtr pPerf = Exp("ADL2_OverdriveN_PerformanceStatus_Get");
            IntPtr pTemp = Exp("ADL2_OverdriveN_Temperature_Get");
            IntPtr pFan = Exp("ADL2_Overdrive5_FanSpeed_Get");

            if (pCreate == IntPtr.Zero || pDestroy == IntPtr.Zero || pNum == IntPtr.Zero || pInfo == IntPtr.Zero)
            {
                report = UiHelper.L("Diag.AdlExportsMissing");
                s.Dispose();
                return null;
            }

            s._destroy = (delegate* unmanaged[Cdecl]<IntPtr, int>)pDestroy;
            s._perfStatus = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, int>)pPerf;
            s._temperature = (delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int>)pTemp;
            s._fan5 = (delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, int>)pFan;

            IntPtr ctx = IntPtr.Zero;
            var alloc = (IntPtr)(delegate* unmanaged[Stdcall]<int, IntPtr>)&Alloc;
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr*, int>)pCreate)(alloc, 1, &ctx);
            log.AppendLine($"[ADL] ADL2_Main_Control_Create => {rc}");
            if (rc != 0 || ctx == IntPtr.Zero)
            {
                report = log.ToString().Trim();
                s.Dispose();
                return null;
            }

            s._ctx = ctx;

            int n = 0;
            rc = ((delegate* unmanaged[Cdecl]<IntPtr, int*, int>)pNum)(ctx, &n);
            if (rc != 0 || n <= 0 || n > 64)
            {
                log.AppendLine($"[ADL] NumberOfAdapters => rc {rc}, n {n}");
                report = log.ToString().Trim();
                s.Dispose();
                return null;
            }

            int total = n * AdapterInfoSize;
            IntPtr buf = Marshal.AllocHGlobal(total + BufSize);
            try
            {
                new Span<byte>((void*)buf, total + BufSize).Clear();
                rc = ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int, int>)pInfo)(ctx, buf, total);
                if (rc != 0)
                {
                    log.AppendLine($"[ADL] AdapterInfo_Get => {rc}");
                    report = log.ToString().Trim();
                    s.Dispose();
                    return null;
                }

                var found = new List<(int Index, int Location, string Name)>();
                for (int i = 0; i < n; i++)
                {
                    byte* b = (byte*)buf + i * AdapterInfoSize;
                    int index = *(int*)(b + 4);
                    int bus = *(int*)(b + 264);
                    int vendor = *(int*)(b + 276);
                    int present = *(int*)(b + 792);
                    string name = Marshal.PtrToStringAnsi((IntPtr)(b + 280), 256).Split('\0')[0];
                    log.AppendLine($"[ADL] adapter #{i}: index={index} bus={bus} vendor=0x{vendor:X} present={present} name=\"{name}\"");
                    if (deviceId is int targetId)
                    {
                        int dev = *(int*)(b + 268), func = *(int*)(b + 272);
                        if (vendor is not (0x1002 or 1002) || index < 0 || bus is < 0 or > 255 ||
                            dev is < 0 or > 31 || func is < 0 or > 7 || *(int*)(b + 796) == 0) continue;
                        int location = (bus << 8) | (dev << 3) | func;
                        if (pciLocation != null && location != pciLocation) continue;
                        string pnp = Marshal.PtrToStringAnsi((IntPtr)(b + 1312), 256).Split('\0')[0];
                        string udid = Marshal.PtrToStringAnsi((IntPtr)(b + 8), 256).Split('\0')[0];
                        if ((!GpuArchitectureDetector.TryParseDeviceId(pnp, out int id) &&
                             !GpuArchitectureDetector.TryParseDeviceId(udid, out id)) || id != targetId) continue;
                        if (!found.Any(f => f.Location == location)) found.Add((index, location, name));
                    }
                    else if ((vendor is 0x1002 or 1002 || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("AMD", StringComparison.OrdinalIgnoreCase)) && !found.Any(f => f.Location == bus))
                    {
                        found.Add((index, bus, name));
                    }
                }

                if (found.Count == 0 || (deviceId != null && found.Count != 1))
                {
                    log.AppendLine("[ADL] No unambiguous AMD adapter match.");
                    report = log.ToString().Trim();
                    s.Dispose();
                    return null;
                }

                var pick = found[deviceId == null ? Math.Clamp(gpuIndex, 0, found.Count - 1) : 0];
                s._adapter = pick.Index;
                s.AdapterName = pick.Name;
                log.AppendLine($"[ADL] Selected adapter {pick.Index}: {pick.Name}");
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }

            report = log.ToString().Trim();
            return s;
        }
        catch (Exception ex)
        {
            report = UiHelper.L("Diag.AdlException", ex.Message);
            s.Dispose();
            return null;
        }
    }

    private static int? Clock(int v)
    {
        if (v <= 0) return null;
        int mhz = v > 20000 ? v / 100 : v;
        return mhz is >= 50 and <= 4500 ? mhz : null;
    }

    public MetricsSnapshot ReadMetrics()
    {
        var snap = new MetricsSnapshot { Timestamp = DateTime.Now };
        if (_ctx == IntPtr.Zero || _adapter < 0)
        {
            snap.Error = UiHelper.L("Err.AdlUnavailable");
            return snap;
        }

        IntPtr buf = Marshal.AllocHGlobal(BufSize);
        try
        {
            new Span<byte>((void*)buf, BufSize).Clear();

            if (_perfStatus != null && _perfStatus(_ctx, _adapter, buf) == 0)
            {
                int* p = (int*)buf;
                snap.GpuClockMhz = Clock(p[3]) ?? Clock(p[0]);
                snap.VramClockMhz = Clock(p[1]);
                if (p[6] is >= 0 and <= 100) snap.GpuUsage = p[6];
                if (p[17] is >= 300 and <= 2000) snap.VoltageMv = p[17];
            }

            if (_temperature != null)
            {
                int t = 0;
                if (_temperature(_ctx, _adapter, 1, &t) == 0)
                {
                    double c = t > 1000 ? t / 1000.0 : t;
                    if (c is > 0 and < 150) snap.TempEdgeC = c;
                }
            }

            if (_fan5 != null)
            {
                foreach (int type in new[] { 2, 1 })
                {
                    new Span<byte>((void*)buf, BufSize).Clear();
                    int* f = (int*)buf;
                    f[0] = 16;
                    f[1] = type;
                    if (_fan5(_ctx, _adapter, 0, buf) == 0 && f[2] > 0)
                    {
                        if (type == 2) snap.FanRpm = f[2];
                        else snap.FanDutyPercent = f[2];
                        break;
                    }
                }
            }

            snap.Valid = snap.GpuClockMhz != null || snap.TempEdgeC != null || snap.GpuUsage != null || snap.FanRpm != null || snap.FanDutyPercent != null;
            if (!snap.Valid) snap.Error = UiHelper.L("Err.AdlNoMetrics");
        }
        catch (Exception ex)
        {
            snap.Valid = false;
            snap.Error = ex.Message;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }

        return snap;
    }

    public void Dispose()
    {
        try
        {
            if (_ctx != IntPtr.Zero && _destroy != null) _destroy(_ctx);
        }
        catch
        {
        }

        _ctx = IntPtr.Zero;
        if (_module != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_module); } catch { }
            _module = IntPtr.Zero;
        }
    }
}
