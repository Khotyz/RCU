using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RedControlUtility.Services;

public sealed class GameEntry
{
    public string Name { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string Launcher { get; set; } = string.Empty;
    public string? IconPath { get; set; }
}

public static class GameLibraryService
{
    private static readonly string[] IgnoredExe =
    {
        "unins", "uninst", "crash", "unitycrash", "vc_redist", "vcredist", "dxsetup", "dotnet", "directx", "setup", "installer",
        "redist", "launcher_helper", "benchmark", "ue4prereq", "easyanticheat", "eac", "battleye", "beservice", "cefsharp", "webhelper",
        "crashpad", "report", "updater", "notification_helper", "oalinst", "physx"
    };

    private static readonly Regex VdfPath = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);

    private static readonly string[] IgnoredNames = { "steamworks", "redistributable", "steam linux runtime", "proton", "wallpaper engine", "soundtrack" };

    public static List<GameEntry> Scan()
    {
        var all = new List<GameEntry>();
        Safe(() => all.AddRange(ScanSteam()), "Steam");
        Safe(() => all.AddRange(ScanEpic()), "Epic");
        Safe(() => all.AddRange(ScanGog()), "GOG");
        return all
            .Where(g => !string.IsNullOrEmpty(g.ExecutablePath) && File.Exists(g.ExecutablePath))
            .GroupBy(g => g.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Safe(Action a, string what)
    {
        try { a(); }
        catch (Exception ex) { LogService.Warn($"Game scan ({what}): {ex.Message}"); }
    }

    private static IEnumerable<GameEntry> ScanSteam()
    {
        string? root = null;
        foreach (var (hive, view, key, name) in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Registry64, @"Software\Valve\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Valve\Steam", "InstallPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Valve\Steam", "InstallPath")
        })
        {
            using var b = RegistryKey.OpenBaseKey(hive, view);
            using var k = b.OpenSubKey(key);
            if (k?.GetValue(name) is string s && Directory.Exists(s)) { root = s.Replace('/', '\\'); break; }
        }

        if (root == null) yield break;

        var libraries = new List<string> { root };
        string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in VdfPath.Matches(File.ReadAllText(vdf)))
            {
                string p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(p) && !libraries.Contains(p, StringComparer.OrdinalIgnoreCase)) libraries.Add(p);
            }
        }

        foreach (string lib in libraries)
        {
            string apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (string acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(acf); } catch { continue; }
                string? name = VdfValue(text, "name"), dir = VdfValue(text, "installdir");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dir)) continue;
                if (IgnoredNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                string install = Path.Combine(apps, "common", dir);
                string? exe = FindMainExecutable(install);
                if (exe != null) yield return new GameEntry { Name = name, ExecutablePath = exe, Launcher = "Steam" };
            }
        }
    }

    private static IEnumerable<GameEntry> ScanEpic()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (string file in Directory.EnumerateFiles(dir, "*.item"))
        {
            GameEntry? entry = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array &&
                    !cats.EnumerateArray().Any(c => string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase))) continue;
                string name = r.TryGetProperty("DisplayName", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                string install = r.TryGetProperty("InstallLocation", out var i) ? i.GetString() ?? string.Empty : string.Empty;
                string launch = r.TryGetProperty("LaunchExecutable", out var l) ? l.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(name) || !Directory.Exists(install)) continue;
                string? exe = FindMainExecutable(install);
                if (exe == null && !string.IsNullOrEmpty(launch) && File.Exists(Path.Combine(install, launch))) exe = Path.Combine(install, launch);
                if (exe != null) entry = new GameEntry { Name = name, ExecutablePath = exe, Launcher = "Epic Games" };
            }
            catch (Exception ex)
            {
                LogService.Warn($"Epic manifest '{Path.GetFileName(file)}': {ex.Message}");
            }

            if (entry != null) yield return entry;
        }
    }

    private static IEnumerable<GameEntry> ScanGog()
    {
        using var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        using var games = b.OpenSubKey(@"SOFTWARE\GOG.com\Games");
        if (games == null) yield break;
        foreach (string id in games.GetSubKeyNames())
        {
            using var k = games.OpenSubKey(id);
            string? name = k?.GetValue("gameName") as string;
            string? path = k?.GetValue("path") as string;
            string? exe = k?.GetValue("exe") as string;
            if (string.IsNullOrEmpty(name)) continue;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) exe = !string.IsNullOrEmpty(path) ? FindMainExecutable(path) : null;
            if (exe != null) yield return new GameEntry { Name = name, ExecutablePath = exe, Launcher = "GOG" };
        }
    }

    private static string? FindMainExecutable(string root)
    {
        if (!Directory.Exists(root)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        FileInfo? best = null;
        foreach (string f in Directory.EnumerateFiles(root, "*.exe", options))
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (IgnoredExe.Any(i => n.Contains(i, StringComparison.OrdinalIgnoreCase))) continue;
            var fi = new FileInfo(f);
            if (best == null || fi.Length > best.Length) best = fi;
        }

        return best?.FullName;
    }

    private static string? VdfValue(string text, string key)
    {
        var m = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

}
