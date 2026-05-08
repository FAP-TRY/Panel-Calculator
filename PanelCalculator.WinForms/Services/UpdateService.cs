using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Checks for application updates on GitHub Releases and applies them via a
/// self-replace batch script.  The local database at %AppData%\PanelCalculator\
/// is never touched — only the application EXE is replaced.
/// </summary>
public static class UpdateService
{
    // ── Version — bump this on every release ─────────────────────────────
    public const string AppVersion = "1.1.0";

    // ── GitHub configuration ──────────────────────────────────────────────
    private const string Owner     = "FAP-TRY";
    private const string Repo      = "Panel-Calculator";
    private const string ApiUrl    = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    // Name of the asset file attached to each GitHub release.
    // Must match what is uploaded when creating the release.
    private const string AssetName = "PanelCalculator.exe";

    // ── Public record ─────────────────────────────────────────────────────
    public sealed record ReleaseInfo(
        string TagName,
        string Version,
        string DownloadUrl,
        string HtmlUrl,
        string ReleaseNotes);

    // ── API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Silently checks GitHub for a newer release.
    /// Returns null if already up-to-date, no asset found, or check failed.
    /// Never throws — designed for fire-and-forget background calls.
    /// </summary>
    public static async Task<ReleaseInfo?> CheckAsync()
    {
        try
        {
            using var client = MakeClient();
            var json = await client.GetStringAsync(ApiUrl).ConfigureAwait(false);
            var doc  = JsonNode.Parse(json);
            if (doc == null) return null;

            var tagName   = doc["tag_name"]?.GetValue<string>() ?? "";
            var htmlUrl   = doc["html_url"]?.GetValue<string>() ?? "";
            var body      = doc["body"]?.GetValue<string>() ?? "";
            var remoteVer = tagName.TrimStart('v');

            if (!IsNewer(remoteVer, AppVersion)) return null;

            // Find the download URL for our named asset
            var assets      = doc["assets"]?.AsArray();
            string? dlUrl   = null;
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    if (asset?["name"]?.GetValue<string>() == AssetName)
                    {
                        dlUrl = asset["browser_download_url"]?.GetValue<string>();
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(dlUrl)) return null;

            return new ReleaseInfo(tagName, remoteVer, dlUrl, htmlUrl, body);
        }
        catch
        {
            // Network error, JSON parse error, etc. — silently ignore.
            return null;
        }
    }

    /// <summary>
    /// Downloads the new EXE, writes a self-updater batch script to %TEMP%,
    /// launches it, and exits the application.
    /// <para>
    /// The batch script waits for the current process to end, copies the new
    /// EXE over the old one, and re-launches the application.
    /// </para>
    /// </summary>
    public static async Task DownloadAndApplyAsync(
        ReleaseInfo info,
        IProgress<(int Percent, string Status)>? progress = null)
    {
        var tempDir    = Path.GetTempPath();
        var tempExe    = Path.Combine(tempDir, "PanelCalculator_update.exe");
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                         ?? Path.Combine(AppContext.BaseDirectory, "PanelCalculator.WinForms.exe");

        // ── Download ──────────────────────────────────────────────────────
        progress?.Report((0, "Memulai download..."));
        using var client = MakeClient(timeout: 300);
        using var resp   = await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead)
                                        .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var totalBytes = resp.Content.Headers.ContentLength ?? 0L;
        await using var src  = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var dest = File.Create(tempExe);

        var    buf        = new byte[81_920];
        long   downloaded = 0;
        int    read;
        while ((read = await src.ReadAsync(buf).ConfigureAwait(false)) > 0)
        {
            await dest.WriteAsync(buf.AsMemory(0, read)).ConfigureAwait(false);
            downloaded += read;
            if (totalBytes > 0)
            {
                int pct = (int)(downloaded * 100 / totalBytes);
                long mb = downloaded / 1024 / 1024;
                progress?.Report((pct, $"Mendownload... {mb} MB / {totalBytes / 1024 / 1024} MB"));
            }
        }
        await dest.FlushAsync().ConfigureAwait(false);
        progress?.Report((100, "Download selesai. Mempersiapkan pembaruan..."));

        // ── Build updater batch script ────────────────────────────────────
        var batPath = Path.Combine(tempDir, "panel_calc_update.bat");
        var exeName = Path.GetFileName(currentExe);
        var bat = $"""
            @echo off
            setlocal
            set CURRENT_EXE={currentExe}
            set NEW_EXE={tempExe}
            :WAITLOOP
            tasklist /FI "IMAGENAME eq {exeName}" 2>NUL | find /I "{exeName}" >NUL
            if "%ERRORLEVEL%"=="0" (
                timeout /t 1 /nobreak >NUL
                goto WAITLOOP
            )
            timeout /t 1 /nobreak >NUL
            copy /Y "%NEW_EXE%" "%CURRENT_EXE%"
            if exist "%NEW_EXE%" del "%NEW_EXE%"
            start "" "%CURRENT_EXE%"
            del "%~f0"
            """;

        await File.WriteAllTextAsync(batPath, bat).ConfigureAwait(false);

        // ── Launch batch script and exit ──────────────────────────────────
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
        {
            CreateNoWindow  = true,
            UseShellExecute = false
        });

        Application.Exit();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static HttpClient MakeClient(int timeout = 15)
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
        c.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PanelCalculator", AppVersion));
        // GitHub API requires an Accept header for JSON
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    private static bool IsNewer(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
