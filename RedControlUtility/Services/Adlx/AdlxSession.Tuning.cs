using RedControlUtility.Models;

namespace RedControlUtility.Services.Adlx;

public sealed partial class AdlxSession
{
    private static class TuneSlot
    {
        public const int IsAtFactory = 4, ResetToFactory = 5,
            IsSupportedAuto = 6, IsSupportedPreset = 7, IsSupportedGfx = 8, IsSupportedVram = 9, IsSupportedFan = 10, IsSupportedPower = 11,
            GetAuto = 12, GetPreset = 13, GetGfx = 14, GetVram = 15, GetFan = 16, GetPower = 17;
    }


    private AdlxObject? TuningObject(int supportedSlot, int getSlot, string interfaceName)
    {
        if (_tuning == null || _gpu == null) return null;
        if (!_tuning.GetBoolWith(supportedSlot, _gpu.Ptr, out bool sup).Succeeded() || !sup) return null;
        if (!_tuning.GetPtrWith(getSlot, _gpu.Ptr, out IntPtr p).Succeeded() || p == IntPtr.Zero) return null;

        using var raw = new AdlxObject(p);
        return raw.Query(interfaceName);
    }

    public TuningCapabilities QueryTuningCapabilities(bool polaris = false)
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

                using (var g2 = polaris ? null : TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning2"))
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
                    if (g1 != null && g1.GetRange2(3, out var fr, out var vr).Succeeded() && fr.IsValid && vr.IsValid &&
                        ReadGfxStates(g1) is { Count: > 0 })
                    {
                        caps.GfxMode = GfxTuningMode.StateList;
                        caps.StateFreqRange = fr;
                        caps.StateVoltageRange = vr;
                    }
                }

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

                {
                    var fr = _tuning.GetBoolWith(TuneSlot.IsSupportedFan, _gpu.Ptr, out bool fsup);
                    LogService.Info($"ADLX fan tuning: IsSupportedManualFanTuning result={fr} supported={fsup}");
                }

