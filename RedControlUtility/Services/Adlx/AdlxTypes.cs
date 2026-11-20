namespace RedControlUtility.Services.Adlx;

public enum AdlxResult
{
    Ok = 0,
    AlreadyEnabled,
    AlreadyInitialized,
    Fail,
    InvalidArgs,
    BadVer,
    UnknownInterface,
    Terminated,
    AdlInitError,
    NotFound,
    InvalidObject,
    OrphanObjects,
    NotSupported,
    PendingOperation,
    GpuInactive,
    GpuInUse,
    TimeoutOperation,
    NotActive,
    ResetNeeded,

    Unavailable = 1000
}

public static class AdlxResultExtensions
{
    public static bool Succeeded(this AdlxResult r) =>
        r == AdlxResult.Ok || r == AdlxResult.AlreadyEnabled || r == AdlxResult.AlreadyInitialized;

    public static string Describe(this AdlxResult r) => r switch
    {
        AdlxResult.Ok => "OK",
        AdlxResult.AlreadyEnabled => UiHelper.L("Result.AlreadyEnabled"),
        AdlxResult.AlreadyInitialized => UiHelper.L("Result.AlreadyInitialized"),
        AdlxResult.Fail => UiHelper.L("Result.Fail"),
        AdlxResult.InvalidArgs => UiHelper.L("Result.InvalidArgs"),
        AdlxResult.BadVer => UiHelper.L("Result.BadVer"),
        AdlxResult.UnknownInterface => UiHelper.L("Result.UnknownInterface"),
        AdlxResult.Terminated => UiHelper.L("Result.Terminated"),
        AdlxResult.AdlInitError => UiHelper.L("Result.AdlInitError"),
        AdlxResult.NotFound => UiHelper.L("Result.NotFound"),
        AdlxResult.InvalidObject => UiHelper.L("Result.InvalidObject"),
        AdlxResult.OrphanObjects => UiHelper.L("Result.OrphanObjects"),
        AdlxResult.NotSupported => UiHelper.L("Result.NotSupported"),
        AdlxResult.PendingOperation => UiHelper.L("Result.PendingOperation"),
        AdlxResult.GpuInactive => UiHelper.L("Result.GpuInactive"),
        AdlxResult.GpuInUse => UiHelper.L("Result.GpuInUse"),
        AdlxResult.TimeoutOperation => UiHelper.L("Result.TimeoutOperation"),
        AdlxResult.NotActive => UiHelper.L("Result.NotActive"),
        AdlxResult.ResetNeeded => UiHelper.L("Result.ResetNeeded"),
        AdlxResult.Unavailable => UiHelper.L("Result.Unavailable"),
        _ => r.ToString()
    };
}

public readonly record struct AdlxRange(int Min, int Max, int Step)
{
    public bool IsValid => Max > Min;
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
    public static AdlxRange Empty => new(0, 0, 1);
}

public enum AdlxVSyncMode { AlwaysOff = 0, OffUnlessAppSpecifies = 1, OnUnlessAppSpecifies = 2, AlwaysOn = 3 }
public enum AdlxScaleMode { PreserveAspectRatio = 0, FullPanel = 1, Centered = 2 }
public enum AdlxTessellationMode { AmdOptimized = 0, UseAppSettings = 1, OverrideAppSettings = 2 }
public enum AdlxMemoryTiming
{
    Default = 0,
    FastTiming = 1,
    FastTimingLevel2 = 2,
    Automatic = 3,
    Level1 = 4,
    Level2 = 5
}
