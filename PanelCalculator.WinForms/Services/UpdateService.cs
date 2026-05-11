using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Checks for application updates on GitHub Releases and applies them via a
/// self-replace PowerShell script.  The local database at %AppData%\PanelCalculator\
/// is never touched — only the application EXE is replaced.
/// </summary>
public static class UpdateService
{
    // ── Version — bump this on every release ─────────────────────────────
    public const string AppVersion = "1.2.0";

    // ── GitHub configuration ──────────────────────────────────────────────
    private const string Owner    = "FAP-TRY";
    private const string Repo     = "Panel-Calculator";
    private const string ApiUrl   = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

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
            using var client = MakeApiClient();
            var json = await client.GetStringAsync(ApiUrl).ConfigureAwait(false);
            var doc  = JsonNode.Parse(json);
            if (doc == null) return null;

            var tagName   = doc["tag_name"]?.GetValue<string>() ?? "";
            var htmlUrl   = doc["html_url"]?.GetValue<string>() ?? "";
            var body      = doc["body"]?.GetValue<string>() ?? "";
            var remoteVer = tagName.TrimStart('v');

            if (!IsNewer(remoteVer, AppVersion)) return null;

            // Find the download URL for our named asset
            var assets    = doc["assets"]?.AsArray();
            string? dlUrl = null;
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
    /// Downloads the new EXE, writes a self-updater PowerShell script to %TEMP%,
    /// launches it with admin elevation (UAC), and exits the application.
    /// The script waits for the app to close, replaces the EXE, and restarts it.
    /// </summary>
    public static async Task DownloadAndApplyAsync(
        ReleaseInfo info,
        IProgress<(int Percent, string Status)>? progress = null)
    {
        var tempDir    = Path.GetTempPath();
        var tempExe    = Path.Combine(tempDir, "PanelCalculator_update.exe");
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                         ?? Path.Combine(AppContext.BaseDirectory, "PanelCalculator.WinForms.exe");

        // ── Download (use binary client — no Accept:application/json) ─────
        progress?.Report((0, "Memulai download..."));

        // Validate download URL before starting
        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
            throw new InvalidOperationException("URL download tidak valid.");

        using var client = MakeDownloadClient();
        using var resp   = await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead)
                                       .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        // Sanity-check: must be a binary (not an HTML error page)
        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Server mengembalikan halaman HTML, bukan file EXE.\n" +
                $"Coba download manual dari:\n{info.HtmlUrl}");

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

        // Validate downloaded file size (must be > 10 MB to be a valid EXE)
        var fileInfo = new FileInfo(tempExe);
        if (fileInfo.Length < 10 * 1024 * 1024)
            throw new InvalidOperationException(
                $"File yang didownload terlalu kecil ({fileInfo.Length / 1024} KB) — " +
                $"kemungkinan terjadi error saat download.\n" +
                $"Coba lagi atau download manual dari:\n{info.HtmlUrl}");

        progress?.Report((100, "Download selesai. Mempersiapkan pembaruan..."));

        // ── Build PowerShell updater script ───────────────────────────────
        // PowerShell handles Program Files elevation better than cmd.exe
        var ps1Path = Path.Combine(tempDir, "panel_calc_update.ps1");
        var exeName = Path.GetFileNameWithoutExtension(currentExe); // without .exe for Get-Process

        // Build PowerShell script — use @"..." verbatim so $var stays as PowerShell syntax
        // Substitute only the three file-path values via String.Replace
        var ps1 = @"# Panel Calculator Auto-Updater
$newExe     = 'NEW_EXE_PATH'
$currentExe = 'CURRENT_EXE_PATH'
$procName   = 'PROC_NAME'

# Wait for the running app to exit (max 30s)
$waited = 0
while ((Get-Process -Name $procName -ErrorAction SilentlyContinue) -and $waited -lt 30) {
    Start-Sleep -Seconds 1
    $waited++
}
Start-Sleep -Seconds 1

# Replace EXE
try {
    Copy-Item -Path $newExe -Destination $currentExe -Force -ErrorAction Stop
    Remove-Item -Path $newExe -Force -ErrorAction SilentlyContinue
    Start-Process -FilePath $currentExe
} catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        ""Gagal mengganti file aplikasi:`n$_`n`nSilakan copy manual:`n$newExe`n-> $currentExe"",
        'Update Gagal')
}"
            .Replace("NEW_EXE_PATH",     tempExe.Replace("'", "''"))
            .Replace("CURRENT_EXE_PATH", currentExe.Replace("'", "''"))
            .Replace("PROC_NAME",        exeName);

        await File.WriteAllTextAsync(ps1Path, ps1).ConfigureAwait(false);

        // ── Launch PowerShell as Administrator (UAC) and exit ────────────
        // -Verb RunAs triggers the UAC elevation prompt so we can write to Program Files
        Process.Start(new ProcessStartInfo
        {
            FileName        = "powershell.exe",
            Arguments       = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1Path}\"",
            UseShellExecute = true,   // required for Verb = runas
            Verb            = "runas" // UAC elevation — needed for Program Files
        });

        Application.Exit();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>HTTP client for GitHub JSON API (requires Accept: application/json).</summary>
    private static HttpClient MakeApiClient(int timeout = 15)
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
        c.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PanelCalculator", AppVersion));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return c;
    }

    /// <summary>
    /// HTTP client for binary file downloads.
    /// Does NOT set Accept:application/json — that can confuse CDN redirects
    /// and cause GitHub to return a JSON error page instead of the binary.
    /// </summary>
    private static HttpClient MakeDownloadClient(int timeout = 600)
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
        c.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PanelCalculator", AppVersion));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("*/*", 0.9));
        return c;
    }

    private static bool IsNewer(string remote, string local)
    {
        if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
            return r > l;
        return string.Compare(remote, local, StringComparison.OrdinalIgnoreCase) > 0;
    }
}
