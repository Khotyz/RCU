using RedControlUtility.Services.Adlx;

namespace RedControlUtility.Models;

public enum ApplyMode
{
    /// <summary>Cada alteração é aplicada ao driver imediatamente.</summary>
    Instant = 0,
    /// <summary>As alterações só vão para o driver ao clicar em "Aplicar".</summary>
    Manual = 1
}

/// <summary>Configurações do próprio aplicativo (settings.json em %APPDATA%\RedControlUtility).</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = string.Empty;
    public ApplyMode GraphicsApplyMode { get; set; } = ApplyMode.Manual;
    public bool ConfirmTuning { get; set; } = true;
    public bool LiveApplyUndervolt { get; set; }
    public bool KeepMinClockBelowMax { get; set; }
    public bool AdvancedMode { get; set; }
    public bool AutoRevertTuning { get; set; } = true;

    /// <summary>Limite de FPS (FRTC) global, valido para todos os jogos. Aplicado ao abrir o app e ao mudar.</summary>
    public bool GlobalFrtc { get; set; }

    /// <summary>Aviso de superaquecimento pela sobreposicao. Desligado por padrao, limite de 80 C.</summary>
    public bool OverheatWarn { get; set; }
    public int OverheatTempC { get; set; } = 80;
    public bool OverheatAlwaysOn { get; set; }
    public int OverheatSeconds { get; set; } = 10;
    public int GlobalFrtcFps { get; set; } = 60;

    /// <summary>Se o driver de vídeo travar e se recuperar (TDR) com tuning ativo, volta ao padrão de fábrica sozinho.</summary>
    public bool AutoResetOnInstability { get; set; } = true;
    public bool ApplyProfileOnStartup { get; set; }
    public bool PerAppProfilesEnabled { get; set; } = true;
    public bool RestoreOnAppExit { get; set; } = true;
    /// <summary>Mostra um overlay curto por cima do jogo com as tecnologias ativas quando o perfil entra em ação.</summary>
    public bool NotifyOnProfile { get; set; } = true;

    /// <summary>Quantos segundos o overlay fica visível ao iniciar o jogo.</summary>
    private double _overlaySeconds = 3;

    /// <summary>Segundos inteiros (arredonda na escrita, então o JSON mostra 3 e nunca 3.00001).</summary>
    public double OverlaySeconds { get => _overlaySeconds; set => _overlaySeconds = Math.Round(value); }

    /// <summary>Onde o overlay aparece: 0 sup. esquerdo, 1 sup. direito, 2 inf. esquerdo, 3 inf. direito, 4 topo central.</summary>
    public int OverlayCorner { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public int SensorIntervalMs { get; set; } = 1000;

    /// <summary>Consulta as releases do GitHub ao abrir o app (nada é baixado sem confirmação).</summary>
    public bool CheckForUpdates { get; set; } = true;
    public AdlxBackendKind Backend { get; set; } = AdlxBackendKind.Auto;
    public int GpuIndex { get; set; }
    public string GamesViewMode { get; set; } = "List";
}
