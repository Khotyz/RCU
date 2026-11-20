using RedControlUtility.Models;

namespace RedControlUtility.Services.Adlx;

/// <summary>
/// Sessão ADLX de alto nível: enumeração de GPUs, sensores, SAM (Resizable BAR), 3D, tuning e displays.
/// Todos os acessos são serializados por um lock (o ADLX é chamado da UI e da thread de sensores).
/// </summary>
public sealed partial class AdlxSession : IDisposable
{
    // Slots da vtable (ver comentário em AdlxObject). IADLXSystem: sem Acquire/Release.
    private static class SysSlot
    {
        public const int GetGPUs = 1;
        public const int GetDisplaysServices = 3;
        public const int Get3DSettingsServices = 7;
        public const int GetGPUTuningServices = 8;
        public const int GetPerformanceMonitoringServices = 9;
        public const int TotalSystemRAM = 10;
    }

    private static class GpuSlot
    {
        public const int VendorId = 3, AsicFamily = 4, Type = 5, IsExternal = 6, Name = 7, DriverPath = 8, PnpString = 9,
            HasDesktops = 10, TotalVram = 11, VramType = 12, BiosInfo = 13, DeviceId = 14, RevisionId = 15,
            SubSystemId = 16, SubSystemVendorId = 17, UniqueId = 18;
    }

    private readonly object _sync = new();
    private AdlxLibrary? _lib;
    private AdlxObject? _system;
    private readonly List<AdlxObject> _gpus = new();
    private readonly List<GpuInfo> _gpuInfos = new();
    private AdlxObject? _gpu;

    private AdlxObject? _perf;
    private AdlxObject? _tuning;
    private AdlxObject? _tuning1;
    private AdlxObject? _3d;
    private AdlxObject? _3d1;
    private AdlxObject? _3d2;
    private AdlxObject? _3d3;
    private AdlxObject? _displays;
    private bool _perfTracking;

    public string BackendName => _lib?.BackendName ?? string.Empty;
    public string AdlxVersion => _lib == null ? string.Empty : (string.IsNullOrEmpty(_lib.Version) ? AdlxLibrary.FormatVersion(_lib.FullVersion) : _lib.Version);
    public IReadOnlyList<GpuInfo> Gpus => _gpuInfos;
    public int SelectedIndex { get; private set; } = -1;
    public string InitReport { get; private set; } = string.Empty;

    private AdlxSession()
    {
    }

    public static AdlxSession? Open(AdlxBackendKind kind, out string report)
    {
        var log = new System.Text.StringBuilder();
        string[] attempts = kind switch
        {
            AdlxBackendKind.AmdDll => new[] { "amd", "amd-driver" },
            AdlxBackendKind.HelperDll => new[] { "helper" },
            _ => new[] { "amd", "amd-driver", "helper" }
        };

        foreach (string attempt in attempts)
        {
            AdlxLibrary? lib;
            try
            {
                lib = AdlxLibrary.OpenAttempt(attempt, log);
            }
            catch (Exception ex)
            {
                log.AppendLine($"[{attempt}] Exceção ao abrir o ADLX: {ex.Message}");
                continue;
            }

            if (lib == null) continue;

            var session = new AdlxSession { _lib = lib };
            try
            {
                session._system = new AdlxObject(lib.System, refCounted: false);
                if (!session.EnumerateGpusInternal(log))
                {
                    log.AppendLine($"[{attempt}] ADLX inicializou ({lib.BackendName}), mas nenhuma GPU foi enumerada. Tentando o próximo método.");
                    session.Dispose();
                    continue;
                }

                session.AcquireServices(log);
                report = log.ToString().Trim();
                session.InitReport = report;
                return session;
            }
            catch (Exception ex)
            {
                log.AppendLine($"[{attempt}] Exceção ao preparar a sessão ADLX: {ex.Message}");
                session.Dispose();
            }
        }

        report = log.ToString().Trim();
        return null;
    }

    // ------------------------------------------------------------------ GPUs

