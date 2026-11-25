using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace RedControlUtility.Services;

public sealed record UpdateProgress(string Stage, int Percent = 0, long Bytes = 0, long Total = 0);

public sealed record UpdateInfo(string Tag, Version Version, string PageUrl, string? AssetUrl, string AssetName, string Notes);

public static class UpdateService
{
    public const string Owner = "Khotyz";
    public const string Repo = "RCU";

    public const string StageDownload = "download";
    public const string StageDownloaded = "downloaded";
    public const string StageLaunch = "launch";
    public const string StageLaunched = "launched";
    public const string StageClosing = "closing";
    public const string StagePage = "page";

    public static Version Current { get; } = ReadCurrentVersion();

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Math.Max(Current.Build, 0)}";

    private static readonly HttpClient Http = CreateClient();

    public static UpdateInfo? Available { get; private set; }

    public static event Action<UpdateInfo>? Found;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"RedControlUtility/{CurrentText}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    private static Version ReadCurrentVersion()
    {
        try
        {
            var asm = typeof(UpdateService).Assembly;
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                int cut = info.IndexOfAny(new[] { '+', '-' });
                if (cut > 0) info = info[..cut];
                if (Version.TryParse(info, out var v)) return Normalize(v);
            }

            return Normalize(asm.GetName().Version ?? new Version(0, 1, 0));
        }
        catch
        {
            return new Version(0, 1, 0);
        }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string s = tag.Trim();
        int start = 0;
        while (start < s.Length && !char.IsDigit(s[start])) start++;
        int end = start;
        while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.')) end++;
        if (end <= start) return null;
        return Version.TryParse(s[start..end].TrimEnd('.'), out var v) ? Normalize(v) : null;
    }

    public static async Task<(UpdateInfo? Info, string? Error)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var res = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                return (null, UiHelper.L("Err.GitHubStatus", (int)res.StatusCode));
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? string.Empty : string.Empty;
            var version = ParseVersion(tag);
            if (version == null)
            {
                return (null, UiHelper.L("Err.ReleaseNoVersion"));
            }

            string page = root.TryGetProperty("html_url", out var htmlEl) && htmlEl.GetString() is { Length: > 0 } h
                ? h
                : $"https://github.com/{Owner}/{Repo}/releases";
            string notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;

            string? assetUrl = null;
            string assetName = string.Empty;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    string? dl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    if (string.IsNullOrEmpty(dl) || string.IsNullOrEmpty(name)) continue;
                    if (a.TryGetProperty("state", out var st) && !string.Equals(st.GetString(), "uploaded", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        !name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) &&
                        !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    bool setup = name.Contains("Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                    if (assetUrl != null && !setup) continue;
                    assetUrl = dl;
                    assetName = name;
                    if (setup) break;
                }
            }

            var info = new UpdateInfo(tag, version, page, assetUrl, assetName, notes);
            bool newer = version > Current;
            bool hasFiles = assetUrl != null;
            Available = newer && hasFiles ? info : null;
            LogService.Info($"Atualização: instalada {CurrentText}, GitHub {tag} ({version}) => {(Available != null ? "nova versão disponível" : newer ? "release ainda sem Setup ou Portable, ignorada" : "já está atualizado")}");
            if (Available != null) Found?.Invoke(Available);
            return (info, null);
        }
        catch (OperationCanceledException)
        {
            return (null, UiHelper.L("Err.Timeout"));
        }
        catch (Exception ex)
        {
            LogService.Warn($"Atualização: falha na verificação ({ex.Message})");
            return (null, ex.Message);
        }
    }

    public static async Task<(bool Ok, string Message)> InstallAsync(UpdateInfo info, IProgress<UpdateProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(info.AssetUrl))
            {
                progress?.Report(new UpdateProgress(StagePage));
                OpenPage(info.PageUrl);
                return (true, UiHelper.L("Update.PageOpened"));
            }

            string dir = Path.Combine(Path.GetTempPath(), "RedControlUtility", "update-" + info.Tag.Replace('/', '_'));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, info.AssetName);

            using (var res = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                res.EnsureSuccessStatusCode();
                long total = res.Content.Headers.ContentLength ?? 0;
                await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(file);
                var buffer = new byte[81920];
                long done = 0;
                int lastPercent = -1;
                int read;
                while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(new UpdateProgress(StageDownload, percent, done, total));
                    }
                }
            }

            progress?.Report(new UpdateProgress(StageDownloaded, 100));

            bool installer = info.AssetName.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                             info.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            progress?.Report(new UpdateProgress(StageLaunch));
            Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = installer ? "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS" : string.Empty,
                UseShellExecute = true,
                Verb = installer ? "runas" : string.Empty
            });
            progress?.Report(new UpdateProgress(StageLaunched));
            if (installer)
            {
                progress?.Report(new UpdateProgress(StageClosing));
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2500).ConfigureAwait(false);
                    Environment.Exit(0);
                });
            }

            return (true, file);
        }
        catch (Exception ex)
        {
            LogService.Error($"Atualização: falha ao baixar ({ex.Message})");
            return (false, ex.Message);
        }
    }

    public static void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Error($"Atualização: não consegui abrir o navegador ({ex.Message})");
        }
    }
}
