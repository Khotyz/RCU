using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Models;

public sealed class TuningSettings
{
    public int? GpuMinMhz { get; set; }
    public int? GpuMaxMhz { get; set; }
    public int? VoltageMv { get; set; }
    public int? PowerLimitPercent { get; set; }
    public int? VramMaxMhz { get; set; }
    public AdlxMemoryTiming? VramTiming { get; set; }
    public bool? ZeroRpm { get; set; }
    public int? FanMinRpm { get; set; }
    public int? FanTargetRpm { get; set; }
    public List<FanCurvePoint>? FanCurve { get; set; }
    public List<VfCurvePoint>? VfStates { get; set; }

    public bool HasValues() => GpuMinMhz != null || GpuMaxMhz != null || VoltageMv != null || PowerLimitPercent != null ||
        VramMaxMhz != null || VramTiming != null || ZeroRpm != null || FanMinRpm != null || FanTargetRpm != null ||
        FanCurve is { Count: > 0 } || VfStates is { Count: > 0 };

    public TuningSettings Clone()
    {
        var c = (TuningSettings)MemberwiseClone();
        c.FanCurve = FanCurve?.Select(p => p.Clone()).ToList();
        c.VfStates = VfStates?.Select(p => p.Clone()).ToList();
        return c;
    }
}

public sealed class ApplyReport
{
    public List<(string Feature, AdlxResult Result)> Items { get; } = new();
    public bool AllOk => Items.Count > 0 && Items.All(i => i.Result.Succeeded());
    public bool AnyOk => Items.Any(i => i.Result.Succeeded());
    public bool Empty => Items.Count == 0;

    public void Add(string feature, AdlxResult result) => Items.Add((feature, result));

    public string Detail => string.Join("\n", Items.Select(i => $"{(i.Result.Succeeded() ? UiHelper.L("Common.Ok") : UiHelper.L("Common.Failed"))}  {i.Feature}: {i.Result.Describe()}"));

    public string Summary
    {
        get
        {
            if (Items.Count == 0) return UiHelper.L("Report.NothingToApply");
            var failed = Items.Where(i => !i.Result.Succeeded()).ToList();
            if (failed.Count == 0) return UiHelper.L("Report.AppliedCount", Items.Count);
            return string.Join("; ", failed.Select(f => $"{f.Feature}: {f.Result.Describe()}"));
        }
    }
}