    private bool EnumerateGpusInternal(System.Text.StringBuilder log)
    {
        if (_system == null) return false;

        var hg = _system.GetInt(0, out int hgType);
        log.AppendLine($"[ADLX] GetHybridGraphicsType => {hg} (tipo {hgType})");

        var rGpus = _system.GetPtr(SysSlot.GetGPUs, out IntPtr listPtr);
        log.AppendLine($"[ADLX] GetGPUs => {rGpus} ({rGpus.Describe()}), lista=0x{listPtr.ToInt64():X}");
        if (!rGpus.Succeeded() || listPtr == IntPtr.Zero)
        {
            return false;
        }

        using var list = new AdlxObject(listPtr);
        uint count = list.ListSize();
        log.AppendLine($"[ADLX] Lista de GPUs: Size() = {count}");
        int idx = 0;
        var listed = list.ListItems(log, typedAtSlot: 11);
        log.AppendLine($"[ADLX] Itens lidos com At(): {listed.Count}");
        foreach (var item in listed)
        {
            // Obtém o ponteiro tipado como IADLXGPU.
            AdlxObject gpuObj = item.Query("IADLXGPU") ?? item;
            if (!ReferenceEquals(gpuObj, item))
            {
                item.Dispose();
            }

            _gpus.Add(gpuObj);
            _gpuInfos.Add(DescribeGpu(gpuObj, idx++));
        }

        if (_gpus.Count == 0) return false;

        // GPU padrão: a discreta com mais VRAM.
        int best = 0;
        for (int i = 1; i < _gpuInfos.Count; i++)
        {
            var a = _gpuInfos[i];
            var b = _gpuInfos[best];
            if ((a.IsDiscrete && !b.IsDiscrete) || (a.IsDiscrete == b.IsDiscrete && a.VramMb > b.VramMb))
            {
                best = i;
            }
        }

        SelectGpuInternal(best);
        return true;
    }

    private static GpuInfo DescribeGpu(AdlxObject g, int index)
    {
        var info = new GpuInfo { Index = index, FromAdlx = true };

        if (g.GetString(GpuSlot.Name, out string name).Succeeded() && !string.IsNullOrWhiteSpace(name)) info.Name = name.Trim();
        if (g.GetString(GpuSlot.VendorId, out string vid).Succeeded() && vid.Length > 0) info.VendorId = vid;
        if (g.GetString(GpuSlot.DeviceId, out string did).Succeeded()) info.DeviceId = did;
        if (g.GetString(GpuSlot.RevisionId, out string rid).Succeeded()) info.RevisionId = rid;
        if (g.GetString(GpuSlot.SubSystemId, out string ssid).Succeeded()) info.SubSystemId = ssid;
        if (g.GetString(GpuSlot.SubSystemVendorId, out string ssv).Succeeded()) info.SubSystemVendorId = ssv;
        if (g.GetString(GpuSlot.PnpString, out string pnp).Succeeded()) info.PnpString = pnp;
        if (g.GetString(GpuSlot.VramType, out string vt).Succeeded()) info.VramType = vt;
        if (g.GetInt(GpuSlot.UniqueId, out int uid).Succeeded()) info.UniqueId = uid;
        if (g.GetUInt(GpuSlot.TotalVram, out uint vram).Succeeded()) info.VramMb = (int)vram;
        if (g.GetInt(GpuSlot.Type, out int type).Succeeded()) info.IsDiscrete = type != 1; // 1 = GPUTYPE_INTEGRATED
        if (g.GetString3(GpuSlot.BiosInfo, out string part, out string ver, out _).Succeeded())
        {
            info.BiosPartNumber = part;
            info.BiosVersion = ver;
        }

        return info;
    }

    public void SelectGpu(int index)
    {
        lock (_sync)
        {
            SelectGpuInternal(index);
            ReleaseServices();
            AcquireServices();
        }
    }

    private void SelectGpuInternal(int index)
    {
        if (index < 0 || index >= _gpus.Count) index = 0;
        SelectedIndex = index;
        _gpu = _gpus[index];
    }

    // ------------------------------------------------------------------ Serviços

    private void AcquireServices(System.Text.StringBuilder? log = null)
    {
        if (_system == null) return;

        _perf = GetService(SysSlot.GetPerformanceMonitoringServices);
        _tuning = GetService(SysSlot.GetGPUTuningServices);
        _3d = GetService(SysSlot.Get3DSettingsServices);
        _displays = GetService(SysSlot.GetDisplaysServices);

        _tuning1 = _tuning?.Query("IADLXGPUTuningServices1");
        _3d1 = _3d?.Query("IADLX3DSettingsServices1");
        _3d2 = _3d?.Query("IADLX3DSettingsServices2");
        _3d3 = _3d?.Query("IADLX3DSettingsServices3");

        log?.AppendLine($"[ADLX] serviços: perf={_perf != null} tuning={_tuning != null} (Tuning1={_tuning1 != null}) 3D={_3d != null} (S1={_3d1 != null}, S2={_3d2 != null}, S3={_3d3 != null}) displays={_displays != null}");

        if (_perf != null)
        {
            // O ADLX só mantém métricas "atuais" se o tracking estiver ligado.
            _perf.SetInt(4, 500); // SetSamplingInterval(ms) - best effort
            _perfTracking = _perf.Invoke(11).Succeeded(); // StartPerformanceMetricsTracking
        }
    }

