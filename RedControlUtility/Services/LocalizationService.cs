using System.Globalization;
using System.Text.Json;

namespace RedControlUtility.Services;

public sealed class LocalizationService
{
    private Dictionary<string, string> _current = new();
    private Dictionary<string, string> _fallback = new();

    public string Language { get; private set; } = "en-US";
    public event Action? LanguageChanged;

    public static IReadOnlyList<(string Code, string Name)> Supported { get; } = Array.AsReadOnly(new[]
    {
        ("pt-BR", "Português (Brasil)"),
        ("en-US", "English (US)"),
        ("es-ES", "Español"),
        ("zh-CN", "简体中文")
    });

    public static string DetectSystemLanguage() => DetectLanguage(CultureInfo.CurrentUICulture);

    internal static string DetectLanguage(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "pt" => "pt-BR",
        "es" => "es-ES",
        "zh" when !culture.Name.Contains("-Hant", StringComparison.OrdinalIgnoreCase) &&
                   culture.Name is not ("zh-TW" or "zh-HK" or "zh-MO" or "zh-CHT") => "zh-CN",
        _ => "en-US"
    };

    public void LoadLanguage(string language)
    {
        language = Supported.FirstOrDefault(item => item.Code.Equals(language, StringComparison.OrdinalIgnoreCase)).Code ?? "en-US";
        _fallback = Read("en-US");
        _current = language == "en-US" ? _fallback : Read(language);
        Language = language;
        var handlers = LanguageChanged;
        if (handlers == null) return;
        foreach (Action h in handlers.GetInvocationList())
        {
            try { h(); }
            catch (Exception ex) { LogService.Error($"Language: {ex.Message}"); }
        }
    }

    private static Dictionary<string, string> Read(string lang)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "locales", $"{lang}.json");
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Language {lang}: {ex.Message}");
        }

        return new Dictionary<string, string>();
    }

    public string Get(string key)
    {
        if (_current.TryGetValue(key, out var v)) return v;
        if (_fallback.TryGetValue(key, out v)) return v;
        return key;
    }

    public string Get(string key, params object[] args)
    {
        try { return string.Format(CultureInfo.CurrentCulture, Get(key), args); }
        catch { return Get(key); }
    }
}