                using (var fan = TuningObject(TuneSlot.IsSupportedFan, TuneSlot.GetFan, "IADLXManualFanTuning"))
                {
                    if (fan != null)
                    {
                        if (fan.GetRange2(3, out var sr, out var tr).Succeeded())
                        {
                            caps.FanSpeedRange = sr;
                            caps.FanTempRange = tr;
                        }

                        caps.FanPointCount = ReadFanCurve(fan)?.Count ?? 0;
                        if (polaris && (!caps.FanSpeedRange.IsValid || !caps.FanTempRange.IsValid)) caps.FanPointCount = 0;
                        caps.ZeroRpmSupported = fan.Supported(8);
                        if (polaris && !caps.FanCurveSupported)
                        {
                            caps.FanMinRpmRange = ReadFanSpeedRange(fan, 15);
                            caps.FanTargetRpmRange = ReadFanSpeedRange(fan, 19);
                        }
                        caps.FanSupported = caps.FanCurveSupported || caps.FanMinRpmRange.IsValid ||
                            caps.FanTargetRpmRange.IsValid || caps.ZeroRpmSupported;
                    }
                }

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
            }
            catch (Exception ex)
            {
                LogService.Error($"QueryTuningCapabilities: {ex.Message}");
            }
        }

        return caps;
    }

    private static AdlxRange ReadFanSpeedRange(AdlxObject fan, int slot) =>
        fan.Supported(slot) && fan.GetRange(slot + 1, out var range).Succeeded() && range.IsValid &&
        fan.GetInt(slot + 2, out int speed).Succeeded() && speed >= range.Min && speed <= range.Max
            ? range : AdlxRange.Empty;

    private static List<FanCurvePoint>? ReadFanCurve(AdlxObject fan)
    {
        if (!fan.GetPtr(4, out IntPtr ptr).Succeeded() || ptr == IntPtr.Zero) return null;
        using var list = new AdlxObject(ptr);
        uint count = list.ListSize(), begin = list.ListBegin();
        if (count is 0 or > 32) return null;
        var points = new List<FanCurvePoint>();
        for (uint i = 0; i < count; i++)
        {
            if (!list.ListAt(begin + i, out var state, 11).Succeeded() || state == null) return null;
            using (state)
            {
                if (!state.GetInt(3, out int speed).Succeeded() || !state.GetInt(5, out int temperature).Succeeded()) return null;
                points.Add(new FanCurvePoint { FanSpeedPercent = speed, TemperatureC = temperature });
            }
        }
        return points;
    }

    private static List<VfCurvePoint>? ReadGfxStates(AdlxObject gfx)
    {
        if (!gfx.GetPtr(4, out IntPtr ptr).Succeeded() || ptr == IntPtr.Zero) return null;
        using var list = new AdlxObject(ptr);
        uint count = list.ListSize(), begin = list.ListBegin();
        if (count is 0 or > 32) return null;
        var states = new List<VfCurvePoint>();
        for (uint i = 0; i < count; i++)
        {
            if (!list.ListAt(begin + i, out var state, 11).Succeeded() || state == null) return null;
            using (state)
            {
                if (!state.GetInt(3, out int frequency).Succeeded() || !state.GetInt(5, out int voltage).Succeeded()) return null;
                states.Add(new VfCurvePoint { Index = (int)i, FrequencyMHz = frequency, VoltageMv = voltage });
            }
        }
        return states;
    }

    private static void ReadTimings(AdlxObject vram, TuningCapabilities caps)
    {
        if (!vram.GetBool(3, out bool sup).Succeeded() || !sup) return;
        caps.VramTimingSupported = true;
        if (vram.GetPtr(4, out IntPtr lp).Succeeded() && lp != IntPtr.Zero)
        {
            using var list = new AdlxObject(lp);
            foreach (var item in list.ListItems())
            {
                using (item)
                {
                    if (item.GetInt(3, out int d).Succeeded())
                    {
                        caps.VramTimings.Add((AdlxMemoryTiming)d);
                    }
                }
            }
        }
    }

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
                    if (g != null) s.VfStates = ReadGfxStates(g);
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
                        if (caps.FanCurveSupported) s.FanCurve = ReadFanCurve(fan);
                        if (caps.FanMinRpmRange.IsValid && fan.GetInt(17, out int minRpm).Succeeded()) s.FanMinRpm = minRpm;
                        if (caps.FanTargetRpmRange.IsValid && fan.GetInt(21, out int targetRpm).Succeeded()) s.FanTargetRpm = targetRpm;
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
            }
            catch (Exception ex)
            {
                LogService.Error($"ReadTuning: {ex.Message}");
            }
        }

        return s;
    }

    public ApplyReport ApplyTuning(TuningSettings t, TuningCapabilities caps)
    {
        var report = new ApplyReport();
        lock (_sync)
        {
            try
            {
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
                    report.Add(UiHelper.L("Report.PowerLimit"), pw == null ? AdlxResult.NotSupported : pw.SetInt(5, v));
                }

                if (((caps.FanCurveSupported && t.FanCurve is { Count: > 0 }) || t.ZeroRpm != null ||
                    (caps.FanMinRpmRange.IsValid && t.FanMinRpm != null) ||
                    (caps.FanTargetRpmRange.IsValid && t.FanTargetRpm != null)) && caps.FanSupported)
                {
                    ApplyFan(t, caps, report);
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
            if (!result.Succeeded()) LogService.Info($"Tuning: {feature} => {result}");
        }

        return report;
    }

    private void ApplyGfxMinMax(TuningSettings t, TuningCapabilities caps, ApplyReport report)
    {
        using var g = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning2");
        if (g == null)
        {
            report.Add(UiHelper.L("Report.GpuClockVoltage"), AdlxResult.NotSupported);
            return;
        }

        g.GetInt(4, out int curMin);
        g.GetInt(7, out int curMax);

        int? newMin = t.GpuMinMhz != null ? (caps.MinFreqRange.IsValid ? caps.MinFreqRange.Clamp(t.GpuMinMhz.Value) : t.GpuMinMhz) : null;
        int? newMax = t.GpuMaxMhz != null ? (caps.MaxFreqRange.IsValid ? caps.MaxFreqRange.Clamp(t.GpuMaxMhz.Value) : t.GpuMaxMhz) : null;

        bool minFirst = newMax != null && newMax.Value < curMin;
        if (minFirst)
        {
            if (newMin != null) report.Add(UiHelper.L("Report.GpuClockMin"), g.SetInt(5, newMin.Value));
            if (newMax != null) report.Add(UiHelper.L("Report.GpuClockMax"), g.SetInt(8, newMax.Value));
        }
        else
        {
            if (newMax != null) report.Add(UiHelper.L("Report.GpuClockMax"), g.SetInt(8, newMax.Value));
            if (newMin != null) report.Add(UiHelper.L("Report.GpuClockMin"), g.SetInt(5, newMin.Value));
        }

        if (t.VoltageMv != null)
        {
            int v = caps.VoltageRange.IsValid ? caps.VoltageRange.Clamp(t.VoltageMv.Value) : t.VoltageMv.Value;
            report.Add(UiHelper.L("Report.GpuVoltage"), g.SetInt(11, v));
        }
    }

    private void ApplyGfxStates(TuningSettings t, ApplyReport report)
    {
        using var g = TuningObject(TuneSlot.IsSupportedGfx, TuneSlot.GetGfx, "IADLXManualGraphicsTuning1");
        if (g == null || !g.GetPtr(4, out IntPtr lp).Succeeded() || lp == IntPtr.Zero)
        {
            report.Add(UiHelper.L("Report.VfStates"), AdlxResult.NotSupported);
            return;
        }

        using var list = new AdlxObject(lp);
        uint count = list.ListSize(), begin = list.ListBegin();
        var states = t.VfStates!;
        string feature = UiHelper.L("Report.VfStates");
        if (count is 0 or > 32 || states.Count != count || states.Select(p => p.Index).Distinct().Count() != states.Count)
        {
            report.Add(feature, AdlxResult.InvalidArgs);
            return;
        }
        foreach (var point in states)
        {
            if (point.Index < 0 || point.Index >= count ||
                !list.ListAt(begin + (uint)point.Index, out var item, 11).Succeeded() || item == null)
            {
                report.Add(feature, AdlxResult.InvalidArgs);
                return;
            }
            using (item)
            {
                var result = item.SetInt(4, point.FrequencyMHz);
                if (result.Succeeded()) result = item.SetInt(6, point.VoltageMv);
                if (!result.Succeeded())
                {
                    report.Add(feature, result);
                    return;
                }
            }
        }

        var valid = g.ValidateStates(6, list.Ptr, out int errorIndex);
        if (!valid.Succeeded())
        {
            report.Add($"{feature} ({UiHelper.L("Report.InvalidPoint", errorIndex + 1)})", valid);
            return;
        }
        report.Add(feature, g.SetPtr(7, list.Ptr));
    }

    private void ApplyVram(TuningSettings t, TuningCapabilities caps, ApplyReport report)
    {
        using var v2 = TuningObject(TuneSlot.IsSupportedVram, TuneSlot.GetVram, "IADLXManualVRAMTuning2");
        if (v2 != null)
        {
            if (t.VramMaxMhz != null && caps.VramFreqSupported)
            {
                int f = caps.VramFreqRange.IsValid ? caps.VramFreqRange.Clamp(t.VramMaxMhz.Value) : t.VramMaxMhz.Value;
                report.Add(UiHelper.L("Report.VramClock"), v2.SetInt(9, f));
            }

            if (t.VramTiming != null && caps.VramTimingSupported)
            {
                report.Add(UiHelper.L("Report.VramTiming"), v2.SetInt(6, (int)t.VramTiming.Value));
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
            report.Add(UiHelper.L("Report.VramTiming"), v1.SetInt(6, (int)t.VramTiming.Value));
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
                    items[^1].SetInt(4, f);
                    var valid = v1.ValidateStates(10, list.Ptr, out _);
                    report.Add(UiHelper.L("Report.VramClock"), valid.Succeeded() ? v1.SetPtr(11, list.Ptr) : valid);
                }
            }
            finally
            {
                foreach (var i in items) i.Dispose();
            }
        }
    }

    private void ApplyFan(TuningSettings t, TuningCapabilities caps, ApplyReport report)
    {
        using var fan = TuningObject(TuneSlot.IsSupportedFan, TuneSlot.GetFan, "IADLXManualFanTuning");
        if (fan == null)
        {
            report.Add(UiHelper.L("Report.Fans"), AdlxResult.NotSupported);
            return;
        }

        if (t.ZeroRpm != null && fan.GetBool(8, out bool zsup).Succeeded() && zsup)
        {
            report.Add("Zero RPM", fan.SetBool(10, t.ZeroRpm.Value));
        }

        if (t.FanMinRpm is int min && caps.FanMinRpmRange.IsValid)
            report.Add(UiHelper.L("Tuning.FanMinRpm"), SetFanSpeed(fan, 17, min, caps.FanMinRpmRange));
        if (t.FanTargetRpm is int target && caps.FanTargetRpmRange.IsValid)
            report.Add(UiHelper.L("Tuning.FanTargetRpm"), SetFanSpeed(fan, 21, target, caps.FanTargetRpmRange));

        if (!caps.FanCurveSupported || t.FanCurve is not { Count: > 0 })
        {
            return;
        }

        if (!fan.GetPtr(4, out IntPtr lp).Succeeded() || lp == IntPtr.Zero)
        {
            report.Add(UiHelper.L("Report.FanCurve"), AdlxResult.NotSupported);
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
                items[i].SetInt(4, spd);
                items[i].SetInt(6, tmp);
            }

            var valid = fan.ValidateStates(6, list.Ptr, out int errIdx);
            if (!valid.Succeeded())
            {
                report.Add($"{UiHelper.L("Report.FanCurve")} ({UiHelper.L("Report.InvalidPoint", errIdx + 1)})", valid);
                return;
            }

            report.Add(UiHelper.L("Report.FanCurve"), fan.SetPtr(7, list.Ptr));
        }
        finally
        {
            foreach (var i in items) i.Dispose();
        }
    }

    private static AdlxResult SetFanSpeed(AdlxObject fan, int getSlot, int value, AdlxRange range)
    {
        if (value < range.Min || value > range.Max)
            return AdlxResult.InvalidArgs;
        var result = fan.GetInt(getSlot, out int current);
        return !result.Succeeded() || current == value ? result : fan.SetInt(getSlot + 1, value);
    }

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
