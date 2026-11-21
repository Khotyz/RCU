using System.Text;
using RedControlUtility.Models;
using RedControlUtility.Services.Adl;
using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Services;

public sealed class HardwareService : IDisposable
{
    private AdlxSession? _session;
    private AdlLegacySession? _adl;
    private AdlLegacySession? _overdrive;
    private TuningCapabilities _adlxTuning = new();
    private readonly object _tuningLock = new();
    private readonly List<GpuInfo> _gpus = new();

    public GpuInfo CurrentGpu { get; private set; } = new();
    public GpuCapabilities Caps { get; private set; } = new();
    public IReadOnlyList<GpuInfo> AllGpus => _gpus;
    public bool AdlxAvailable => _session != null;
    public bool LegacySensors => _adl != null;
    public string BackendName => _session?.BackendName ?? (_overdrive != null ? "AMD Display Library Overdrive N" :
        _adl != null ? UiHelper.L("Backend.LegacyAdl") : UiHelper.L("Backend.ReadOnly"));
    public string AdlxVersion => _session?.AdlxVersion ?? string.Empty;
    public string InitReport { get; private set; } = string.Empty;

    public TuningSettings? TuningBaseline { get; private set; }

    public event Action? Reinitialized;

    public void Initialize(AdlxBackendKind kind, int gpuIndex)
    {
        lock (_tuningLock) InitializeCore(kind, gpuIndex);
        Reinitialized?.Invoke();
    }

    private void InitializeCore(AdlxBackendKind kind, int gpuIndex)
    {
        _session?.Dispose();
        _session = null;
        _adl?.Dispose();
        _adl = null;
        CloseOverdrive();
        _gpus.Clear();
        TuningBaseline = null;

        try
        {
            _session = AdlxSession.Open(kind, out string report);
            InitReport = report;
            LogService.Info($"ADLX init ({kind}):{Environment.NewLine}{report}");
        }
        catch (Exception ex)
        {
            InitReport = UiHelper.L("Diag.Exception", ex.Message);
            LogService.Error($"ADLX init: {ex}");
        }

        if (_session != null)
        {
            _gpus.AddRange(_session.Gpus);
            SelectGpuCore(gpuIndex);
            LogService.Info($"ADLX OK via {_session.BackendName} (v{_session.AdlxVersion}). GPU: {CurrentGpu.Name} | {CurrentGpu.ArchitectureText} | DevID {CurrentGpu.DeviceId}");
        }
        else
        {
            LoadFallback();
            RefreshCapabilities();
            LogService.Warn($"ADLX unavailable. Fallback: {CurrentGpu.Name} | {CurrentGpu.ArchitectureText}");
        }

        RefreshLegacySensors(CurrentGpu.Index);
    }

    private void RefreshLegacySensors(int gpuIndex)
    {
        _adl?.Dispose();
        _adl = null;
        if (_session == null || !Caps.MetricsAvailable)
        {
            try
            {
                _adl = CurrentGpu.Architecture == GpuArchitecture.Polaris
                    ? AdlLegacySession.OpenForGpu(CurrentGpu, _session?.GetPciLocation(), out string adlReport)
                    : AdlLegacySession.Open(gpuIndex, out adlReport);
                InitReport += (InitReport.Length > 0 ? Environment.NewLine : string.Empty) + adlReport;
                LogService.Info(adlReport);
                if (_adl != null && _adl.ReadMetrics().Valid) Caps.MetricsAvailable = true;
                else if (_adl != null) { _adl.Dispose(); _adl = null; }
            }
            catch (Exception ex)
            {
                LogService.Error($"ADL: {ex.Message}");
            }
        }
    }

    public void SelectGpu(int index)
    {
        lock (_tuningLock)
        {
            if (_session == null || _gpus.Count == 0) return;
            SelectGpuCore(index);
            RefreshLegacySensors(CurrentGpu.Index);
        }
        Reinitialized?.Invoke();
    }

