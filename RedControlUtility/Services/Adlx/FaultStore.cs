using System.Text.Json;

namespace RedControlUtility.Services.Adlx;

public static class FaultStore
{
    private static readonly object Sync = new();
    private static string FilePath => Path.Combine(AppPaths.DataDir, "driver-faults.json");

    public static string[] Load(string key)
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(FilePath)) return Array.Empty<string>();
                var all = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(FilePath));
                return all != null && all.TryGetValue(key, out var items) ? items : Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }

    public static void Save(string key, string[] items)
    {
        lock (Sync)
        {
            try
            {
                Dictionary<string, string[]> all = new();
                if (File.Exists(FilePath))
                {
                    try { all = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(FilePath)) ?? new(); }
                    catch { }
                }

                all[key] = items;
                File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
            }
            catch (Exception ex)
            {
                LogService.Warn($"Driver fault memory: {ex.Message}");
            }
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            try { File.Delete(FilePath); }
            catch { }
        }
    }
}
