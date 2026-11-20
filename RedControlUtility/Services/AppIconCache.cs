namespace RedControlUtility.Services;

/// <summary>Guarda o ícone de cada app como PNG em disco (carregado pequeno, sem manter bitmaps na memória).</summary>
public static class AppIconCache
{
    private static string Dir => Path.Combine(AppPaths.DataDir, "icons");

    public static string? GetPath(Models.AppProfile app)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath)) return null;
            Directory.CreateDirectory(Dir);
            string file = Path.Combine(Dir, $"{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(app.ExecutablePath.ToLowerInvariant())))[..12]}.png");
            if (File.Exists(file)) return file;

            string b64 = app.IconBase64;
            if (string.IsNullOrEmpty(b64)) b64 = app.IconBase64 = ShortcutResolver.ExtractIconBase64(app.ExecutablePath);
            if (string.IsNullOrEmpty(b64)) return null;
            File.WriteAllBytes(file, Convert.FromBase64String(b64));
            return file;
        }
        catch
        {
            return null;
        }
    }
}
