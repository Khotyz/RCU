using System.Runtime.InteropServices;

namespace RedControlUtility.Services.Adl;

public static unsafe partial class DriverFsrService
{
    private static IntPtr Export(Session s, string name) => NativeLibrary.TryGetExport(s.Module, name, out IntPtr p) ? p : IntPtr.Zero;

    public static (bool Ok, string Message) SetFrtc(bool enable, int fps, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, UiHelper.L("Err.AtiMissing"));
            IntPtr pSet = Export(s, "ADL2_FPS_Settings_Set"), pGet = Export(s, "ADL2_FPS_Settings_Get"),
                   pCaps = Export(s, "ADL2_FPS_Caps");
            if (pSet == IntPtr.Zero) return (false, UiHelper.L("Err.ExportMissing", "ADL2_FPS_Settings_Set"));

            if (pCaps != IntPtr.Zero)
            {
                int supported = 0, version = 0;
                int crc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int*, int>)pCaps)(s.Ctx, s.Adapter, &supported, &version);
                if (crc != 0 || supported == 0)
                {
                    return (false, UiHelper.L("Err.NoGlobalFps", crc, supported));
                }
            }

            int value = enable ? Math.Max(1, fps) : 0;

            int* inp = stackalloc int[10];
            for (int i = 0; i < 10; i++) inp[i] = 0;
            inp[0] = 40;
            inp[1] = 1;
            inp[2] = value;
            inp[3] = value;
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)pSet)(s.Ctx, s.Adapter, inp);
            if (rc != 0) return (false, $"ADL2_FPS_Settings_Set => {rc}");

            string back = "";
            if (pGet != IntPtr.Zero)
            {
                int* o = stackalloc int[10];
                for (int i = 0; i < 10; i++) o[i] = 0;
                o[0] = 36;
                int g = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)pGet)(s.Ctx, s.Adapter, o);
                if (g == 0)
                {
                    back = $" | lido: [{string.Join(", ", Enumerable.Range(0, 9).Select(i => o[i]))}]";
                }
                else
                {
                    back = $" | leitura => {g}";
                }
            }

            return (true, $"FRTC {(enable ? fps + " fps" : UiHelper.L("Common.Off"))} ({s.Name}){back}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private const int DisplayInfoSize = 552;
    private const int DisplayInfoMaskOffset = 544, DisplayInfoValueOffset = 548;
    private const int ConnectedAndMapped = 0x3;

    private readonly record struct AdlDisplay(int Logical, int LogicalAdapter);

    private static List<AdlDisplay> ConnectedDisplays(Session s, out int rc)
    {
        var result = new List<AdlDisplay>();
        rc = -1;
        IntPtr pInfo = Export(s, "ADL2_Display_DisplayInfo_Get");
        if (pInfo == IntPtr.Zero) return result;

        int n = 0;
        IntPtr list = IntPtr.Zero;
        rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, IntPtr*, int, int>)pInfo)(s.Ctx, s.Adapter, &n, &list, 0);
        if (rc != 0 || list == IntPtr.Zero || n <= 0) return result;
        try
        {
            for (int i = 0; i < n; i++)
            {
                byte* b = (byte*)list + (long)i * DisplayInfoSize;
                int value = *(int*)(b + DisplayInfoValueOffset);
                if ((value & ConnectedAndMapped) != ConnectedAndMapped) continue;
                result.Add(new AdlDisplay(*(int*)b, *(int*)(b + 8)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(list);
        }

        return result;
    }

    public static (bool Supported, bool Enabled, int Count, string Message) GetGpuScaling(int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, false, 0, UiHelper.L("Err.AtiMissing"));
            IntPtr pGet = Export(s, "ADL2_DFP_GPUScalingEnable_Get");
            if (pGet == IntPtr.Zero) return (false, false, 0, UiHelper.L("Err.ExportMissing", "ADL2_DFP_GPUScalingEnable_Get"));

            var displays = ConnectedDisplays(s, out int rc);
            if (displays.Count == 0) return (false, false, 0, UiHelper.L("Err.NoDisplay", rc));

            int supported = 0, enabled = 0;
            var log = new List<string>();
            foreach (var d in displays)
            {
                int sup = 0, cur = 0, def = 0;
                int g = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int>)pGet)(s.Ctx, d.LogicalAdapter, d.Logical, &sup, &cur, &def);
                log.Add($"tela {d.Logical}: rc={g} suporte={sup} atual={cur}");
                if (g == 0 && sup != 0)
                {
                    supported++;
                    if (cur != 0) enabled++;
                }
            }

            return (supported > 0, supported > 0 && enabled == supported, displays.Count, string.Join("; ", log));
        }
        catch (Exception ex)
        {
            return (false, false, 0, ex.Message);
        }
    }

    public static (bool Ok, string Message) SetGpuScaling(bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, UiHelper.L("Err.AtiMissing"));
            IntPtr pSet = Export(s, "ADL2_DFP_GPUScalingEnable_Set"), pGet = Export(s, "ADL2_DFP_GPUScalingEnable_Get"),
                   pRegSet = Export(s, "ADL2_Adapter_RegValueInt_Set");
            if (pSet == IntPtr.Zero) return (false, UiHelper.L("Err.ExportMissing", "ADL2_DFP_GPUScalingEnable_Set"));

            var displays = ConnectedDisplays(s, out int rc);
            if (displays.Count == 0) return (false, UiHelper.L("Err.NoDisplay", rc));

            var log = new List<string>();
            int okCount = 0;
            foreach (var d in displays)
            {
                if (pRegSet != IntPtr.Zero)
                {
                    IntPtr key = Marshal.StringToHGlobalAnsi($"GPUScaling{d.LogicalAdapter}{d.Logical}");
                    try
                    {
                        ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, int>)pRegSet)(s.Ctx, d.LogicalAdapter, 1, IntPtr.Zero, key, enable ? 1 : 0);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(key);
                    }
                }

                int r = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)pSet)(s.Ctx, d.LogicalAdapter, d.Logical, enable ? 1 : 0);
                string now = "";
                if (pGet != IntPtr.Zero)
                {
                    int sup = 0, cur = 0, def = 0;
                    int g = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int>)pGet)(s.Ctx, d.LogicalAdapter, d.Logical, &sup, &cur, &def);
                    now = g == 0 ? $" (suporte={sup}, atual={cur})" : $" (leitura {g})";
                    if (r == 0 && g == 0 && (cur != 0) != enable) r = -1;
                }

                log.Add($"tela {d.Logical}: {(r == 0 ? "ok" : r.ToString())}{now}");
                if (r == 0) okCount++;
            }

            return (okCount > 0, string.Join("; ", log));
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string? ReadReg(Session s, string key)
    {
        byte* val = stackalloc byte[0x100];
        new Span<byte>(val, 0x100).Clear();
        IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), k = Marshal.StringToHGlobalAnsi(key);
        try
        {
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, byte*, int>)s.PGet)(
                s.Ctx, s.Adapter, DriverPathOption, sub, k, 0x100, val);
            if (rc != 0) return null;
            string text = val[1] == 0 && val[0] != 0
                ? Marshal.PtrToStringUni((IntPtr)val) ?? string.Empty
                : Marshal.PtrToStringAnsi((IntPtr)val) ?? string.Empty;
            return text.Trim();
        }
        finally
        {
            Marshal.FreeHGlobal(sub);
            Marshal.FreeHGlobal(k);
        }
    }

    public static (bool Supported, bool Enabled, string Message) GetEnhancedSync(int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, false, UiHelper.L("Err.AtiMissing"));
            IntPtr pSup = Export(s, "ADL2_TurboSyncSupport_Get");
            int sup = 0;
            int rc = pSup != IntPtr.Zero ? ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, int>)pSup)(s.Ctx, s.Adapter, &sup) : -1;
            string? na = ReadReg(s, "TurboSync_NA");
            string? cur = ReadReg(s, "TurboSync");
            bool supported = rc == 0 && sup == 1 && na != "1";
            return (supported, cur == "1", $"ADL2_TurboSyncSupport_Get rc={rc} valor={sup}, TurboSync_NA={na ?? "-"}, TurboSync={cur ?? "-"}");
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message);
        }
    }

    public static (bool Ok, string Message) SetEnhancedSync(bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, UiHelper.L("Err.AtiMissing"));

            IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), key = Marshal.StringToHGlobalAnsi("TurboSync");
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                new Span<byte>((void*)buf, 16).Clear();
                *(byte*)buf = (byte)(enable ? '1' : '0');
                int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, IntPtr, int>)s.PSet)(
                    s.Ctx, s.Adapter, DriverPathOption, sub, key, 1, buf);
                if (rc != 0) return (false, $"ADL2_Adapter_RegValueString_Set => {rc}");
            }
            finally
            {
                Marshal.FreeHGlobal(sub);
                Marshal.FreeHGlobal(key);
                Marshal.FreeHGlobal(buf);
            }

            string notify = "";
            IntPtr pNotify = Export(s, "ADL2_User_Settings_Notify");
            if (pNotify != IntPtr.Zero)
            {
                int nrc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)pNotify)(s.Ctx, s.Adapter, 1, 1);
                notify = $", ADL2_User_Settings_Notify => {nrc}";
            }

            string? back = ReadReg(s, "TurboSync");
            bool ok = (back == "1") == enable;
            return (ok, $"TurboSync={(enable ? "1" : "0")} (leitura: {back ?? "-"}){notify}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public sealed record AdlFreeSync(bool Supported, bool Enabled, double MinHz, double MaxHz);

    public static List<AdlFreeSync> ReadFreeSync(int gpuIndex = 0)
    {
        var result = new List<AdlFreeSync>();
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return result;
            IntPtr pCap = Export(s, "ADL2_Display_FreeSync_Cap"), pGet = Export(s, "ADL2_Display_FreeSyncState_Get");
            if (pCap == IntPtr.Zero) return result;

            // Allocated once, outside the loop (CA2014): reused and zeroed for every display.
            int* cap = stackalloc int[8];
            foreach (var d in ConnectedDisplays(s, out _))
            {
                for (int i = 0; i < 8; i++) cap[i] = 0;
                int crc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int>)pCap)(s.Ctx, d.LogicalAdapter, d.Logical, cap);
                double min = crc == 0 ? cap[1] / 1_000_000.0 : 0, max = crc == 0 ? cap[2] / 1_000_000.0 : 0;
                bool sup = false, on = false;
                int cur = 0, def = 0, mn = 0, mx = 0, grc = -1;
                if (pGet != IntPtr.Zero)
                {
                    grc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int*, int>)pGet)(s.Ctx, d.LogicalAdapter, d.Logical, &cur, &def, &mn, &mx);
                    sup = grc == 0;
                    on = sup && cur != 0;
                    if (sup && mn > 0 && mx > 0) { min = mn / 1_000_000.0; max = mx / 1_000_000.0; }
                }

                LogService.Info($"ADL FreeSync display {d.Logical}: cap rc={crc} caps=0x{cap[0]:X} state rc={grc} cur={cur} def={def} min={min:0.#} max={max:0.#}");
                result.Add(new AdlFreeSync(sup, on, min, max));
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"DriverFsrService.ReadFreeSync: {ex.Message}");
        }

        return result;
    }

    public static (bool Ok, string Message) SetFreeSync(int ordinal, bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, UiHelper.L("Err.AtiMissing"));
            IntPtr pSet = Export(s, "ADL2_Display_FreeSyncState_Set"), pGet = Export(s, "ADL2_Display_FreeSyncState_Get");
            if (pSet == IntPtr.Zero || pGet == IntPtr.Zero) return (false, UiHelper.L("Err.ExportMissing", "ADL2_Display_FreeSyncState_Set"));

            var displays = ConnectedDisplays(s, out int rc);
            if (ordinal < 0 || ordinal >= displays.Count) return (false, UiHelper.L("Err.NoDisplay", rc));
            var d = displays[ordinal];

            int cur = 0, def = 0, mn = 0, mx = 0;
            ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int*, int>)pGet)(s.Ctx, d.LogicalAdapter, d.Logical, &cur, &def, &mn, &mx);
            int setting = enable ? 7 : 0;
            int r = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int, int>)pSet)(s.Ctx, d.LogicalAdapter, d.Logical, setting, 0);
            int g = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int*, int>)pGet)(s.Ctx, d.LogicalAdapter, d.Logical, &cur, &def, &mn, &mx);
            if (r == 0 && g == 0 && (cur != 0) != enable) r = -1;
            return (r == 0, $"rc={r} state={cur}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public sealed record AdlDisplayEntry(int Logical, string Name, bool ScalingSupported, bool ScalingOn);

    public static List<AdlDisplayEntry> ListDisplays(int gpuIndex = 0)
    {
        var result = new List<AdlDisplayEntry>();
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return result;
            IntPtr pInfo = Export(s, "ADL2_Display_DisplayInfo_Get"), pGet = Export(s, "ADL2_DFP_GPUScalingEnable_Get");
            if (pInfo == IntPtr.Zero) return result;

            int n = 0;
            IntPtr list = IntPtr.Zero;
            int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int*, IntPtr*, int, int>)pInfo)(s.Ctx, s.Adapter, &n, &list, 0);
            if (rc != 0 || list == IntPtr.Zero || n <= 0) return result;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    byte* b = (byte*)list + (long)i * DisplayInfoSize;
                    if ((*(int*)(b + DisplayInfoValueOffset) & ConnectedAndMapped) != ConnectedAndMapped) continue;
                    int logical = *(int*)b, logicalAdapter = *(int*)(b + 8);
                    string name = Marshal.PtrToStringAnsi((IntPtr)(b + 20), 256)?.Split('\0')[0].Trim() ?? string.Empty;
                    int sup = 0, cur = 0, def = 0;
                    bool got = pGet != IntPtr.Zero &&
                               ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int*, int*, int*, int>)pGet)(s.Ctx, logicalAdapter, logical, &sup, &cur, &def) == 0;
                    result.Add(new AdlDisplayEntry(logical, name, got && sup != 0, got && cur != 0));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(list);
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"DriverFsrService.ListDisplays: {ex.Message}");
        }

        return result;
    }

    public static (bool Available, bool Enabled) GetUmdFlag(string key, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, false);
            bool na = ReadReg(s, key + "_NA") == "1";
            return (!na, ReadReg(s, key) == "1");
        }
        catch
        {
            return (false, false);
        }
    }

    public static (bool Ok, string Message) SetUmdFlag(string key, bool enable, int gpuIndex = 0)
    {
        try
        {
            using var s = Open(gpuIndex);
            if (s == null) return (false, UiHelper.L("Err.AtiMissing"));
            IntPtr sub = Marshal.StringToHGlobalAnsi(SubKey), k = Marshal.StringToHGlobalAnsi(key);
            IntPtr buf = Marshal.AllocHGlobal(16);
            try
            {
                new Span<byte>((void*)buf, 16).Clear();
                *(byte*)buf = (byte)(enable ? '1' : '0');
                int rc = ((delegate* unmanaged[Cdecl]<IntPtr, int, int, IntPtr, IntPtr, int, IntPtr, int>)s.PSet)(
                    s.Ctx, s.Adapter, DriverPathOption, sub, k, 1, buf);
                if (rc != 0) return (false, $"ADL2_Adapter_RegValueString_Set => {rc}");
            }
            finally
            {
                Marshal.FreeHGlobal(sub);
                Marshal.FreeHGlobal(k);
                Marshal.FreeHGlobal(buf);
            }

            IntPtr pNotify = Export(s, "ADL2_User_Settings_Notify");
            if (pNotify != IntPtr.Zero) ((delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int>)pNotify)(s.Ctx, s.Adapter, 1, 1);

            string? back = ReadReg(s, key);
            return ((back == "1") == enable, $"{key}={(enable ? "1" : "0")} (leitura: {back ?? "-"})");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
