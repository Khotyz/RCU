using System.Text.Json;
using RedControlUtility.Models;

namespace RedControlUtility.Services;

public sealed class ProfileManager
{
    public async Task SaveProfileAsync(string filePath, UtilityProfile profile)
    {
        string json = JsonSerializer.Serialize(profile, Json.Options);
        await File.WriteAllTextAsync(filePath, json);
    }

    public UtilityProfile? LoadProfile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            return Migrate(JsonSerializer.Deserialize<UtilityProfile>(File.ReadAllText(filePath), Json.Options));
        }
        catch (Exception ex)
        {
            LogService.Error($"Perfil inválido '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static UtilityProfile? Migrate(UtilityProfile? p)
    {
        if (p != null && p.ColorProfile == "Fake HDR") p.ColorProfile = "FakeHdr";
        return p;
    }

    public async Task<UtilityProfile?> LoadProfileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            return Migrate(JsonSerializer.Deserialize<UtilityProfile>(await File.ReadAllTextAsync(filePath), Json.Options));
        }
        catch (Exception ex)
        {
            LogService.Error($"Perfil inválido '{filePath}': {ex.Message}");
            return null;
        }
    }
}
