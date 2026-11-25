using RedControlUtility.Models;

namespace RedControlUtility.Services;

public sealed class TuningPersistenceService : IDisposable
{
    private const int IntervalMs = 45000;
    private const int MaxFailures = 6;
    private Timer? _timer;
    private int _busy;
    private DateTime _retryAfter = DateTime.MinValue;
    private int _failures;

    public void Start()
    {
        Stop();
        _timer = new Timer(_ => Check(), null, 15000, IntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();

    public static void Remember(TuningSettings applied)
    {
        var saved = App.CurrentProfile.Tuning;
        saved.GpuMinMhz = applied.GpuMinMhz ?? saved.GpuMinMhz;
        saved.GpuMaxMhz = applied.GpuMaxMhz ?? saved.GpuMaxMhz;
        saved.VoltageMv = applied.VoltageMv ?? saved.VoltageMv;
        saved.PowerLimitPercent = applied.PowerLimitPercent ?? saved.PowerLimitPercent;
        saved.VramMaxMhz = applied.VramMaxMhz ?? saved.VramMaxMhz;
        saved.VramTiming = applied.VramTiming ?? saved.VramTiming;
        saved.ZeroRpm = applied.ZeroRpm ?? saved.ZeroRpm;
        saved.FanMinRpm = applied.FanMinRpm ?? saved.FanMinRpm;
        saved.FanTargetRpm = applied.FanTargetRpm ?? saved.FanTargetRpm;
        if (applied.FanCurve is { Count: > 0 }) saved.FanCurve = applied.FanCurve.Select(p => p.Clone()).ToList();
        if (applied.VfStates is { Count: > 0 }) saved.VfStates = applied.VfStates.Select(p => p.Clone()).ToList();
        App.SaveCurrentProfile();
    }

    public static void Forget()
    {
        App.CurrentProfile.Tuning = new TuningSettings();
        App.SaveCurrentProfile();
    }

    private void Check()
    {
        if (App.RecoveredFromCrash || _failures >= MaxFailures || DateTime.UtcNow < _retryAfter) return;
        if (DateTime.UtcNow - UiHelper.LastTuningApplyUtc < TimeSpan.FromSeconds(5)) return;
        if (File.Exists(AppPaths.PendingFlag)) return;
        var saved = App.CurrentProfile.Tuning;
        if (!saved.HasValues() || !App.Hardware.Caps.Tuning.AnyTuning) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var current = App.Hardware.ReadTuning();
            var diff = Drift(saved, current, App.Engine.Active != null);
            if (diff == null)
            {
                _failures = 0;
                return;
            }

            var report = App.Hardware.ApplyTuning(diff);
            if (report.AnyOk && report.AllOk)
            {
                _failures = 0;
                LogService.Warn($"Tuning drifted from the saved values and was reapplied: {report.Summary}");
            }
            else
            {
                _retryAfter = DateTime.UtcNow.AddMinutes(10);
                _failures++;
                LogService.Warn(_failures >= MaxFailures
                    ? $"Tuning reapply failed {_failures} times, giving up: {report.Summary}"
                    : $"Tuning reapply failed, retrying in 10 minutes: {report.Summary}");
            }
        }
        catch (Exception ex)
        {
            _retryAfter = DateTime.UtcNow.AddMinutes(10);
            _failures++;
            LogService.Error($"Tuning check: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static bool Differs(int? want, int? have, int tolerance = 1) => want != null && have != null && Math.Abs(want.Value - have.Value) > tolerance;

    private static TuningSettings? Drift(TuningSettings s, TuningSettings c, bool ignorePower)
    {
        var d = new TuningSettings();
        bool any = false;

        if (Differs(s.GpuMinMhz, c.GpuMinMhz) || Differs(s.GpuMaxMhz, c.GpuMaxMhz) || Differs(s.VoltageMv, c.VoltageMv))
        {
            d.GpuMinMhz = s.GpuMinMhz;
            d.GpuMaxMhz = s.GpuMaxMhz;
            d.VoltageMv = s.VoltageMv;
            any = true;
        }

        if (!ignorePower && Differs(s.PowerLimitPercent, c.PowerLimitPercent, 0))
        {
            d.PowerLimitPercent = s.PowerLimitPercent;
            any = true;
        }

        if (Differs(s.VramMaxMhz, c.VramMaxMhz) || (s.VramTiming != null && c.VramTiming != null && s.VramTiming != c.VramTiming))
        {
            d.VramMaxMhz = s.VramMaxMhz;
            d.VramTiming = s.VramTiming;
            any = true;
        }

        if ((s.ZeroRpm != null && c.ZeroRpm != null && s.ZeroRpm != c.ZeroRpm) || Differs(s.FanMinRpm, c.FanMinRpm, 50) || Differs(s.FanTargetRpm, c.FanTargetRpm, 50) ||
            CurveDiffers(s.FanCurve, c.FanCurve))
        {
            d.ZeroRpm = s.ZeroRpm;
            d.FanMinRpm = s.FanMinRpm;
            d.FanTargetRpm = s.FanTargetRpm;
            d.FanCurve = s.FanCurve?.Select(p => p.Clone()).ToList();
            any = true;
        }

        if (StatesDiffer(s.VfStates, c.VfStates))
        {
            d.VfStates = s.VfStates?.Select(p => p.Clone()).ToList();
            any = true;
        }

        return any ? d : null;
    }

    private static bool CurveDiffers(List<FanCurvePoint>? want, List<FanCurvePoint>? have)
    {
        if (want is not { Count: > 0 } || have is not { Count: > 0 }) return false;
        if (want.Count != have.Count) return true;
        for (int i = 0; i < want.Count; i++)
        {
            if (Math.Abs(want[i].TemperatureC - have[i].TemperatureC) > 1 || Math.Abs(want[i].FanSpeedPercent - have[i].FanSpeedPercent) > 1) return true;
        }

        return false;
    }

    private static bool StatesDiffer(List<VfCurvePoint>? want, List<VfCurvePoint>? have)
    {
        if (want is not { Count: > 0 } || have is not { Count: > 0 }) return false;
        if (want.Count != have.Count) return true;
        for (int i = 0; i < want.Count; i++)
        {
            if (Math.Abs(want[i].FrequencyMHz - have[i].FrequencyMHz) > 1 || Math.Abs(want[i].VoltageMv - have[i].VoltageMv) > 1) return true;
        }

        return false;
    }
}
