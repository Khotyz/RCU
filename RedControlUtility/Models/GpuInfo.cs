using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Models;

public enum GpuArchitecture
{
    Unknown = 0,
    Gcn = 1,
    Polaris = 2,
    Vega = 3,
    Rdna1 = 4,
    Rdna2 = 5,
    Rdna3 = 6,
    Rdna3_5 = 7,
    Rdna4 = 8
}

public static class GpuArchitectureExtensions
{
    private static readonly HashSet<GpuArchitecture> Fsr4Architectures = new()
    {
        GpuArchitecture.Unknown,
        GpuArchitecture.Rdna2,
        GpuArchitecture.Rdna3,
        GpuArchitecture.Rdna3_5,
        GpuArchitecture.Rdna4
    };

    public static bool SupportsFsr4(this GpuArchitecture a) => Fsr4Architectures.Contains(a);

    public static string DisplayName(this GpuArchitecture a) => a switch
    {
        GpuArchitecture.Gcn => "GCN",
        GpuArchitecture.Polaris => "Polaris (GCN 4)",
        GpuArchitecture.Vega => "Vega (GCN 5)",
        GpuArchitecture.Rdna1 => "RDNA 1",
        GpuArchitecture.Rdna2 => "RDNA 2",
        GpuArchitecture.Rdna3 => "RDNA 3",
        GpuArchitecture.Rdna3_5 => "RDNA 3.5",
        GpuArchitecture.Rdna4 => "RDNA 4",
        _ => "—"
    };
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "AMD Radeon GPU";
    public string VendorId { get; set; } = "1002";
    public string DeviceId { get; set; } = string.Empty;
    public string RevisionId { get; set; } = string.Empty;
    public string SubSystemId { get; set; } = string.Empty;
    public string SubSystemVendorId { get; set; } = string.Empty;
    public int UniqueId { get; set; }
    public int Index { get; set; }

    public GpuArchitecture Architecture { get; set; } = GpuArchitecture.Unknown;
    public string ChipName { get; set; } = string.Empty;

    public int VramMb { get; set; }
    public string VramType { get; set; } = string.Empty;
    public bool IsDiscrete { get; set; } = true;
    public string DriverVersion { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public string BiosPartNumber { get; set; } = string.Empty;
    public string PnpString { get; set; } = string.Empty;

    public bool FromAdlx { get; set; }

    public string ArchitectureText =>
        string.IsNullOrEmpty(ChipName) ? Architecture.DisplayName() : $"{Architecture.DisplayName()} · {ChipName}";
}

public enum GfxTuningMode
{
    None = 0,
    MinMaxVoltage = 1,
    StateList = 2
}

public sealed class TuningCapabilities
{
    public GfxTuningMode GfxMode { get; set; }
    public AdlxRange MinFreqRange { get; set; } = AdlxRange.Empty;
    public AdlxRange MaxFreqRange { get; set; } = AdlxRange.Empty;
    public AdlxRange VoltageRange { get; set; } = AdlxRange.Empty;
    public int? DefaultMinFreq { get; set; }
    public int? DefaultMaxFreq { get; set; }
    public int? DefaultVoltage { get; set; }
    public AdlxRange StateFreqRange { get; set; } = AdlxRange.Empty;
    public AdlxRange StateVoltageRange { get; set; } = AdlxRange.Empty;

    public bool PowerSupported { get; set; }
    public AdlxRange PowerRange { get; set; } = AdlxRange.Empty;
    public int? PowerDefault { get; set; }

    public bool FanSupported { get; set; }
    public AdlxRange FanSpeedRange { get; set; } = AdlxRange.Empty;
    public AdlxRange FanTempRange { get; set; } = AdlxRange.Empty;
    public int FanPointCount { get; set; }
    public bool ZeroRpmSupported { get; set; }

    public bool VramSupported { get; set; }
    public bool VramFreqSupported { get; set; }
    public AdlxRange VramFreqRange { get; set; } = AdlxRange.Empty;
    public bool VramTimingSupported { get; set; }
    public List<AdlxMemoryTiming> VramTimings { get; set; } = new();

    public bool PresetSupported { get; set; }
    public List<string> Presets { get; set; } = new();

    public bool AtFactoryKnown { get; set; }
    public bool IsAtFactory { get; set; }

    public bool AnyTuning => GfxMode != GfxTuningMode.None || PowerSupported || FanSupported || VramSupported || PresetSupported;
}

public sealed class GpuCapabilities
{
    public bool AdlxAvailable { get; set; }
    public bool SamSupported { get; set; }
    public bool SamEnabled { get; set; }
    public GraphicsCapabilities Graphics { get; set; } = new();
    public TuningCapabilities Tuning { get; set; } = new();
    public bool MetricsAvailable { get; set; }
}
