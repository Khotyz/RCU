using RedControlUtility.Models;

namespace RedControlUtility.Services.Adlx;

public sealed partial class AdlxSession
{
    private static class S3D
    {
        public const int AntiLag = 3, Chill = 4, Boost = 5, ImageSharpening = 6, EnhancedSync = 7, WaitForVerticalRefresh = 8,
            Frtc = 9, AntiAliasing = 10, Mlaa = 11, AnisotropicFiltering = 12, Tessellation = 13, Rsr = 14,
            ResetShaderCache = 15,
            Afmf = 17,
            ImageSharpenDesktop = 18,
            FidelityFxSuperResolution = 19,
            FidelityFxFrameGen = 20;

        public const int SetAntiLag = 5, SetAfmf = 5, SetFsr = 5, SetEnhancedSync = 5, SetRsr = 5;
        public const int SetChill = 8, SetMinFps = 9, SetMaxFps = 10;
        public const int SetFrtc = 7, SetFps = 8;
        public const int SetSharpening = 7, SetSharpness = 8;
        public const int SetBoost = 7, SetBoostResolution = 8;
        public const int SetAnisotropic = 6, SetAnisotropicLevel = 7;
        public const int SetTessellationMode = 6, SetTessellationLevel = 7;
        public const int SetAaMode = 7, SetAaLevel = 8, SetAaMethod = 9, SetMlaa = 5;
        public const int SetVSyncMode = 6;
    }

    private const string NAntiLag = "Radeon Anti-Lag";
    private const string NAfmf = "AMD Fluid Motion Frames";
    private const string NFrtc = "Frame Rate Target Control";
    private const string NChill = "Radeon Chill";
    private const string NRis = "Radeon Image Sharpening";
    private const string NRsr = "Radeon Super Resolution";
    private const string NBoost = "Radeon Boost";
    private const string NEnhSync = "Enhanced Sync";
    private const string NFsr = "FidelityFX Super Resolution";

    private static readonly Dictionary<string, string[]> Conflicts = new()
    {
        [NFrtc] = new[] { NChill, NAfmf },
        [NChill] = new[] { NFrtc },
        [NRis] = new[] { NAfmf, NRsr },
        [NRsr] = new[] { NAfmf, NBoost, NRis },
        [NBoost] = new[] { NAfmf, NRsr },
        [NAfmf] = new[] { NChill, NFrtc, NRis, NRsr, NBoost },
    };

    private AdlxObject? Feature(AdlxObject? services, int slot, bool withGpu)
    {
        if (services == null || _gpu == null) return null;
        AdlxResult r = withGpu ? services.GetPtrWith(slot, _gpu.Ptr, out IntPtr p) : services.GetPtr(slot, out p);
        return r.Succeeded() && p != IntPtr.Zero ? new AdlxObject(p) : null;
    }

    private AdlxObject? Feature3D(string name) => name switch
    {
        NAntiLag => Feature(_3d, S3D.AntiLag, true),
        NAfmf => Feature(_3d1, S3D.Afmf, false),
        NFsr => Feature(_3d3, S3D.FidelityFxSuperResolution, true),
        NEnhSync => Feature(_3d, S3D.EnhancedSync, true),
        NChill => Feature(_3d, S3D.Chill, true),
        NFrtc => Feature(_3d, S3D.Frtc, true),
        NRis => Feature(_3d, S3D.ImageSharpening, true),
        NRsr => Feature(_3d, S3D.Rsr, false),
        NBoost => Feature(_3d, S3D.Boost, true),
        _ => null
    };

    private static int SetEnabledSlot(string name) => name switch
    {
        NChill => S3D.SetChill,
        NFrtc => S3D.SetFrtc,
        NRis => S3D.SetSharpening,
        NBoost => S3D.SetBoost,
        NRsr => S3D.SetRsr,
        NAfmf => S3D.SetAfmf,
        NFsr => S3D.SetFsr,
        _ => S3D.SetEnhancedSync
    };

    private static bool Usable(AdlxObject f) => f.IsValid;