    private void SelectGpuCore(int index)
    {
        if (_session == null || _gpus.Count == 0) return;
        index = Math.Clamp(index, 0, _gpus.Count - 1);
        _session.SelectGpu(index);
        CurrentGpu = _gpus[index];
        TuningBaseline = null;

        var (arch, chip) = GpuArchitectureDetector.Detect(CurrentGpu.DeviceId, CurrentGpu.PnpString, CurrentGpu.Name);
        CurrentGpu.Architecture = arch;
        CurrentGpu.ChipName = chip;
        DriverInfoService.Enrich(CurrentGpu);

        RefreshCapabilities();
    }

    private void RefreshCapabilities()
    {
        var caps = new GpuCapabilities { AdlxAvailable = _session != null };
        if (_session != null)
        {
            var gs = _session.ReadGraphics(out var g);
            PatchEnhancedSyncCaps(g, gs);
            PatchUmdCaps(g, gs);
            caps.Graphics = g;
            var (sup, en) = _session.ReadSam();
            caps.SamSupported = sup;
            caps.SamEnabled = en;
            caps.MetricsAvailable = _session.ReadMetrics().Valid;
        }

        CloseOverdrive();
        bool polaris = CurrentGpu.Architecture == GpuArchitecture.Polaris;
        _adlxTuning = _session?.QueryTuningCapabilities(polaris) ?? new();
        if (polaris)
        {
            bool needFan = !_adlxTuning.FanCurveSupported && !_adlxTuning.FanMinRpmRange.IsValid && !_adlxTuning.FanTargetRpmRange.IsValid;
            _overdrive = AdlLegacySession.OpenOverdriveN(CurrentGpu, _session?.GetPciLocation(),
                _adlxTuning.GfxMode == GfxTuningMode.None, needFan, !_adlxTuning.VramFreqSupported, !_adlxTuning.PowerSupported);
        }
        caps.Tuning = MergeTuningCapabilities();
        Caps = caps;
    }

    private void CloseOverdrive()
    {
        _overdrive?.Dispose();
        _overdrive = null;
    }

    private TuningCapabilities MergeTuningCapabilities()
    {
        var caps = _adlxTuning.Copy();
        if (_overdrive == null) return caps;
        var legacy = _overdrive.OverdriveTuning;
        caps.AtFactoryKnown = false;
        if (legacy.GfxMode == GfxTuningMode.StateList)
        {
            caps.GfxMode = legacy.GfxMode;
            caps.StateFreqRange = legacy.StateFreqRange;
            caps.StateVoltageRange = legacy.StateVoltageRange;
        }
        if (legacy.VramFreqSupported)
        {
            caps.VramSupported = true;
            caps.VramFreqSupported = true;
            caps.VramFreqRange = legacy.VramFreqRange;
        }
        if (legacy.PowerSupported)
        {
            caps.PowerSupported = true;
            caps.PowerRange = legacy.PowerRange;
            caps.PowerDefault = legacy.PowerDefault;
        }
        if (legacy.FanSupported)
        {
            caps.FanSupported = true;
            caps.FanMinRpmRange = legacy.FanMinRpmRange;
            caps.FanTargetRpmRange = legacy.FanTargetRpmRange;
        }
        return caps;
    }

    private void LoadFallback()
    {
        var adapters = DriverInfoService.ReadAmdAdapters();
        var best = adapters.OrderByDescending(a => a.VramBytes).FirstOrDefault();
        var gpu = new GpuInfo { FromAdlx = false, Name = best?.Name ?? UiHelper.L("Err.GpuNameFallback") };
        if (best != null)
        {
            gpu.DeviceId = best.MatchingId;
            gpu.DriverVersion = best.DriverVersion;
            gpu.VramMb = (int)(best.VramBytes / (1024 * 1024));
            gpu.PnpString = best.MatchingId;
        }

        var (arch, chip) = GpuArchitectureDetector.Detect(gpu.DeviceId, gpu.PnpString, gpu.Name);
        gpu.Architecture = arch;
        gpu.ChipName = chip;
        CurrentGpu = gpu;
        _gpus.Add(gpu);
        Caps = new GpuCapabilities { AdlxAvailable = false };
    }

