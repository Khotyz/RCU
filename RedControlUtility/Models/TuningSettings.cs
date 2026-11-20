using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Models;

/// <summary>Estado de tuning manual (null = não alterar).</summary>
public sealed class TuningSettings
{
    public int? GpuMinMhz { get; set; }
    public int? GpuMaxMhz { get; set; }
    public int? VoltageMv { get; set; }
    public int? PowerLimitPercent { get; set; }
    public int? VramMaxMhz { get; set; }
    public AdlxMemoryTiming? VramTiming { get; set; }
    public bool? ZeroRpm { get; set; }
    public List<FanCurvePoint>? FanCurve { get; set; }
    public List<VfCurvePoint>? VfStates { get; set; }
    public string? Preset { get; set; }

    public TuningSettings Clone()
    {
        var c = (TuningSettings)MemberwiseClone();
        c.FanCurve = FanCurve?.Select(p => p.Clone()).ToList();
        c.VfStates = VfStates?.Select(p => p.Clone()).ToList();
        return c;
    }
}

/// <summary>Resultado detalhado de uma aplicação de configurações.</summary>
public sealed class ApplyReport
{
    public List<(string Feature, AdlxResult Result)> Items { get; } = new();
    public bool AllOk => Items.Count > 0 && Items.All(i => i.Result.Succeeded());
    public bool AnyOk => Items.Any(i => i.Result.Succeeded());
    public bool Empty => Items.Count == 0;

    public void Add(string feature, AdlxResult result) => Items.Add((feature, result));

    /// <summary>Uma linha por ajuste, com o código devolvido pelo driver (útil para diagnosticar).</summary>
    public string Detail => string.Join("\n", Items.Select(i => $"{(i.Result.Succeeded() ? "OK" : "FALHOU")}  {i.Feature}: {i.Result.Describe()}"));

    public string Summary
    {
        get
        {
            if (Items.Count == 0) return "Nada para aplicar.";
            var failed = Items.Where(i => !i.Result.Succeeded()).ToList();
            if (failed.Count == 0) return $"{Items.Count} ajuste(s) aplicado(s).";
            return string.Join("; ", failed.Select(f => $"{f.Feature}: {f.Result.Describe()}"));
        }
    }
}
