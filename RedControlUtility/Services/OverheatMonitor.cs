using RedControlUtility.Models;

namespace RedControlUtility.Services;

public sealed class OverheatMonitor : IDisposable
{
    private const int PeriodMs = 3000;
    private const int HysteresisC = 5;
    private const int RepeatSeconds = 60;

    private Timer? _timer;
    private bool _hot;
    private DateTime _lastWarn = DateTime.MinValue;
    private int _busy;

    public void Start()
    {
        _timer ??= new Timer(_ => Tick(), null, PeriodMs, PeriodMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var s = App.Settings.Current;
            if (!s.OverheatWarn)
            {
                if (_hot) Dismiss();
                _hot = false;
                return;
            }

            var m = App.Hardware.ReadMetrics();
            double? t = m.Valid ? (m.TempEdgeC ?? m.TempHotspotC) : null;
            if (t == null) return;

            int limit = Math.Clamp(s.OverheatTempC, 50, 120);
            if (t >= limit)
            {
                bool first = !_hot;
                _hot = true;
                int temp = (int)Math.Round(t.Value);
                if (s.OverheatAlwaysOn) Warn(temp, limit, first);
                else if (first || (DateTime.UtcNow - _lastWarn).TotalSeconds >= Math.Max(RepeatSeconds, s.OverheatSeconds + 10)) Warn(temp, limit, true);
            }
            else if (t <= limit - HysteresisC)
            {
                if (_hot && s.OverheatAlwaysOn) Dismiss();
                _hot = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"OverheatMonitor: {ex.Message}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void Dismiss()
    {
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            try { OverlayService.Shared.Close(); }
            catch (Exception ex) { LogService.Error($"Overlay de temperatura: {ex.Message}"); }
        });
    }

    private void Warn(int temp, int limit, bool log)
    {
        _lastWarn = DateTime.UtcNow;
        if (log) LogService.Warn($"GPU em {temp} C (limite {limit} C).");
        var s = App.Settings.Current;
        double seconds = s.OverheatAlwaysOn ? 15 : Math.Clamp(s.OverheatSeconds, 3, 60);
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var items = new List<OverlayItem> { new(UiHelper.L("Notify.OverheatTemp"), $"{temp} °C", false) };
                OverlayService.Shared.Show(UiHelper.L("Notify.Overheat"), items, seconds, App.Settings.Current.OverlayCorner);
            }
            catch (Exception ex)
            {
                LogService.Error($"Overlay de temperatura: {ex.Message}");
            }
        });
    }

    public void Dispose() => Stop();
}
