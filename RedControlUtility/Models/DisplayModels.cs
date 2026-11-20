using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Models;

public sealed class DisplayCapabilities
{
    public bool FreeSync { get; set; }
    public bool Vsr { get; set; }
    public bool GpuScaling { get; set; }
    public bool IntegerScaling { get; set; }
    public bool ScalingMode { get; set; }
    public bool Hue { get; set; }
    public AdlxRange HueRange { get; set; } = AdlxRange.Empty;
    public bool Saturation { get; set; }
    public AdlxRange SaturationRange { get; set; } = AdlxRange.Empty;
    public bool Brightness { get; set; }
    public AdlxRange BrightnessRange { get; set; } = AdlxRange.Empty;
    public bool Contrast { get; set; }
    public AdlxRange ContrastRange { get; set; } = AdlxRange.Empty;
    public bool Temperature { get; set; }
    public AdlxRange TemperatureRange { get; set; } = AdlxRange.Empty;

    public bool AnyColor => Hue || Saturation || Brightness || Contrast || Temperature;
    public bool Any => FreeSync || Vsr || GpuScaling || IntegerScaling || ScalingMode || AnyColor;
}

public sealed class DisplaySettings
{
    public bool? FreeSync { get; set; }
    public bool? Vsr { get; set; }
    public bool? GpuScaling { get; set; }
    public bool? IntegerScaling { get; set; }
    public int? ScalingMode { get; set; }
    public int? Hue { get; set; }
    public int? Saturation { get; set; }
    public int? Brightness { get; set; }
    public int? Contrast { get; set; }
    public int? Temperature { get; set; }
}

public sealed class DisplayInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public long UniqueId { get; set; }
    public int NativeWidth { get; set; }
    public int NativeHeight { get; set; }
    public double RefreshRate { get; set; }
    public DisplayCapabilities Caps { get; set; } = new();
    public DisplaySettings Current { get; set; } = new();

    public string Title =>
        $"{(string.IsNullOrWhiteSpace(Name) ? $"Display {Index + 1}" : Name)}" +
        (NativeWidth > 0 ? $"  ({NativeWidth}×{NativeHeight} @ {RefreshRate:0.#} Hz)" : string.Empty);
}
