namespace RedControlUtility.Models;

public sealed class AppProfile
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string IconBase64 { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public bool UseGlobal { get; set; }
    public string Source { get; set; } = string.Empty;
    public GraphicsSettings Graphics { get; set; } = new();

    public int? PowerLimitPercent { get; set; }
    public string RunOnStart { get; set; } = string.Empty;
    public string RunOnExit { get; set; } = string.Empty;

    public string ExeName => Path.GetFileName(ExecutablePath);
}
