using System.IO.Compression;
using System.Text;

namespace RedControlUtility.Services;

public static class ReportService
{
    public static string Create(string diagnostics)
    {
        string dir = Path.Combine(Path.GetDirectoryName(AppPaths.LogDir) ?? AppPaths.LogDir, "reports");
        Directory.CreateDirectory(dir);

        foreach (var old in new DirectoryInfo(dir).GetFiles("RCU-report-*.zip").OrderByDescending(f => f.CreationTimeUtc).Skip(4))
        {
            try { old.Delete(); }
            catch { }
        }

        string path = Path.Combine(dir, $"RCU-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "system.txt", Redact(SystemInfo()));
            Add(zip, "diagnostics.txt", Redact(diagnostics));
            if (File.Exists(AppPaths.SettingsFile)) Add(zip, "settings.json", Redact(ReadShared(AppPaths.SettingsFile)));
            foreach (var file in Directory.GetFiles(AppPaths.LogDir))
                Add(zip, "logs/" + Path.GetFileName(file), Redact(ReadShared(file)));
        }

        return path;
    }

    private static string SystemInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"App: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
        sb.AppendLine($"Culture: {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        sb.AppendLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string Redact(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile)) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length >= 3) text = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
