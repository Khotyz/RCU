namespace RedControlUtility.Models;

/// <summary>Leitura instantânea dos sensores (null = sensor não suportado/indisponível).</summary>
public sealed class MetricsSnapshot
{
    public bool Valid { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double? GpuUsage { get; set; }
    public int? GpuClockMhz { get; set; }
    public int? VramClockMhz { get; set; }
    public double? TempEdgeC { get; set; }
    public double? TempHotspotC { get; set; }
    public double? TempMemoryC { get; set; }
    public double? PowerW { get; set; }
    public double? BoardPowerW { get; set; }
    public int? FanRpm { get; set; }
    public int? FanDutyPercent { get; set; }
    public int? VramUsedMb { get; set; }
    public int? VoltageMv { get; set; }
    public string? Error { get; set; }
}