    public GraphicsSettings ReadGraphics(out GraphicsCapabilities caps)
    {
        caps = new GraphicsCapabilities();
        var s = new GraphicsSettings();
        lock (_sync)
        {
            try
            {
                foreach (var name in new[] { NAntiLag, NAfmf, NFsr, NEnhSync, NChill, NFrtc, NRis, NRsr, NBoost })
                {
                    using var f = Feature3D(name);
                    bool usable = f != null && Usable(f);
                    bool enabled = usable && f!.GetBool(4, out bool e).Succeeded() && e;
                    SetCap(caps, name, usable);
                    SetValue(s, name, enabled);

                    if (!usable || f == null) continue;
                    switch (name)
                    {
                        case NChill:
                            if (f.GetRange(5, out var cr).Succeeded() && cr.IsValid) caps.ChillRange = cr;
                            if (f.GetInt(6, out int cmin).Succeeded()) s.ChillMinFps = cmin;
                            if (f.GetInt(7, out int cmax).Succeeded()) s.ChillMaxFps = cmax;
                            break;
                        case NFrtc:
                            if (f.GetRange(5, out var fr).Succeeded() && fr.IsValid) caps.FrtcRange = fr;
                            if (f.GetInt(6, out int fps).Succeeded() && fps > 0) s.FrtcFps = fps;
                            break;
                        case NRis:
                            if (f.GetRange(5, out var sr).Succeeded() && sr.IsValid) caps.ImageSharpeningRange = sr;
                            if (f.GetInt(6, out int sharp).Succeeded()) s.ImageSharpeningLevel = sharp;
                            break;
                        case NRsr:
                            if (f.GetRange(6, out var rr).Succeeded() && rr.IsValid) caps.RsrRange = rr;
                            if (f.GetInt(7, out int rsh).Succeeded()) s.RsrSharpness = rsh;
                            break;
                        case NBoost:
                            if (f.GetRange(5, out var br).Succeeded() && br.IsValid) caps.BoostRange = br;
                            if (f.GetInt(6, out int bres).Succeeded()) s.BoostResolution = bres;
                            break;
                    }
                }

                using (var f = Feature(_3d, S3D.WaitForVerticalRefresh, true))
                {
                    caps.VSync = f != null && Usable(f);
                    if (caps.VSync && f!.GetInt(5, out int mode).Succeeded()) s.VSyncMode = mode;
                }

                using (var f = Feature(_3d, S3D.AnisotropicFiltering, true))
                {
                    caps.AnisotropicFiltering = f != null && Usable(f);
                    if (caps.AnisotropicFiltering && f != null)
                    {
                        if (f.GetBool(4, out bool e).Succeeded()) s.AnisotropicFiltering = e;
                        if (f.GetInt(5, out int lvl).Succeeded()) s.AnisotropicLevel = lvl;
                    }
                }

                using (var f = Feature(_3d, S3D.Tessellation, true))
                {
                    caps.Tessellation = f != null && Usable(f);
                    if (caps.Tessellation && f != null)
                    {
                        if (f.GetInt(4, out int mode).Succeeded()) s.TessellationMode = mode;
                        if (f.GetInt(5, out int lvl).Succeeded()) s.TessellationLevel = lvl;
                    }
                }

                using (var f = Feature(_3d, S3D.AntiAliasing, true))
                {
                    caps.AntiAliasing = f != null && Usable(f);
                    if (caps.AntiAliasing && f != null)
                    {
                        if (f.GetInt(4, out int am).Succeeded()) s.AaMode = am;
                        if (f.GetInt(5, out int al).Succeeded()) s.AaLevel = al;
                        if (f.GetInt(6, out int amt).Succeeded()) s.AaMethod = amt;
                    }
                }

                using (var f = Feature(_3d, S3D.Mlaa, true))
                {
                    caps.Mlaa = f != null && Usable(f);
                    if (caps.Mlaa && f != null && f.GetBool(4, out bool ml).Succeeded()) s.Mlaa = ml;
                }

                using (var f = Feature(_3d, S3D.ResetShaderCache, true))
                {
                    caps.ResetShaderCache = f != null && f.Supported();
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"ReadGraphics: {ex.Message}");
            }
        }

        return s;
    }