    private AdlxObject? GetService(int slot)
    {
        if (_system != null && _system.GetPtr(slot, out IntPtr p).Succeeded() && p != IntPtr.Zero)
        {
            return new AdlxObject(p);
        }

        return null;
    }

    private void ReleaseServices()
    {
        if (_perf != null && _perfTracking)
        {
            try { _perf.Invoke(12); } catch { } // StopPerformanceMetricsTracking
        }

        _perfTracking = false;
        foreach (var o in new[] { _perf, _tuning1, _tuning, _3d3, _3d2, _3d1, _3d, _displays })
        {
            o?.Dispose();
        }

        _perf = _tuning = _tuning1 = _3d = _3d1 = _3d2 = _3d3 = _displays = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ReleaseServices();
            foreach (var g in _gpus) g.Dispose();
            _gpus.Clear();
            _gpu = null;
            _system = null; // pertence à biblioteca (sem refcount)
            _lib?.Dispose();
            _lib = null;
        }
    }

    // ------------------------------------------------------------------ Sensores

    public MetricsSnapshot ReadMetrics()
    {
        var snap = new MetricsSnapshot { Timestamp = DateTime.Now };
        lock (_sync)
        {
            if (_perf == null || _gpu == null)
            {
                snap.Error = "Serviço de monitoramento indisponível";
                return snap;
            }

            try
            {
                if (!_perf.GetPtrWith(18, _gpu.Ptr, out IntPtr p).Succeeded() || p == IntPtr.Zero) // GetCurrentGPUMetrics
                {
                    snap.Error = "GetCurrentGPUMetrics falhou";
                    return snap;
                }

                using var m = new AdlxObject(p);
                snap.Valid = true;

                if (m.GetDouble(4, out double usage).Succeeded()) snap.GpuUsage = usage;
                if (m.GetInt(5, out int clk).Succeeded()) snap.GpuClockMhz = clk;
                if (m.GetInt(6, out int vclk).Succeeded()) snap.VramClockMhz = vclk;
                if (m.GetDouble(7, out double t).Succeeded()) snap.TempEdgeC = t;
                if (m.GetDouble(8, out double hs).Succeeded()) snap.TempHotspotC = hs;
                if (m.GetDouble(9, out double pw).Succeeded()) snap.PowerW = pw;
                if (m.GetDouble(10, out double bp).Succeeded()) snap.BoardPowerW = bp;
                if (m.GetInt(11, out int fan).Succeeded()) snap.FanRpm = fan;
                if (m.GetInt(12, out int vram).Succeeded()) snap.VramUsedMb = vram;
                if (m.GetInt(13, out int volt).Succeeded()) snap.VoltageMv = volt;

                using var m1 = m.Query("IADLXGPUMetrics1");
                if (m1 != null && m1.GetDouble(15, out double mt).Succeeded()) snap.TempMemoryC = mt;

                using var m3 = m.Query("IADLXGPUMetrics3");
                if (m3 != null && m3.GetInt(19, out int duty).Succeeded()) snap.FanDutyPercent = duty;
            }
            catch (Exception ex)
            {
                snap.Valid = false;
                snap.Error = ex.Message;
            }
        }

        return snap;
    }

    public int TotalSystemRamMb()
    {
        lock (_sync)
        {
            return _system != null && _system.GetUInt(SysSlot.TotalSystemRAM, out uint ram).Succeeded() ? (int)ram : 0;
        }
    }

    // ------------------------------------------------------------------ SAM / Resizable BAR

    public (bool Supported, bool Enabled) ReadSam()
    {
        lock (_sync)
        {
            using var sam = GetSam();
            if (sam == null) return (false, false);
            bool supported = sam.Supported(3);
            bool enabled = supported && sam.GetBool(4, out bool e).Succeeded() && e;
            return (supported, enabled);
        }
    }

    public AdlxResult SetSam(bool enabled)
    {
        lock (_sync)
        {
            using var sam = GetSam();
            return sam == null ? AdlxResult.NotSupported : sam.SetBool(5, enabled);
        }
    }

    private AdlxObject? GetSam()
    {
        if (_tuning1 == null || _gpu == null) return null;
        // IADLXGPUTuningServices1::GetSmartAccessMemory (slot 18)
        return _tuning1.GetPtrWith(18, _gpu.Ptr, out IntPtr p).Succeeded() && p != IntPtr.Zero ? new AdlxObject(p) : null;
    }
}
