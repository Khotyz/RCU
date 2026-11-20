using RedControlUtility.Models;

namespace RedControlUtility.Services.Adlx;

public sealed partial class AdlxSession
{
    // IADLXGPUTuningServices (deriva de IADLXInterface)
    private static class TuneSlot
    {
        public const int IsAtFactory = 4, ResetToFactory = 5,
            IsSupportedAuto = 6, IsSupportedPreset = 7, IsSupportedGfx = 8, IsSupportedVram = 9, IsSupportedFan = 10, IsSupportedPower = 11,
            GetAuto = 12, GetPreset = 13, GetGfx = 14, GetVram = 15, GetFan = 16, GetPower = 17;
    }

    private static readonly string[] PresetNames = { "PowerSaver", "Quiet", "Balanced", "Turbo", "Rage" };

    private AdlxObject? TuningObject(int supportedSlot, int getSlot, string interfaceName)
    {
        if (_tuning == null || _gpu == null) return null;
        if (!_tuning.GetBoolWith(supportedSlot, _gpu.Ptr, out bool sup).Succeeded() || !sup) return null;
        if (!_tuning.GetPtrWith(getSlot, _gpu.Ptr, out IntPtr p).Succeeded() || p == IntPtr.Zero) return null;

        using var raw = new AdlxObject(p);
        return raw.Query(interfaceName);
    }

    // ------------------------------------------------------------------ Capacidades