    public MetricsSnapshot ReadMetrics()
    {
        lock (_tuningLock)
        {
            var metrics = _session?.ReadMetrics();
            if (metrics is { Valid: true }) return metrics;
            return _adl?.ReadMetrics() ?? metrics ?? new MetricsSnapshot { Error = UiHelper.L("Err.AdlxUnavailable") };
        }
    }

    private static ApplyReport Unavailable(string feature)
    {
        var r = new ApplyReport();
        r.Add(feature, AdlxResult.Unavailable);
        return r;
    }

    public GraphicsSettings ReadGraphics()
    {
        if (_session == null) return new GraphicsSettings();
        var s = _session.ReadGraphics(out var caps);
        PatchEnhancedSyncCaps(caps, s);
        PatchUmdCaps(caps, s);
        Caps.Graphics = caps;
        return s;
    }

    private void PatchEnhancedSyncCaps(GraphicsCapabilities caps, GraphicsSettings cur)
    {
        if (caps.EnhancedSync) return;
        var (sup, en, msg) = Adl.DriverFsrService.GetEnhancedSync(CurrentGpu.Index);
        LogService.Info($"Enhanced Sync via ADL: suporte={sup}, ligado={en} | {msg}");
        if (!sup) return;
        caps.EnhancedSync = true;
        cur.EnhancedSync = en;
    }

    public ApplyReport ApplyGraphics(GraphicsSettings s)
    {
        if (_session == null) return Unavailable("ADLX");

        var report = new ApplyReport();
        var forSession = s.Clone();

        forSession.Frtc = null;
        forSession.FrtcFps = null;

        var inner = _session.ApplyGraphics(forSession);
        foreach (var item in inner.Items) report.Add(item.Feature, item.Result);

        if (s.EnhancedSync != null) ReconcileEnhancedSync(s.EnhancedSync.Value, report);
        ApplyUmdFlags(s, report);

        try
        {
            var a = _session.ReadGraphics(out _);
            LogService.Info($"Estado 3D após aplicar: AntiLag={a.AntiLag} AFMF={a.Afmf} FSR={a.Fsr} Chill={a.Chill} " +
                            $"FRTC={a.Frtc}/{a.FrtcFps} RIS={a.ImageSharpening}/{a.ImageSharpeningLevel} RSR={a.Rsr} " +
                            $"EnhSync={a.EnhancedSync} Boost={a.Boost} VSync={a.VSyncMode}");
        }
        catch (Exception ex)
        {
            LogService.Error($"Leitura após aplicar: {ex.Message}");
        }

        return report;
    }

    private const string KeyTripleBuffer = "EnableTripleBuffering", KeySurfaceFormat = "SurfaceFormatReplacements";

    private void PatchUmdCaps(GraphicsCapabilities caps, GraphicsSettings cur)
    {
        var tb = Adl.DriverFsrService.GetUmdFlag(KeyTripleBuffer, CurrentGpu.Index);
        caps.TripleBuffer = tb.Available;
        if (tb.Available) cur.TripleBuffer = tb.Enabled;
        var sf = Adl.DriverFsrService.GetUmdFlag(KeySurfaceFormat, CurrentGpu.Index);
        caps.SurfaceFormatOpt = sf.Available;
        if (sf.Available) cur.SurfaceFormatOpt = sf.Enabled;
    }

    private void ApplyUmdFlags(GraphicsSettings s, ApplyReport report)
    {
        void One(string name, string key, bool? want)
        {
            if (want == null) return;
            var r = Adl.DriverFsrService.SetUmdFlag(key, want.Value, CurrentGpu.Index);
            LogService.Info($"{name} pelo driver: {(r.Ok ? "ok" : "falhou")} - {r.Message}");
            report.Add(name, r.Ok ? AdlxResult.Ok : AdlxResult.Fail);
        }

        One(UiHelper.L("Report.TripleBuffer"), KeyTripleBuffer, s.TripleBuffer);
        One(UiHelper.L("Report.SurfaceFormat"), KeySurfaceFormat, s.SurfaceFormatOpt);
    }

