using System.Runtime.InteropServices;
using RedControlUtility.Models;

namespace RedControlUtility.Services;

public sealed class DisplayPersistenceService : IDisposable
{
    private const uint PbtResumeSuspend = 0x7;
    private const uint PbtResumeAutomatic = 0x12;
    private const int DriftIntervalMs = 60000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SubscribeParameters
    {
        public IntPtr Callback;
        public IntPtr Context;
    }

    private delegate uint PowerCallback(IntPtr context, uint type, IntPtr setting);

    [DllImport("powrprof.dll")]
    private static extern uint PowerRegisterSuspendResumeNotification(uint flags, ref SubscribeParameters recipient, out IntPtr handle);

    [DllImport("powrprof.dll")]
    private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr handle);

    private readonly object _lock = new();
    private Timer? _timer;
    private Timer? _resumeTimer;
    private PowerCallback? _callback;
    private IntPtr _powerHandle;

    private static string Key(DisplayInfo d) => d.UniqueId != 0 ? d.UniqueId.ToString() : d.Name;

    public void Start()
    {
        Stop();
        _timer = new Timer(_ => Reapply(true), null, 6000, DriftIntervalMs);
        try
        {
            _callback = OnPower;
            var p = new SubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(_callback), Context = IntPtr.Zero };
            if (PowerRegisterSuspendResumeNotification(2, ref p, out _powerHandle) != 0) _powerHandle = IntPtr.Zero;
        }
        catch (Exception ex)
        {
            LogService.Warn($"Resume notification: {ex.Message}");
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _resumeTimer?.Dispose();
        _resumeTimer = null;
        if (_powerHandle != IntPtr.Zero)
        {
            try { PowerUnregisterSuspendResumeNotification(_powerHandle); } catch { }
            _powerHandle = IntPtr.Zero;
        }
    }

    private uint OnPower(IntPtr context, uint type, IntPtr setting)
    {
        if (type == PbtResumeAutomatic || type == PbtResumeSuspend)
        {
            _resumeTimer?.Dispose();
            _resumeTimer = new Timer(_ => AfterResume(), null, 5000, Timeout.Infinite);
        }

        return 0;
    }

    private void AfterResume()
    {
        LogService.Info("Resume detected: reapplying display and color settings.");
        Reapply(true);
        try
        {
            if (!App.RecoveredFromCrash)
            {
                var r = App.ApplyProfile(App.CurrentProfile);
                LogService.Info($"Resume profile: {r.Summary}");
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Resume profile: {ex.Message}");
        }
    }

    public void Remember(DisplayInfo d, DisplaySettings diff)
    {
        lock (_lock)
        {
            var map = App.CurrentProfile.Displays;
            if (!map.TryGetValue(Key(d), out var saved)) map[Key(d)] = saved = new DisplaySettings();
            saved.FreeSync = diff.FreeSync ?? saved.FreeSync;
            saved.FreeSyncColorAccuracy = diff.FreeSyncColorAccuracy ?? saved.FreeSyncColorAccuracy;
            saved.Vsr = diff.Vsr ?? saved.Vsr;
            saved.GpuScaling = diff.GpuScaling ?? saved.GpuScaling;
            saved.IntegerScaling = diff.IntegerScaling ?? saved.IntegerScaling;
            saved.ScalingMode = diff.ScalingMode ?? saved.ScalingMode;
            saved.Hue = diff.Hue ?? saved.Hue;
            saved.Saturation = diff.Saturation ?? saved.Saturation;
            saved.Brightness = diff.Brightness ?? saved.Brightness;
            saved.Contrast = diff.Contrast ?? saved.Contrast;
            saved.Temperature = diff.Temperature ?? saved.Temperature;
        }

        App.SaveCurrentProfile();
    }

    public void Reapply(bool includeColor = false)
    {
        if (!Monitor.TryEnter(_lock)) return;
        try
        {
            if (App.CurrentProfile.Displays.Count > 0 && App.Hardware.AdlxAvailable)
            {
                foreach (var d in App.Hardware.ReadDisplays(false, true))
                {
                    if (!App.CurrentProfile.Displays.TryGetValue(Key(d), out var saved)) continue;
                    var diff = Drift(d, saved);
                    if (diff == null) continue;
                    var r = App.Hardware.ApplyDisplay(d.Index, diff);
                    LogService.Info($"Display '{d.Name}' drifted from the saved settings, reapplied: {r.Summary}");
                }
            }

            if (includeColor) ApplyColor(App.CurrentProfile);
        }
        catch (Exception ex)
        {
            LogService.Error($"Display reapply: {ex.Message}");
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    public static void ApplyColor(UtilityProfile p)
    {
        if (p.ColorProfile == "Custom") App.DisplayColor.ApplyCustomColor(p.CustomGamma, p.CustomRed, p.CustomGreen, p.CustomBlue, 0.0, true);
        else if (p.ColorProfile != "Default") App.DisplayColor.ApplyColorProfile(p.ColorProfile, true);
    }

    private static DisplaySettings? Drift(DisplayInfo d, DisplaySettings saved)
    {
        var c = d.Caps;
        var cur = d.Current;
        var diff = new DisplaySettings();
        bool any = false;

        bool? B(bool supported, bool? want, bool? have)
        {
            if (!supported || want == null || have == null || want == have) return null;
            any = true;
            return want;
        }

        int? I(bool supported, int? want, int? have)
        {
            if (!supported || want == null || have == null || want == have) return null;
            any = true;
            return want;
        }

        diff.FreeSync = B(c.FreeSync, saved.FreeSync, cur.FreeSync);
        diff.FreeSyncColorAccuracy = B(c.FreeSyncColorAccuracy, saved.FreeSyncColorAccuracy, cur.FreeSyncColorAccuracy);
        diff.Vsr = B(c.Vsr, saved.Vsr, cur.Vsr);
        diff.GpuScaling = B(c.GpuScaling, saved.GpuScaling, cur.GpuScaling);
        diff.IntegerScaling = B(c.IntegerScaling, saved.IntegerScaling, cur.IntegerScaling);
        diff.ScalingMode = I(c.ScalingMode, saved.ScalingMode, cur.ScalingMode);
        diff.Hue = I(c.Hue, saved.Hue, cur.Hue);
        diff.Saturation = I(c.Saturation, saved.Saturation, cur.Saturation);
        diff.Brightness = I(c.Brightness, saved.Brightness, cur.Brightness);
        diff.Contrast = I(c.Contrast, saved.Contrast, cur.Contrast);
        diff.Temperature = I(c.Temperature, saved.Temperature, cur.Temperature);
        return any ? diff : null;
    }

    public void Dispose() => Stop();
}
