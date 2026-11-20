using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Models;

public enum ApplyMode
{
    Instant = 0,
    Manual = 1
}

public sealed class AppSettings
{
    public string Language { get; set; } = string.Empty;
    public ApplyMode GraphicsApplyMode { get; set; } = ApplyMode.Manual;
    public bool ConfirmTuning { get; set; } = true;
    public bool LiveApplyUndervolt { get; set; }
    public bool KeepMinClockBelowMax { get; set; }
    public bool AdvancedMode { get; set; }
    public bool AutoRevertTuning { get; set; } = true;

    public bool GlobalFrtc { get; set; }

    public bool OverheatWarn { get; set; }
    public int OverheatTempC { get; set; } = 80;
    public bool OverheatAlwaysOn { get; set; }
    public int OverheatSeconds { get; set; } = 10;
    public int GlobalFrtcFps { get; set; } = 60;

    public bool AutoResetOnInstability { get; set; } = true;
    public bool ApplyProfileOnStartup { get; set; }
    public bool PerAppProfilesEnabled { get; set; } = true;
    public bool RestoreOnAppExit { get; set; } = true;
    public bool NotifyOnProfile { get; set; } = true;

    private double _overlaySeconds = 3;

    public double OverlaySeconds { get => _overlaySeconds; set => _overlaySeconds = Math.Round(value); }

    public int OverlayCorner { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public int SensorIntervalMs { get; set; } = 1000;

    public bool CheckForUpdates { get; set; } = true;
    public AdlxBackendKind Backend { get; set; } = AdlxBackendKind.Auto;
    public int GpuIndex { get; set; }
    public string GamesViewMode { get; set; } = "List";
}
