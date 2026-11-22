using RedControlUtility.Models;

namespace RedControlUtility.Services;

public sealed class SensorService : IDisposable
{
    private readonly HardwareService _hardware;
    private CancellationTokenSource? _cts;

    public MetricsSnapshot Latest { get; private set; } = new();
    public event Action<MetricsSnapshot>? Updated;

    public SensorService(HardwareService hardware)
    {
        _hardware = hardware;
    }

    public bool Running => _cts != null;

    public void Start(int intervalMs)
    {
        Stop();
        intervalMs = Math.Clamp(intervalMs, 250, 10000);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
            try
            {
                bool first = true;
                int lastGfx = 0, lastMem = 0, gfxZeros = 0, memZeros = 0;
                do
                {
                    var snap = _hardware.ReadMetrics();
                    if (snap.Valid)
                    {
                        snap.GpuClockMhz = HoldZero(snap.GpuClockMhz, ref lastGfx, ref gfxZeros, "GPU");
                        snap.VramClockMhz = HoldZero(snap.VramClockMhz, ref lastMem, ref memZeros, "VRAM");
                    }

                    Latest = snap;
                    MetricsHistory.Add(snap);
                    if (first && snap.Valid)
                    {
                        first = false;
                        LogService.Info($"Sensors: usage={snap.GpuUsage?.ToString("0.#") ?? "n/a"} fanRpm={snap.FanRpm?.ToString() ?? "n/a"} fanDuty={snap.FanDutyPercent?.ToString() ?? "n/a"} power={snap.BoardPowerW ?? snap.PowerW} clock={snap.GpuClockMhz}");
                    }

                    Updated?.Invoke(snap);
                }
                while (await timer.WaitForNextTickAsync(token));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogService.Error($"SensorService: {ex.Message}");
            }
        }, token);
    }

    private const int MaxHeldZeros = 4;
    private static readonly HashSet<string> _loggedHold = new();

    private static int? HoldZero(int? value, ref int last, ref int zeros, string name)
    {
        if (value is > 0)
        {
            last = value.Value;
            zeros = 0;
            return value;
        }

        if (value == 0 && last > 0 && zeros < MaxHeldZeros)
        {
            if (!_loggedHold.Contains(name))
            {
                _loggedHold.Add(name);
                LogService.Info($"Sensors: {name} clock reported 0 after {last} MHz, keeping the last value for up to {MaxHeldZeros} samples (logged once)");
            }

            zeros++;
            return last;
        }

        if (value == 0) zeros++;
        return value;
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch
        {
        }

        _cts = null;
    }

    public void Dispose() => Stop();
}