    private static void SetCap(GraphicsCapabilities c, string name, bool value)
    {
        switch (name)
        {
            case NAntiLag: c.AntiLag = value; break;
            case NAfmf: c.Afmf = value; break;
            case NFsr: c.Fsr = value; break;
            case NEnhSync: c.EnhancedSync = value; break;
            case NChill: c.Chill = value; break;
            case NFrtc: c.Frtc = value; break;
            case NRis: c.ImageSharpening = value; break;
            case NRsr: c.Rsr = value; break;
            case NBoost: c.Boost = value; break;
        }
    }

    private static void SetValue(GraphicsSettings s, string name, bool value)
    {
        switch (name)
        {
            case NAntiLag: s.AntiLag = value; break;
            case NAfmf: s.Afmf = value; break;
            case NFsr: s.Fsr = value; break;
            case NEnhSync: s.EnhancedSync = value; break;
            case NChill: s.Chill = value; break;
            case NFrtc: s.Frtc = value; break;
            case NRis: s.ImageSharpening = value; break;
            case NRsr: s.Rsr = value; break;
            case NBoost: s.Boost = value; break;
        }
    }

    public GraphicsSettings ReadGraphics() => ReadGraphics(out _);

    public bool HasFrtc()
    {
        lock (_sync)
        {
            using var f = Feature3D(NFrtc);
            return f != null;
        }
    }

    private (bool Exists, bool Enabled) ReadState(string name)
    {
        using var f = Feature3D(name);
        if (f == null) return (false, false);
        bool on = f.GetBool(4, out bool e).Succeeded() && e;
        return (true, on);
    }

    private sealed record Op(string Name, bool Enable, Func<AdlxResult> Run, Func<AdlxResult>? Verify = null);

    private static AdlxResult Seq(params Func<AdlxResult>[] steps)
    {
        foreach (var step in steps)
        {
            var r = step();
            if (!r.Succeeded()) return r;
        }

        return AdlxResult.Ok;
    }

    private AdlxResult SetToggle(string name, bool on, bool force)
    {
        using var f = Feature3D(name);
        if (f == null) return AdlxResult.NotSupported;

        bool supported = f.Supported();
        if (!supported && !force)
        {
            LogService.Info($"{name}: IsSupported=false");
            return AdlxResult.NotSupported;
        }

        if (!supported) LogService.Info($"{name}: IsSupported=false, tentando mesmo assim");

        var r = f.SetBool(SetEnabledSlot(name), on);
        if (!r.Succeeded()) return r;

        if (f.GetBool(4, out bool now).Succeeded())
        {
            LogService.Info($"{name}: pedido {on}, driver respondeu {now}");
            if (now != on) return AdlxResult.Fail;
        }

        return r;
    }

    private AdlxResult VerifyState(string name, bool want)
    {
        var (exists, now) = ReadState(name);
        if (!exists) return AdlxResult.NotSupported;
        return now == want ? AdlxResult.Ok : AdlxResult.Fail;
    }

    private bool TurnOff(string name)
    {
        using var f = Feature3D(name);
        if (f == null) return false;
        if (!f.GetBool(4, out bool on).Succeeded() || !on) return true;

        f.SetBool(SetEnabledSlot(name), false);
        bool off = f.GetBool(4, out bool now).Succeeded() && !now;
        LogService.Info($"Conflito: desligando {name} ({(off ? "ok" : "o driver não aceitou")})");
        return off;
    }

    private bool ResolveConflicts(string name)
    {
        if (!Conflicts.TryGetValue(name, out var others)) return false;
        bool any = false;
        foreach (var other in others)
        {
            if (ReadState(other) is { Exists: true, Enabled: true })
            {
                any |= TurnOff(other);
            }
        }

        return any;
    }

    public ApplyReport ApplyGraphics(GraphicsSettings s) => ApplyGraphics(s, true);