    private void ReconcileEnhancedSync(bool want, ApplyReport report)
    {
        const string name = "Enhanced Sync";
        bool adlxOk = false;
        try
        {
            adlxOk = _session!.ReadGraphics(out _).EnhancedSync == want && report.Items.Any(i => i.Feature == name && i.Result.Succeeded());
        }
        catch
        {
        }

        if (adlxOk) return;

        var r = Adl.DriverFsrService.SetEnhancedSync(want, CurrentGpu.Index);
        LogService.Info($"Enhanced Sync pelo driver (ADL): {(r.Ok ? "ok" : "falhou")} - {r.Message}");
        for (int i = report.Items.Count - 1; i >= 0; i--)
        {
            if (report.Items[i].Feature == name) report.Items.RemoveAt(i);
        }

        report.Add(name, r.Ok ? AdlxResult.Ok : AdlxResult.Fail);
    }

    public AdlxResult ResetDriverShaderCache() => _session?.ResetShaderCache() ?? AdlxResult.Unavailable;

    public AdlxResult SetSam(bool enabled)
    {
        if (_session == null) return AdlxResult.Unavailable;
        var r = _session.SetSam(enabled);
        if (r.Succeeded())
        {
            var (sup, en) = _session.ReadSam();
            Caps.SamSupported = sup;
            Caps.SamEnabled = en;
        }

        return r;
    }

    public TuningSettings ReadTuning()
    {
        lock (_tuningLock)
        {
            var settings = _session?.ReadTuning(_adlxTuning) ?? new TuningSettings();
            _overdrive?.ReadOverdriveTuning(settings);
            TuningBaseline ??= settings.Clone();
            return settings;
        }
    }

    public ApplyReport ApplyTuning(TuningSettings settings)
    {
        lock (_tuningLock)
        {
            if (!Caps.Tuning.AnyTuning) return Unavailable("Tuning");
            var report = _session?.ApplyTuning(settings, _adlxTuning) ?? new ApplyReport();
            _overdrive?.ApplyOverdriveTuning(settings, report);
            return report;
        }
    }

    public AdlxResult ResetTuningToFactory()
    {
        lock (_tuningLock)
        {
            if (!Caps.Tuning.AnyTuning) return AdlxResult.Unavailable;
            var adlxResult = _adlxTuning.AnyTuning ? _session!.ResetTuningToFactory() : AdlxResult.Ok;
            var legacyResult = _overdrive?.ResetOverdriveTuning() ?? AdlxResult.Ok;
            if (!adlxResult.Succeeded()) return adlxResult;
            if (!legacyResult.Succeeded()) return legacyResult;
            TuningBaseline = null;
            _adlxTuning = _session?.QueryTuningCapabilities(CurrentGpu.Architecture == GpuArchitecture.Polaris) ?? new();
            Caps.Tuning = MergeTuningCapabilities();
            return AdlxResult.Ok;
        }
    }

    private List<DisplayInfo> _displayCache = new();

    public List<DisplayInfo> ReadDisplays(bool log = true)
    {
        var list = _session?.ReadDisplays() ?? new List<DisplayInfo>();
        MergeAdlFreeSync(list);
        _displayCache = list;
        if (log) LogService.Info($"Monitores pelo ADLX: {list.Count}" + string.Concat(list.Select(d =>
            $" | [{d.Index}] {d.Name}: FreeSync={d.Caps.FreeSync} VSR={d.Caps.Vsr} Escala={d.Caps.GpuScaling}")));

        var adl = Adl.DriverFsrService.ListDisplays(CurrentGpu.Index);
        if (log) LogService.Info($"Monitores pelo ADL: {adl.Count}" + string.Concat(adl.Select(d =>
            $" | {d.Logical} '{d.Name}': escala suporte={d.ScalingSupported} ligada={d.ScalingOn}")));

        if (list.Count == 0)
        {
            int idx = 0;
            foreach (var d in adl)
            {
                var info = new DisplayInfo { Index = idx++, Name = d.Name };
                info.Caps.GpuScaling = d.ScalingSupported;
                if (d.ScalingSupported) info.Current.GpuScaling = d.ScalingOn;
                list.Add(info);
            }

            return list;
        }

        if (adl.Any(d => d.ScalingSupported))
        {
            bool on = adl.Where(d => d.ScalingSupported).All(d => d.ScalingOn);
            foreach (var d in list.Where(d => !d.Caps.GpuScaling))
            {
                d.Caps.GpuScaling = true;
                d.Current.GpuScaling ??= on;
            }
        }

        return list;
    }

