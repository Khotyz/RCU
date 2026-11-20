namespace RedControlUtility.Models;

public sealed class UtilityProfile
{
    public string ProfileName { get; set; } = "Default";
    public string GpuModel { get; set; } = string.Empty;
    public GpuArchitecture Architecture { get; set; }

    public GraphicsSettings Graphics { get; set; } = new();
    public TuningSettings Tuning { get; set; } = new();
    public string ColorProfile { get; set; } = "Default";
    public double CustomGamma { get; set; } = 1.0;
    public double CustomRed { get; set; } = 1.0;
    public double CustomGreen { get; set; } = 1.0;
    public double CustomBlue { get; set; } = 1.0;

    public List<AppProfile> Apps { get; set; } = new();
}