    public ApplyReport ApplyGraphics(GraphicsSettings s, bool includeFrtc)
    {
        var report = new ApplyReport();
        lock (_sync)
        {
            try
            {
                var req = s.Clone();
                ResolveExplicitConflicts(req, report);
                if (!includeFrtc)
                {
                    req.Frtc = null;
                    req.FrtcFps = null;
                }

                foreach (var op in BuildOps(req))
                {
                    var r = RunWithRetry(op);
                    if (op.Name.Length == 0) continue;
                    report.Add(op.Name, r);
                    LogService.Info($"3D: {op.Name} => {r}");
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"ApplyGraphics: {ex.Message}");
                report.Add(UiHelper.L("Report.Graphics"), AdlxResult.Fail);
            }
        }

        return report;
    }

    private AdlxResult RunWithRetry(Op op)
    {
        AdlxResult r;
        try
        {
            r = op.Run();
        }
        catch (Exception ex)
        {
            LogService.Error($"ApplyGraphics[{op.Name}]: {ex.Message}");
            return AdlxResult.Fail;
        }

        if (!op.Enable || r == AdlxResult.NotSupported) return r;

        bool confirmed = r.Succeeded() && (op.Verify == null || op.Verify().Succeeded());
        if (confirmed) return r;

        if (op.Name == NEnhSync && EnsureFreeSync())
        {
            LogService.Info($"{NEnhSync}: FreeSync ligado (é pré-requisito), tentando de novo");
        }
        else if (!ResolveConflicts(op.Name))
        {
            LogService.Info($"{op.Name}: o driver aceitou mas não manteve o estado");
            return AdlxResult.Fail;
        }

        try
        {
            r = op.Run();
        }
        catch (Exception ex)
        {
            LogService.Error($"ApplyGraphics[{op.Name}] (2ª tentativa): {ex.Message}");
            return AdlxResult.Fail;
        }

        if (!r.Succeeded()) return r;
        bool ok = op.Verify == null || op.Verify().Succeeded();
        if (!ok) LogService.Info($"{op.Name}: continua sem efeito depois de remover o conflito");
        return ok ? r : AdlxResult.Fail;
    }

    private static void ResolveExplicitConflicts(GraphicsSettings req, ApplyReport report)
    {
        if (req.Afmf == true)
        {
            if (req.Chill == true || req.Frtc == true || req.ImageSharpening == true || req.Rsr == true || req.Boost == true)
            {
                LogService.Info("AFMF não convive com Chill/FRTC/Image Sharpening/RSR/Boost: mantendo só o AFMF");
                if (req.Chill == true) report.Add($"{NChill} ({UiHelper.L("Report.AfmfConflict")})", AdlxResult.NotSupported);
                if (req.Frtc == true) report.Add($"{NFrtc} ({UiHelper.L("Report.AfmfConflict")})", AdlxResult.NotSupported);
                if (req.ImageSharpening == true) report.Add($"{NRis} ({UiHelper.L("Report.AfmfConflict")})", AdlxResult.NotSupported);
                if (req.Rsr == true) report.Add($"{NRsr} ({UiHelper.L("Report.AfmfConflict")})", AdlxResult.NotSupported);
                if (req.Boost == true) report.Add($"{NBoost} ({UiHelper.L("Report.AfmfConflict")})", AdlxResult.NotSupported);
            }

            req.Chill = req.Chill == true ? false : req.Chill;
            req.ChillMinFps = null;
            req.ChillMaxFps = null;
            req.Frtc = req.Frtc == true ? false : req.Frtc;
            req.FrtcFps = null;
            req.ImageSharpening = req.ImageSharpening == true ? false : req.ImageSharpening;
            req.ImageSharpeningLevel = null;
            req.Rsr = req.Rsr == true ? false : req.Rsr;
            req.RsrSharpness = null;
            req.Boost = req.Boost == true ? false : req.Boost;
            req.BoostResolution = null;
        }

        if (req.Chill == true && req.Frtc == true)
        {
            LogService.Info("Chill e FRTC são exclusivos: mantendo o Chill");
            req.Frtc = false;
            req.FrtcFps = null;
        }
    }

