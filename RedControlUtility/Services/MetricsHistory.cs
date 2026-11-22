using RedControlUtility.Models;

namespace RedControlUtility.Services;

public static class MetricsHistory
{
    public const int Capacity = 120;
    public const int SeriesCount = 4;
    public const int Usage = 0;
    public const int Temperature = 1;
    public const int Power = 2;
    public const int Fan = 3;

    private static readonly float[][] Data = Enumerable.Range(0, SeriesCount).Select(_ => new float[Capacity]).ToArray();
    private static readonly object Gate = new();
    private static int _head;
    private static int _count;

    public static void Clear()
    {
        lock (Gate)
        {
            _head = 0;
            _count = 0;
        }
    }

    public static void Add(MetricsSnapshot s)
    {
        if (!s.Valid) return;
        lock (Gate)
        {
            Data[Usage][_head] = (float)(s.GpuUsage ?? 0);
            Data[Temperature][_head] = (float)(s.TempHotspotC ?? s.TempEdgeC ?? 0);
            Data[Power][_head] = (float)(s.BoardPowerW ?? s.PowerW ?? 0);
            Data[Fan][_head] = s.FanRpm ?? 0;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }
    }

    public static int Copy(int series, float[] destination)
    {
        lock (Gate)
        {
            int start = (_head - _count + Capacity) % Capacity;
            for (int i = 0; i < _count; i++) destination[i] = Data[series][(start + i) % Capacity];
            return _count;
        }
    }
}
