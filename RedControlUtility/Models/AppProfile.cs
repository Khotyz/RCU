namespace RedControlUtility.Models;

/// <summary>
/// Perfil por aplicativo. Os campos de <see cref="Graphics"/> que forem null herdam o valor global.
/// Aplicado automaticamente enquanto o executável estiver em execução (e revertido ao fechar).
/// </summary>
public sealed class AppProfile
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string IconBase64 { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Não restaurar ao fechar o jogo: as opções ficam valendo globalmente até outro perfil mudar.</summary>
    public bool KeepAfterExit { get; set; }
    public GraphicsSettings Graphics { get; set; } = new();

    /// <summary>Limite de potência (%) enquanto o app estiver rodando; null = global.</summary>
    public int? PowerLimitPercent { get; set; }

    /// <summary>Preset de tuning do driver (Quiet/Balanced/Turbo/...), null = global.</summary>
    public string? TuningPreset { get; set; }

    public string ExeName => Path.GetFileName(ExecutablePath);
}
