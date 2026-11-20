using System.Globalization;
using System.Text.Json;

namespace RedControlUtility.Services;

public sealed class LocalizationService
{
    private Dictionary<string, string> _current = new();
    private Dictionary<string, string> _fallback = new();

    public string Language { get; private set; } = "en-US";
    public event Action? LanguageChanged;

    public static string[] Supported { get; } = { "pt-BR", "en-US" };

    /// <summary>Idioma padrão: o do Windows (pt-* => pt-BR; caso contrário en-US).</summary>
    public static string DetectSystemLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase) ? "pt-BR" : "en-US";

    public void LoadLanguage(string language)
    {
        if (!Supported.Contains(language)) language = "en-US";
        _fallback = Read("en-US");
        _current = language == "en-US" ? _fallback : Read(language);
        Language = language;
        var handlers = LanguageChanged;
        if (handlers == null) return;
        foreach (Action h in handlers.GetInvocationList())
        {
            try { h(); }
            catch (Exception ex) { LogService.Error($"Idioma: {ex.Message}"); }
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
            LogService.Error($"Idioma {lang}: {ex.Message}");
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