    private List<Op> BuildOps(GraphicsSettings s)
    {
        var offs = new List<Op>();
        var sets = new List<Op>();
        var ons = new List<Op>();
        var post = new List<Op>();

        void Toggle(string name, bool? value, bool force)
        {
            if (value == null) return;
            bool on = value.Value;
            var op = new Op(name, on, () => SetToggle(name, on, force), () => VerifyState(name, on));
            (on ? ons : offs).Add(op);
        }

        Toggle(NAntiLag, s.AntiLag, false);
        Toggle(NEnhSync, s.EnhancedSync, true);
        Toggle(NChill, s.Chill, false);
        Toggle(NRis, s.ImageSharpening, false);
        Toggle(NRsr, s.Rsr, false);
        Toggle(NBoost, s.Boost, false);
        Toggle(NAfmf, s.Afmf, true);
        Toggle(NFsr, s.Fsr, true);

        if (s.Chill == true && (s.ChillMinFps != null || s.ChillMaxFps != null))
        {
            sets.Add(new Op($"{NChill} (faixa de FPS)", true, () =>
            {
                using var f = Feature3D(NChill);
                if (f == null) return AdlxResult.NotSupported;
                if (!f.GetRange(5, out var rg).Succeeded()) rg = AdlxRange.Empty;
                int mn = s.ChillMinFps ?? (f.GetInt(6, out int cm).Succeeded() ? cm : 60);
                int mx = s.ChillMaxFps ?? (f.GetInt(7, out int cx).Succeeded() ? cx : 144);
                if (rg.IsValid)
                {
                    mn = rg.Clamp(mn);
                    mx = rg.Clamp(mx);
                }

                if (mx < mn) mx = mn;

                f.GetInt(7, out int curMax);
                return Seq(
                    () => mx >= curMax ? f.SetInt(S3D.SetMaxFps, mx) : AdlxResult.Ok,
                    () => f.SetInt(S3D.SetMinFps, mn),
                    () => mx < curMax ? f.SetInt(S3D.SetMaxFps, mx) : AdlxResult.Ok);
            }));
        }

        if (s.Frtc == false)
        {
            offs.Add(new Op(NFrtc, false, () => SetToggle(NFrtc, false, false), () => VerifyState(NFrtc, false)));
        }
        else if (s.Frtc == true)
        {
            int? wantFps = s.FrtcFps;
            int applied = 0;
            ons.Add(new Op(NFrtc, true, () =>
            {
                using var f = Feature3D(NFrtc);
                if (f == null) return AdlxResult.NotSupported;
                if (!f.Supported()) LogService.Info($"{NFrtc}: IsSupported=false, tentando mesmo assim");

                int fps = wantFps ?? 0;
                if (f.GetRange(5, out var rg).Succeeded() && rg.IsValid)
                {
                    if (fps <= 0) fps = f.GetInt(6, out int cur).Succeeded() ? cur : 60;
                    fps = rg.Clamp(fps);
                }

                if (fps <= 0) fps = 60;
                applied = fps;

                var r = f.SetBool(S3D.SetFrtc, true);
                if (!r.Succeeded()) return r;

                var setFps = f.SetInt(S3D.SetFps, fps);
                if (!setFps.Succeeded())
                {
                    setFps = f.SetInt(S3D.SetFps, fps);
                }

                bool on = f.GetBool(4, out bool e).Succeeded() && e;

                f.GetInt(6, out int now);
                LogService.Info($"FRTC: ligado={on}, limite={now} (pedido {fps})");
                return on ? AdlxResult.Ok : AdlxResult.Fail;
            }, () =>
            {
                using var f = Feature3D(NFrtc);
                if (f == null) return AdlxResult.NotSupported;
                bool on = f.GetBool(4, out bool e).Succeeded() && e;
                if (!on) return AdlxResult.Fail;
                if (wantFps == null || !f.GetInt(6, out int now).Succeeded()) return AdlxResult.Ok;
                return now == wantFps.Value || (applied > 0 && now == applied) ? AdlxResult.Ok : AdlxResult.Fail;
            }));
        }

        void ValueOp(string label, string feature, bool enabled, int? value, int rangeSlot, int setSlot)
        {
            if (!enabled || value == null) return;
            int want = value.Value;
            AdlxResult Run()
            {
                using var f = Feature3D(feature);
                if (f == null) return AdlxResult.NotSupported;
                int v = want;
                if (f.GetRange(rangeSlot, out var rg).Succeeded() && rg.IsValid) v = rg.Clamp(v);
                return f.SetInt(setSlot, v);
            }

            sets.Add(new Op(string.Empty, false, () => { Run(); return AdlxResult.Ok; }));
            post.Add(new Op(label, false, () =>
            {
                var r = Run();
                if (!r.Succeeded()) LogService.Info($"{label}: o driver recusou o valor {want} ({r})");
                return r;
            }));
        }

        ValueOp($"{NRis} ({UiHelper.L("Report.Sharpness")})", NRis, s.ImageSharpening == true, s.ImageSharpeningLevel, 5, S3D.SetSharpness);
        ValueOp($"{NRsr} ({UiHelper.L("Report.Sharpness")})", NRsr, s.Rsr == true, s.RsrSharpness, 6, S3D.SetSharpness);
        ValueOp($"{NBoost} ({UiHelper.L("Report.Resolution")})", NBoost, s.Boost == true, s.BoostResolution, 5, S3D.SetBoostResolution);

        if (s.VSyncMode != null)
        {
            int mode = s.VSyncMode.Value;
            ons.Add(new Op(UiHelper.L("Report.VSync"), true, () =>
            {
                using var f = Feature(_3d, S3D.WaitForVerticalRefresh, true);
                if (f == null) return AdlxResult.NotSupported;
                if (!f.Supported()) LogService.Info("VSync: IsSupported=false, tentando mesmo assim");
                var r = f.SetInt(S3D.SetVSyncMode, mode);
                if (r.Succeeded() && f.GetInt(5, out int now).Succeeded())
                {
                    LogService.Info($"VSync: pedido {mode}, driver respondeu {now}");
                    if (now != mode) r = AdlxResult.Fail;
                }

                return r;
            }, () =>
            {
                using var f = Feature(_3d, S3D.WaitForVerticalRefresh, true);
                return f != null && f.GetInt(5, out int now).Succeeded() && now == mode ? AdlxResult.Ok : AdlxResult.Fail;
            }));
        }

        if (s.AnisotropicFiltering != null || s.AnisotropicLevel != null)
        {
            bool on = s.AnisotropicFiltering ?? true;
            var op = new Op(UiHelper.L("Report.Af"), on, () =>
            {
                using var f = Feature(_3d, S3D.AnisotropicFiltering, true);
                if (f == null || !f.Supported()) return AdlxResult.NotSupported;
                if (!on) return f.SetBool(S3D.SetAnisotropic, false);
                int lvl = s.AnisotropicLevel ?? (f.GetInt(5, out int cur).Succeeded() ? cur : 16);
                if (lvl < 2) lvl = 16;
                return Seq(() => f.SetInt(S3D.SetAnisotropicLevel, lvl), () => f.SetBool(S3D.SetAnisotropic, true));
            });
            (on ? ons : offs).Add(op);
        }

        if (s.TessellationMode != null || s.TessellationLevel != null)
        {
            ons.Add(new Op("Tessellation", true, () =>
            {
                using var f = Feature(_3d, S3D.Tessellation, true);
                if (f == null || !f.Supported()) return AdlxResult.NotSupported;
                int mode = s.TessellationMode ?? (f.GetInt(4, out int cm).Succeeded() ? cm : 0);
                if (mode == (int)AdlxTessellationMode.OverrideAppSettings && s.TessellationLevel != null)
                {
                    int lvl = Math.Clamp(s.TessellationLevel.Value, 1, 64);
                    return Seq(() => f.SetInt(S3D.SetTessellationMode, mode), () => f.SetInt(S3D.SetTessellationLevel, lvl));
                }

                return f.SetInt(S3D.SetTessellationMode, mode);
            }));
        }

        if (s.AaMode != null || s.AaMethod != null || s.AaLevel != null)
        {
            ons.Add(new Op("Anti-aliasing", true, () =>
            {
                using var f = Feature(_3d, S3D.AntiAliasing, true);
                if (f == null || !f.Supported()) return AdlxResult.NotSupported;
                int mode = s.AaMode ?? (f.GetInt(4, out int cm).Succeeded() ? cm : 0);
                var steps = new List<Func<AdlxResult>> { () => f.SetInt(S3D.SetAaMode, mode) };
                if (mode != 0 && s.AaMethod != null) steps.Add(() => f.SetInt(S3D.SetAaMethod, s.AaMethod.Value));
                if (mode != 0 && s.AaLevel is >= 2) steps.Add(() => f.SetInt(S3D.SetAaLevel, s.AaLevel.Value));
                return Seq(steps.ToArray());
            }));
        }

        if (s.Mlaa != null)
        {
            bool want = s.Mlaa.Value;
            ons.Add(new Op(UiHelper.L("Report.Mlaa"), true, () =>
            {
                using var f = Feature(_3d, S3D.Mlaa, true);
                if (f == null || !f.Supported()) return AdlxResult.NotSupported;
                return f.SetBool(S3D.SetMlaa, want);
            }));
        }

        var all = new List<Op>(offs.Count + sets.Count + ons.Count + post.Count);
        all.AddRange(offs);
        all.AddRange(sets);
        all.AddRange(ons);
        all.AddRange(post);
        return all;
    }

