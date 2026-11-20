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
                do
                {
                    var snap = _hardware.ReadMetrics();
                    Latest = snap;
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
