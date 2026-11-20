namespace RedControlUtility.Services.Adlx;

/// <summary>ADLX_RESULT (ADLXDefines.h).</summary>
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

    /// <summary>Valor interno: a interface/slot não está disponível neste driver.</summary>
    Unavailable = 1000
}

public static class AdlxResultExtensions
{
    /// <summary>Equivalente a ADLX_SUCCEEDED().</summary>
    public static bool Succeeded(this AdlxResult r) =>
        r == AdlxResult.Ok || r == AdlxResult.AlreadyEnabled || r == AdlxResult.AlreadyInitialized;

    public static string Describe(this AdlxResult r) => r switch
    {
        AdlxResult.Ok => "OK",
        AdlxResult.AlreadyEnabled => "Já habilitado",
        AdlxResult.AlreadyInitialized => "Já inicializado",
        AdlxResult.Fail => "Falha",
        AdlxResult.InvalidArgs => "Argumento inválido",
        AdlxResult.BadVer => "Versão do ADLX incompatível",
        AdlxResult.UnknownInterface => "Interface desconhecida",
        AdlxResult.Terminated => "ADLX finalizado",
        AdlxResult.AdlInitError => "Erro de inicialização do ADL",
        AdlxResult.NotFound => "Não encontrado",
        AdlxResult.InvalidObject => "Objeto inválido",
        AdlxResult.OrphanObjects => "Objetos órfãos",
        AdlxResult.NotSupported => "Não suportado",
        AdlxResult.PendingOperation => "Operação pendente",
        AdlxResult.GpuInactive => "GPU inativa",
        AdlxResult.GpuInUse => "GPU em uso",
        AdlxResult.TimeoutOperation => "Tempo esgotado",
        AdlxResult.NotActive => "Não ativo",
        AdlxResult.ResetNeeded => "Reinício necessário",
        AdlxResult.Unavailable => "Indisponível neste driver",
        _ => r.ToString()
    };
}

/// <summary>ADLX_IntRange.</summary>
public readonly record struct AdlxRange(int Min, int Max, int Step)
{
    public bool IsValid => Max > Min;
    public int Clamp(int value) => Math.Clamp(value, Min, Max);
    public static AdlxRange Empty => new(0, 0, 1);
}

// Valores dos enums (ADLXDefines.h)
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