    private bool EnsureFreeSync()
    {
        bool any = false;
        foreach (var d in DisplayHandles())
        {
            try
            {
                using var f = DisplayFeature(d, DispSvc.FreeSync);
                if (f == null || !f.Supported()) continue;
                if (f.GetBool(4, out bool on).Succeeded() && on)
                {
                    any = true;
                    continue;
                }

                if (f.SetBool(5, true).Succeeded() && f.GetBool(4, out bool now).Succeeded() && now) any = true;
            }
            finally
            {
                d.Dispose();
            }
        }

        return any;
    }

    public string ProbeUpscalers()
    {
        var sb = new System.Text.StringBuilder();
        lock (_sync)
        {
            sb.AppendLine($"ADLX {AdlxVersion} | 3D={_3d != null} S1={_3d1 != null} S2={_3d2 != null} S3={_3d3 != null}");
            Probe(sb, "AFMF (S1 slot 17)", _3d1, S3D.Afmf, false);
            Probe(sb, "FSR upgrade (S3 slot 19)", _3d3, S3D.FidelityFxSuperResolution, true);
            Probe(sb, "FSR frame gen (S3 slot 20)", _3d3, S3D.FidelityFxFrameGen, true);

            foreach (var name in new[] { "IADLX3DSettingsServices3", "IADLX3DSettingsServices4",
                                         "IADLX3DFidelityFXSuperResolution", "IADLX3DFidelityFXFrameGenUpgrade" })
            {
                using var a = _3d?.Query(name);
                using var b = _gpu?.Query(name);
                sb.AppendLine($"QueryInterface({name}): {UiHelper.L("Diag.Services")}={(a != null ? UiHelper.L("Common.Yes") : UiHelper.L("Common.No"))} gpu={(b != null ? UiHelper.L("Common.Yes") : UiHelper.L("Common.No"))}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private void Probe(System.Text.StringBuilder sb, string name, AdlxObject? services, int slot, bool withGpu)
    {
        if (services == null || _gpu == null)
        {
            sb.AppendLine(UiHelper.L("Diag.ServiceUnavailable", name));
            return;
        }

        AdlxResult r = withGpu ? services.GetPtrWith(slot, _gpu.Ptr, out IntPtr p) : services.GetPtr(slot, out p);
        if (!r.Succeeded() || p == IntPtr.Zero)
        {
            sb.AppendLine($"{name}: obter objeto => {r}");
            return;
        }

        using var f = new AdlxObject(p);
        var rs = f.GetBool(3, out bool sup);
        var re = f.GetBool(4, out bool en);
        sb.AppendLine($"{name}: objeto ok | IsSupported={sup} ({rs}) | IsEnabled={en} ({re})");
    }

    public AdlxResult ResetShaderCache()
    {
        lock (_sync)
        {
            using var f = Feature(_3d, S3D.ResetShaderCache, true);
            if (f == null || !f.Supported()) return AdlxResult.NotSupported;
            return f.Invoke(4);
        }
    }
}
