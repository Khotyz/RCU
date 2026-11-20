using System.Text.Json;
using System.Text.Json.Serialization;
using RedControlUtility.Models;

namespace RedControlUtility.Services;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class SettingsService
{
    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json.Options);
                if (loaded != null) Current = loaded;
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Falha ao ler settings.json: {ex.Message}");
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, Json.Options));
        }
        catch (Exception ex)
        {
            LogService.Error($"Falha ao salvar settings.json: {ex.Message}");
        }
    }
}
