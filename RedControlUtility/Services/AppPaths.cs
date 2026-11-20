namespace RedControlUtility.Services;

public static class AppPaths
{
    public const string AppName = "RedControlUtility";

    public static string DataDir { get; } = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName));
    public static string LogDir { get; } = Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName, "logs"));

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string ProfileFile => Path.Combine(DataDir, "profile.json");
    public static string PendingFlag => Path.Combine(DataDir, "tuning.pending");

    private static string Ensure(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
        }

        return path;
    }
}