    private void MergeAdlFreeSync(List<DisplayInfo> list)
    {
        if (list.Count == 0) return;
        var fs = Adl.DriverFsrService.ReadFreeSync(CurrentGpu.Index);
        if (fs.Count == 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            int j = fs.Count == list.Count ? i : (list.Count == 1 ? 0 : -1);
            if (j < 0) continue;
            var c = list[i].Caps;
            if (fs[j].MaxHz > 0)
            {
                c.FreeSyncMinHz = fs[j].MinHz;
                c.FreeSyncMaxHz = fs[j].MaxHz;
            }

            if (!c.FreeSync && fs[j].Supported)
            {
                c.FreeSync = true;
                c.FreeSyncViaAdl = true;
                list[i].Current.FreeSync = fs[j].Enabled;
            }
        }
    }

    public ApplyReport ApplyDisplay(int index, DisplaySettings s)
    {
        bool viaAdl = s.FreeSync != null && index >= 0 && index < _displayCache.Count && _displayCache[index].Caps.FreeSyncViaAdl;
        bool? fsWanted = s.FreeSync;
        if (viaAdl) s.FreeSync = null;
        try
        {
            var report = ApplyDisplayCore(index, s);
            if (viaAdl)
            {
                var r = Adl.DriverFsrService.SetFreeSync(index, fsWanted!.Value, CurrentGpu.Index);
                LogService.Info($"ADL FreeSync set: {r.Message}");
                report.Add(UiHelper.L("Report.AdaptiveSync"), r.Ok ? AdlxResult.Ok : AdlxResult.Fail);
            }

            return report;
        }
        finally
        {
            s.FreeSync = fsWanted;
        }
    }