    public TuningCapabilities QueryTuningCapabilities()
    {
        var caps = new TuningCapabilities();
        lock (_sync)
        {
            try
            {
                if (_tuning == null || _gpu == null) return caps;

                if (_tuning.GetBoolWith(TuneSlot.IsAtFactory, _gpu.Ptr, out bool factory).Succeeded())
                {
                    caps.AtFactoryKnown = true;
                    caps.IsAtFactory = factory;
                }

                // --- GFX: IADLXManualGraphicsTuning2 (min/máx/voltagem) ou 1 (lista de estados)
                using (var g2 = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning2"))
                {
                    if (g2 != null)
                    {
                        caps.GfxMode = GfxTuningMode.MinMaxVoltage;
                        if (g2.GetRange(3, out var minR).Succeeded()) caps.MinFreqRange = minR;
                        if (g2.GetRange(6, out var maxR).Succeeded()) caps.MaxFreqRange = maxR;
                        if (g2.GetRange(9, out var vR).Succeeded()) caps.VoltageRange = vR;

                        using var g21 = g2.Query("IADLXManualGraphicsTuning2_1");
                        if (g21 != null)
                        {
                            if (g21.GetInt(12, out int dmin).Succeeded()) caps.DefaultMinFreq = dmin;
                            if (g21.GetInt(13, out int dmax).Succeeded()) caps.DefaultMaxFreq = dmax;
                            if (g21.GetInt(14, out int dv).Succeeded()) caps.DefaultVoltage = dv;
                        }
                    }
                }

                if (caps.GfxMode == GfxTuningMode.None)
                {
                    using var g1 = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning1");
                    if (g1 != null)
                    {
                        caps.GfxMode = GfxTuningMode.StateList;
                        if (g1.GetRange2(3, out var fr, out var vr).Succeeded())
                        {
                            caps.StateFreqRange = fr;
                            caps.StateVoltageRange = vr;
                        }
                    }
                }

                // --- Potência
                using (var pw = TuningObject(TuneSlot.IsSupportedPower, TuneSlot.GetPower, "IADLXManualPowerTuning"))
                {
                    if (pw != null)
                    {
                        caps.PowerSupported = true;
                        if (pw.GetRange(3, out var pr).Succeeded()) caps.PowerRange = pr;
                        using var pw1 = pw.Query("IADLXManualPowerTuning1");
                        if (pw1 != null && pw1.GetInt(10, out int pd).Succeeded()) caps.PowerDefault = pd;
                    }
                }

                // --- Fans
                {
                    var fr = _tuning.GetBoolWith(TuneSlot.IsSupportedFan, _gpu.Ptr, out bool fsup);
                    LogService.Info($"ADLX fan tuning: IsSupportedManualFanTuning result={fr} supported={fsup}");
                }

                using (var fan = TuningObject(TuneSlot.IsSupportedFan, TuneSlot.GetFan, "IADLXManualFanTuning"))
                {
                    if (fan != null)
                    {
                        caps.FanSupported = true;
                        if (fan.GetRange2(3, out var sr, out var tr).Succeeded())
                        {
                            caps.FanSpeedRange = sr;
                            caps.FanTempRange = tr;
                        }

                        if (fan.GetPtr(4, out IntPtr lp).Succeeded() && lp != IntPtr.Zero)
                        {
                            using var list = new AdlxObject(lp);
                            caps.FanPointCount = (int)list.ListSize();
                        }

                        caps.ZeroRpmSupported = fan.GetBool(8, out bool z).Succeeded() && z;
                    }
                }

                // --- VRAM
                using (var v2 = TuningObject(TuneSlot.IsSupportedVram, TuneSlot.GetVram, "IADLXManualVRAMTuning2"))
                {
                    if (v2 != null)
                    {
                        caps.VramSupported = true;
                        if (v2.GetRange(7, out var vr).Succeeded() && vr.IsValid)
                        {
                            caps.VramFreqSupported = true;
                            caps.VramFreqRange = vr;
                        }

                        ReadTimings(v2, caps);
                    }
                }

                if (!caps.VramSupported)
                {
                    using var v1 = TuningObject(TuneSlot.IsSupportedVram, TuneSlot.GetVram, "IADLXManualVRAMTuning1");
                    if (v1 != null)
                    {
                        caps.VramSupported = true;
                        if (v1.GetRange2(7, out var fr, out _).Succeeded() && fr.IsValid)
                        {
                            caps.VramFreqSupported = true;
                            caps.VramFreqRange = fr;
                        }

                        ReadTimings(v1, caps);
                    }
                }

                // --- Presets
                using (var pr = TuningObject(TuneSlot.IsSupportedPreset, TuneSlot.GetPreset, "IADLXGPUPresetTuning"))
                {
                    if (pr != null)
                    {
                        for (int i = 0; i < PresetNames.Length; i++)
                        {
                            if (pr.GetBool(3 + i, out bool ok).Succeeded() && ok)
                            {
                                caps.Presets.Add(PresetNames[i]);
                            }
                        }

                        caps.PresetSupported = caps.Presets.Count > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"QueryTuningCapabilities: {ex.Message}");
            }
        }

        return caps;
    }

    private static void ReadTimings(AdlxObject vram, TuningCapabilities caps)
    {
        // IsSupportedMemoryTiming (3), GetSupportedMemoryTimingDescriptionList (4)
        if (!vram.GetBool(3, out bool sup).Succeeded() || !sup) return;
        caps.VramTimingSupported = true;
        if (vram.GetPtr(4, out IntPtr lp).Succeeded() && lp != IntPtr.Zero)
        {
            using var list = new AdlxObject(lp);
            foreach (var item in list.ListItems())
            {
                using (item)
                {
                    // IADLXMemoryTimingDescription::GetDescription (slot 3)
                    if (item.GetInt(3, out int d).Succeeded())
                    {
                        caps.VramTimings.Add((AdlxMemoryTiming)d);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ Leitura

    public TuningSettings ReadTuning(TuningCapabilities caps)
    {
        var s = new TuningSettings();
        lock (_sync)
        {
            try
            {
                if (caps.GfxMode == GfxTuningMode.MinMaxVoltage)
                {
                    using var g = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning2");
                    if (g != null)
                    {
                        if (g.GetInt(4, out int mn).Succeeded()) s.GpuMinMhz = mn;
                        if (g.GetInt(7, out int mx).Succeeded()) s.GpuMaxMhz = mx;
                        if (g.GetInt(10, out int v).Succeeded()) s.VoltageMv = v;
                    }
                }
                else if (caps.GfxMode == GfxTuningMode.StateList)
                {
                    using var g = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning1");
                    if (g != null && g.GetPtr(4, out IntPtr lp).Succeeded() && lp != IntPtr.Zero)
                    {
                        using var list = new AdlxObject(lp);
                        s.VfStates = new List<VfCurvePoint>();
                        int i = 0;
                        foreach (var st in list.ListItems())
                        {
                            using (st)
                            {
                                st.GetInt(3, out int f);   // GetFrequency
                                st.GetInt(5, out int volt); // GetVoltage
                                s.VfStates.Add(new VfCurvePoint { Index = i++, FrequencyMHz = f, VoltageMv = volt });
                            }
                        }
                    }
                }

                if (caps.PowerSupported)
                {
                    using var pw = TuningObject(TuneSlot.IsSupportedPower, TuneSlot.GetPower, "IADLXManualPowerTuning");
                    if (pw != null && pw.GetInt(4, out int pl).Succeeded()) s.PowerLimitPercent = pl;
                }

                if (caps.FanSupported)
                {
                    using var fan = TuningObject(TuneSlot.IsSupportedFan, TuneSlot.GetFan, "IADLXManualFanTuning");
                    if (fan != null)
                    {
                        if (fan.GetPtr(4, out IntPtr lp).Succeeded() && lp != IntPtr.Zero)
                        {
                            using var list = new AdlxObject(lp);
                            s.FanCurve = new List<FanCurvePoint>();
                            foreach (var st in list.ListItems())
                            {
                                using (st)
                                {
                                    st.GetInt(3, out int speed);  // GetFanSpeed
                                    st.GetInt(5, out int temp);   // GetTemperature
                                    s.FanCurve.Add(new FanCurvePoint { TemperatureC = temp, FanSpeedPercent = speed });
                                }
                            }
                        }

                        if (caps.ZeroRpmSupported && fan.GetBool(9, out bool z).Succeeded()) s.ZeroRpm = z;
                    }
                }

                if (caps.VramSupported)
                {
                    using var v2 = TuningObject(TuneSlot.IsSupportedVram, TuneSlot.GetVram, "IADLXManualVRAMTuning2");
                    if (v2 != null)
                    {
                        if (caps.VramFreqSupported && v2.GetInt(8, out int vf).Succeeded()) s.VramMaxMhz = vf;
                        if (caps.VramTimingSupported && v2.GetInt(5, out int vt).Succeeded()) s.VramTiming = (AdlxMemoryTiming)vt;
                    }
                }

                if (caps.PresetSupported)
                {
                    using var pr = TuningObject(TuneSlot.IsSupportedPreset, TuneSlot.GetPreset, "IADLXGPUPresetTuning");
                    if (pr != null)
                    {
                        for (int i = 0; i < PresetNames.Length; i++)
                        {
                            if (pr.GetBool(8 + i, out bool cur).Succeeded() && cur)
                            {
                                s.Preset = PresetNames[i];
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"ReadTuning: {ex.Message}");
            }
        }

        return s;
    }

    // ------------------------------------------------------------------ Aplicação

    public ApplyReport ApplyTuning(TuningSettings t, TuningCapabilities caps)
    {
        var report = new ApplyReport();
        lock (_sync)
        {
            try
            {
                // Preset primeiro: ele redefine os valores manuais.
                if (!string.IsNullOrEmpty(t.Preset))
                {
                    report.Add($"Preset {t.Preset}", ApplyPreset(t.Preset));
                }

                if (caps.GfxMode == GfxTuningMode.MinMaxVoltage &&
                    (t.GpuMinMhz != null || t.GpuMaxMhz != null || t.VoltageMv != null))
                {
                    ApplyGfxMinMax(t, caps, report);
                }
                else if (caps.GfxMode == GfxTuningMode.StateList && t.VfStates is { Count: > 0 })
                {
                    ApplyGfxStates(t, report);
                }

                if (t.VramMaxMhz != null || t.VramTiming != null)
                {
                    ApplyVram(t, caps, report);
                }

                if (t.PowerLimitPercent != null && caps.PowerSupported)
                {
                    using var pw = TuningObject(TuneSlot.IsSupportedPower, TuneSlot.GetPower, "IADLXManualPowerTuning");
                    int v = caps.PowerRange.IsValid ? caps.PowerRange.Clamp(t.PowerLimitPercent.Value) : t.PowerLimitPercent.Value;
                    report.Add("Limite de potência", pw == null ? AdlxResult.NotSupported : pw.SetInt(5, v));
                }

                if ((t.FanCurve is { Count: > 0 } || t.ZeroRpm != null) && caps.FanSupported)
                {
                    ApplyFan(t, report);
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"ApplyTuning: {ex.Message}");
                report.Add("Tuning", AdlxResult.Fail);
            }
        }

        foreach (var (feature, result) in report.Items)
        {
            LogService.Info($"Tuning: {feature} => {result}");
        }

        return report;
    }

    private AdlxResult ApplyPreset(string preset)
    {
        int idx = Array.IndexOf(PresetNames, preset);
        if (idx < 0) return AdlxResult.InvalidArgs;
        using var pr = TuningObject(TuneSlot.IsSupportedPreset, TuneSlot.GetPreset, "IADLXGPUPresetTuning");
        return pr == null ? AdlxResult.NotSupported : pr.Invoke(13 + idx);
    }

    private void ApplyGfxMinMax(TuningSettings t, TuningCapabilities caps, ApplyReport report)
    {
        using var g = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning2");
        if (g == null)
        {
            report.Add("Clock/Voltagem da GPU", AdlxResult.NotSupported);
            return;
        }

        g.GetInt(4, out int curMin);
        g.GetInt(7, out int curMax);

        int? newMin = t.GpuMinMhz != null ? (caps.MinFreqRange.IsValid ? caps.MinFreqRange.Clamp(t.GpuMinMhz.Value) : t.GpuMinMhz) : null;
        int? newMax = t.GpuMaxMhz != null ? (caps.MaxFreqRange.IsValid ? caps.MaxFreqRange.Clamp(t.GpuMaxMhz.Value) : t.GpuMaxMhz) : null;

        // Mantém min <= max durante as duas gravações.
        bool minFirst = newMax != null && newMax.Value < curMin;
        if (minFirst)
        {
            if (newMin != null) report.Add("GPU clock mínimo", g.SetInt(5, newMin.Value));
            if (newMax != null) report.Add("GPU clock máximo", g.SetInt(8, newMax.Value));
        }
        else
        {
            if (newMax != null) report.Add("GPU clock máximo", g.SetInt(8, newMax.Value));
            if (newMin != null) report.Add("GPU clock mínimo", g.SetInt(5, newMin.Value));
        }

        if (t.VoltageMv != null)
        {
            int v = caps.VoltageRange.IsValid ? caps.VoltageRange.Clamp(t.VoltageMv.Value) : t.VoltageMv.Value;
            report.Add("Voltagem da GPU", g.SetInt(11, v));
        }
    }

    private void ApplyGfxStates(TuningSettings t, ApplyReport report)
    {
        using var g = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning1");
        if (g == null || !g.GetPtr(4, out IntPtr lp).Succeeded() || lp == IntPtr.Zero)
        {
            report.Add("Estados V/F", AdlxResult.NotSupported);
            return;
        }

        using var list = new AdlxObject(lp);
        var items = list.ListItems();
        try
        {
            for (int i = 0; i < items.Count && i < t.VfStates!.Count; i++)
            {
                items[i].SetInt(4, t.VfStates[i].FrequencyMHz); // SetFrequency
                items[i].SetInt(6, t.VfStates[i].VoltageMv);    // SetVoltage
            }

            var valid = g.ValidateStates(6, list.Ptr, out int errIdx);
            if (!valid.Succeeded())
            {
                LogService.Error($"Estados V/F inválidos (índice {errIdx}).");
                report.Add($"Estados V/F (ponto {errIdx + 1} inválido)", valid);
                return;
            }

            report.Add("Estados V/F", g.SetPtr(7, list.Ptr));
        }
        finally
        {
            foreach (var i in items) i.Dispose();
        }
    }

    private void ApplyVram(TuningSettings t, TuningCapabilities caps, ApplyReport report)
    {
        using var v2 = TuningObject(TuneSlot.IsSupportedVram, TuneSlot.GetVram, "IADLXManualVRAMTuning2");
        if (v2 != null)
        {
            if (t.VramMaxMhz != null && caps.VramFreqSupported)
            {
                int f = caps.VramFreqRange.IsValid ? caps.VramFreqRange.Clamp(t.VramMaxMhz.Value) : t.VramMaxMhz.Value;
                report.Add("Clock máximo da VRAM", v2.SetInt(9, f));
            }

            if (t.VramTiming != null && caps.VramTimingSupported)
            {
                report.Add("Timing da VRAM", v2.SetInt(6, (int)t.VramTiming.Value));
            }

            return;
        }

        using var v1 = TuningObject(TuneSlot.IsSupportedVram, TuneSlot.GetVram, "IADLXManualVRAMTuning1");
        if (v1 == null)
        {
            report.Add("VRAM", AdlxResult.NotSupported);
            return;
        }

        if (t.VramTiming != null && caps.VramTimingSupported)
        {
            report.Add("Timing da VRAM", v1.SetInt(6, (int)t.VramTiming.Value));
        }

        if (t.VramMaxMhz != null && caps.VramFreqSupported && v1.GetPtr(8, out IntPtr lp).Succeeded() && lp != IntPtr.Zero)
        {
            using var list = new AdlxObject(lp);
            var items = list.ListItems();
            try
            {
                if (items.Count > 0)
                {
                    int f = caps.VramFreqRange.IsValid ? caps.VramFreqRange.Clamp(t.VramMaxMhz.Value) : t.VramMaxMhz.Value;
                    items[^1].SetInt(4, f); // último estado = máximo
                    var valid = v1.ValidateStates(10, list.Ptr, out _);
                    report.Add("Clock máximo da VRAM", valid.Succeeded() ? v1.SetPtr(11, list.Ptr) : valid);
                }
            }
            finally
            {
                foreach (var i in items) i.Dispose();
            }
        }
    }

    private void ApplyFan(TuningSettings t, ApplyReport report)
    {
        using var fan = TuningObject(TuneSlot.IsSupportedFan, TuneSlot.GetFan, "IADLXManualFanTuning");
        if (fan == null)
        {
            report.Add("Ventoinhas", AdlxResult.NotSupported);
            return;
        }

        // Zero RPM
        if (t.ZeroRpm != null && fan.GetBool(8, out bool zsup).Succeeded() && zsup)
        {
            report.Add("Zero RPM", fan.SetBool(10, t.ZeroRpm.Value));
        }

        if (t.FanCurve is not { Count: > 0 })
        {
            return;
        }

        if (!fan.GetPtr(4, out IntPtr lp).Succeeded() || lp == IntPtr.Zero)
        {
            report.Add("Curva de ventoinha", AdlxResult.NotSupported);
            return;
        }

        fan.GetRange2(3, out var speedRange, out var tempRange);

        using var list = new AdlxObject(lp);
        var items = list.ListItems();
        try
        {
            var pts = ResampleFanCurve(t.FanCurve, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                int spd = speedRange.IsValid ? speedRange.Clamp(pts[i].FanSpeedPercent) : pts[i].FanSpeedPercent;
                int tmp = tempRange.IsValid ? tempRange.Clamp(pts[i].TemperatureC) : pts[i].TemperatureC;
                items[i].SetInt(4, spd); // SetFanSpeed
                items[i].SetInt(6, tmp); // SetTemperature
            }

            var valid = fan.ValidateStates(6, list.Ptr, out int errIdx);
            if (!valid.Succeeded())
            {
                report.Add($"Curva de ventoinha (ponto {errIdx + 1} inválido)", valid);
                return;
            }

            report.Add("Curva de ventoinha", fan.SetPtr(7, list.Ptr));
        }
        finally
        {
            foreach (var i in items) i.Dispose();
        }
    }

    /// <summary>Reamostra a curva do usuário para o nº de pontos que o driver espera (temperaturas estritamente crescentes).</summary>
    public static List<FanCurvePoint> ResampleFanCurve(List<FanCurvePoint> curve, int count)
    {
        var src = curve.OrderBy(p => p.TemperatureC).ToList();
        var result = new List<FanCurvePoint>();
        if (count <= 0 || src.Count == 0) return result;

        if (src.Count == count)
        {
            result.AddRange(src.Select(p => p.Clone()));
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                double pos = count == 1 ? 0 : i * (src.Count - 1) / (double)(count - 1);
                int lo = (int)Math.Floor(pos);
                int hi = Math.Min(src.Count - 1, lo + 1);
                double f = pos - lo;
                result.Add(new FanCurvePoint
                {
                    TemperatureC = (int)Math.Round(src[lo].TemperatureC + (src[hi].TemperatureC - src[lo].TemperatureC) * f),
                    FanSpeedPercent = (int)Math.Round(src[lo].FanSpeedPercent + (src[hi].FanSpeedPercent - src[lo].FanSpeedPercent) * f)
                });
            }
        }

        // Garante temperaturas estritamente crescentes e velocidade não decrescente.
        for (int i = 1; i < result.Count; i++)
        {
            if (result[i].TemperatureC <= result[i - 1].TemperatureC) result[i].TemperatureC = result[i - 1].TemperatureC + 1;
            if (result[i].FanSpeedPercent < result[i - 1].FanSpeedPercent) result[i].FanSpeedPercent = result[i - 1].FanSpeedPercent;
        }

        return result;
    }

    public AdlxResult ResetTuningToFactory()
    {
        lock (_sync)
        {
            if (_tuning == null || _gpu == null) return AdlxResult.NotSupported;
            var r = _tuning.InvokeWith(TuneSlot.ResetToFactory, _gpu.Ptr);
            LogService.Info($"ResetToFactory => {r}");
            return r;
        }
    }
}