    private ApplyReport ApplyDisplayCore(int index, DisplaySettings s)
    {
        (bool Ok, string Message)? adl = null;
        if (s.GpuScaling == true) adl = RunAdlScaling(true);

        var report = _session?.ApplyDisplay(index, s) ?? Unavailable("Display");

        if (s.GpuScaling == false) adl = RunAdlScaling(false);

        if (adl is { Ok: true })
        {
            report.Items.RemoveAll(i => i.Feature == "Display" && !i.Result.Succeeded());
            NudgeDisplayModes();

            for (int i = 0; i < report.Items.Count; i++)
            {
                if (report.Items[i].Feature == UiHelper.L("Report.GpuScaling") && !report.Items[i].Result.Succeeded())
                    report.Items[i] = (UiHelper.L("Report.GpuScaling"), AdlxResult.Ok);
            }

            if (!report.Items.Any(i => i.Feature == UiHelper.L("Report.GpuScaling"))) report.Add(UiHelper.L("Report.GpuScaling"), AdlxResult.Ok);
        }
        else if (adl is { Ok: false } && !report.Items.Any(i => i.Feature == UiHelper.L("Report.GpuScaling"))) report.Add(UiHelper.L("Report.GpuScaling"), AdlxResult.Fail);

        return report;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsExW(string? device, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr param);

    private static void NudgeDisplayModes()
    {
        try
        {
            ChangeDisplaySettingsExW(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            LogService.Error($"Atualizar modos: {ex.Message}");
        }
    }

    private (bool Ok, string Message) RunAdlScaling(bool enable)
    {
        var r = Adl.DriverFsrService.SetGpuScaling(enable, CurrentGpu.Index);
        LogService.Info($"Escala da GPU via ADL ({(enable ? "ligar" : "desligar")}): {(r.Ok ? "ok" : "falhou")} - {r.Message}");
        return r;
    }

    public string BuildDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== Red Control Utility {UpdateService.CurrentText} - "+UiHelper.L("Diag.Title")+" ===");
        sb.AppendLine($"{UiHelper.L("Diag.Date")}: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{UiHelper.L("Diag.Os")}: {Environment.OSVersion} | {UiHelper.L("Diag.Process64")}: {Environment.Is64BitProcess}");
        sb.AppendLine($"Backend ADLX: {BackendName} (v{AdlxVersion})");
        sb.AppendLine();
        sb.AppendLine("--- "+UiHelper.L("Diag.Init")+" ---");
        sb.AppendLine(InitReport);
        sb.AppendLine();
        sb.AppendLine("--- GPU ---");
        foreach (var g in _gpus)
        {
            sb.AppendLine($"{(g.Index == CurrentGpu.Index ? "*" : " ")} [{g.Index}] {g.Name} | VEN {g.VendorId} DEV {g.DeviceId} REV {g.RevisionId} | {g.ArchitectureText} | VRAM {g.VramMb} MB {g.VramType} | driver {g.DriverVersion} | BIOS {g.BiosVersion}");
        }

        sb.AppendLine();
        sb.AppendLine("--- "+UiHelper.L("Diag.DriverInstall")+" ---");
        string sys = Environment.SystemDirectory;
        sb.AppendLine($"amdadlx64.dll: {File.Exists(Path.Combine(sys, "amdadlx64.dll"))} | atiadlxx.dll: {File.Exists(Path.Combine(sys, "atiadlxx.dll"))}");
        bool sw = System.Diagnostics.Process.GetProcessesByName("RadeonSoftware").Length > 0;
        sb.AppendLine(UiHelper.L("Diag.Adrenalin", sw));
        sb.AppendLine(UiHelper.L("Diag.LegacySensors", _adl != null));

        var c = Caps;
        sb.AppendLine();
        sb.AppendLine("--- "+UiHelper.L("Diag.Support")+" ---");
        sb.AppendLine($"{UiHelper.L("Diag.Sensors")}: {c.MetricsAvailable} | SAM/ReBAR: sup={c.SamSupported} on={c.SamEnabled}");
        var gr = c.Graphics;
        sb.AppendLine($"3D: AntiLag={gr.AntiLag} AFMF={gr.Afmf} FSR={gr.Fsr} Chill={gr.Chill} FRTC={gr.Frtc} RIS={gr.ImageSharpening} RSR={gr.Rsr} EnhSync={gr.EnhancedSync} Boost={gr.Boost} VSync={gr.VSync} AF={gr.AnisotropicFiltering} Tess={gr.Tessellation}");
        if (_session != null)
        {
            sb.AppendLine("--- "+UiHelper.L("Diag.Upscalers")+" ---");
            sb.AppendLine(_session.ProbeUpscalers());
        }

        var t = c.Tuning;
        sb.AppendLine($"AMD Display Library Overdrive N: {_overdrive != null}");
        sb.AppendLine($"Tuning: GFX={t.GfxMode} Power={t.PowerSupported} Fan={t.FanSupported}({t.FanPointCount} pts, zeroRPM={t.ZeroRpmSupported}) VRAM={t.VramSupported}(freq={t.VramFreqSupported}, timing={t.VramTimingSupported})");
        sb.AppendLine($"  {UiHelper.L("Diag.Ranges")}: minFreq={t.MinFreqRange} maxFreq={t.MaxFreqRange} volt={t.VoltageRange} power={t.PowerRange} vram={t.VramFreqRange}");
        return sb.ToString();
    }

    public void Dispose()
    {
        lock (_tuningLock)
        {
            CloseOverdrive();
            _session?.Dispose();
            _session = null;
            _adl?.Dispose();
            _adl = null;
            _adlxTuning = new();
            Caps = new();
        }
    }
}
