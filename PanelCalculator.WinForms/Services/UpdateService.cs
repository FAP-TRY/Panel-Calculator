using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using PanelCalculator.Data.Security;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Checks for application updates on GitHub Releases and applies them via a
/// self-replace PowerShell script.  The local database at %AppData%\PanelCalculator\
/// is never touched — only the application EXE is replaced.
///
/// Security model (added 2026-05-16):
///   1. Each release MUST publish a manifest asset named
///      "PanelCalculator.exe.sha256" alongside the EXE.
///   2. After the EXE is downloaded, the client also fetches the manifest,
///      recomputes SHA-256 locally, and refuses to install if the hashes
///      do not match exactly.
///   3. Only release-asset hosts owned by GitHub itself are accepted.
///   4. Every check / verify / fail is appended to
///      %AppData%\PanelCalculator\logs\update-yyyy-MM-dd.log
/// </summary>
public static class UpdateService
{
    // ── Version — bump this on every release ─────────────────────────────
    public const string AppVersion = "1.2.5";

    // ── GitHub configuration ──────────────────────────────────────────────
    private const string Owner    = "FAP-TRY";
    private const string Repo     = "Panel-Calculator";
    private const string ApiUrl   = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    // Name of the asset file attached to each GitHub release.
    // Must match what is uploaded when creating the release.
    private const string AssetName         = "PanelCalculator.exe";
    private const string ManifestAssetName = "PanelCalculator.exe.sha256";

    // ── Public record ─────────────────────────────────────────────────────
    public sealed record ReleaseInfo(
        string TagName,
        string Version,
        string DownloadUrl,
        string HtmlUrl,
        string ReleaseNotes,
        string? ManifestUrl);

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

            // Find the download URL for our named asset (and the manifest, if present)
            var assets       = doc["assets"]?.AsArray();
            string? dlUrl       = null;
            string? manifestUrl = null;
            if (assets != null)
            {
                foreach (var asset in assets)
                {
                    var name = asset?["name"]?.GetValue<string>();
                    if (name == AssetName)
                        dlUrl = asset!["browser_download_url"]?.GetValue<string>();
                    else if (name == ManifestAssetName)
                        manifestUrl = asset!["browser_download_url"]?.GetValue<string>();
                }
            }

            if (string.IsNullOrEmpty(dlUrl)) return null;

            return new ReleaseInfo(tagName, remoteVer, dlUrl, htmlUrl, body, manifestUrl);
        }
        catch
        {
            // Network error, JSON parse error, etc. — silently ignore.
            return null;
        }
    }

    /// <summary>
    /// Downloads the new EXE, verifies its SHA-256 against the release
    /// manifest, writes a self-updater PowerShell script to %TEMP%, launches
    /// it with admin elevation (UAC), and exits the application.
    /// </summary>
    public static async Task DownloadAndApplyAsync(
        ReleaseInfo info,
        IProgress<(int Percent, string Status)>? progress = null)
    {
        var tempDir    = Path.GetTempPath();
        var tempExe    = Path.Combine(tempDir, "PanelCalculator_update.exe");
        var tempSha    = Path.Combine(tempDir, "PanelCalculator_update.exe.sha256");
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
                         ?? Path.Combine(AppContext.BaseDirectory, "PanelCalculator.WinForms.exe");

        var log = OpenUpdateLog();
        log($"Begin update apply. tag={info.TagName} target={info.Version}");

        // ── Validate URLs (host pinning) ─────────────────────────────────
        progress?.Report((0, "Memvalidasi sumber update..."));

        if (string.IsNullOrWhiteSpace(info.DownloadUrl))
        {
            log("ABORT: empty DownloadUrl in ReleaseInfo.");
            throw new InvalidOperationException("URL download tidak valid.");
        }

        if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var dlUri) ||
            !UpdateVerifier.IsAllowedDownloadHost(dlUri))
        {
            log($"ABORT: DownloadUrl host '{info.DownloadUrl}' is not in the allowed list.");
            throw new UpdateVerificationException(
                "URL download bukan dari host GitHub yang diizinkan. " +
                "Update dibatalkan untuk keamanan.");
        }

        if (string.IsNullOrWhiteSpace(info.ManifestUrl))
        {
            log("ABORT: release has no SHA-256 manifest asset (PanelCalculator.exe.sha256).");
            throw new UpdateVerificationException(
                "Rilis ini tidak menyertakan file verifikasi keamanan " +
                "(PanelCalculator.exe.sha256). Update dibatalkan. Hubungi support.");
        }

        if (!Uri.TryCreate(info.ManifestUrl, UriKind.Absolute, out var manifestUri) ||
            !UpdateVerifier.IsAllowedDownloadHost(manifestUri))
        {
            log($"ABORT: ManifestUrl host '{info.ManifestUrl}' is not in the allowed list.");
            throw new UpdateVerificationException(
                "URL manifest keamanan bukan dari host GitHub yang diizinkan. " +
                "Update dibatalkan.");
        }

        // ── Download EXE ────────────────────────────────────────────────
        progress?.Report((0, "Memulai download..."));

        using (var client = MakeDownloadClient())
        using (var resp   = await client.GetAsync(dlUri, HttpCompletionOption.ResponseHeadersRead)
                                        .ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();

            // Validate the FINAL responding host (in case of redirect)
            var finalUri = resp.RequestMessage?.RequestUri ?? dlUri;
            if (!UpdateVerifier.IsAllowedDownloadHost(finalUri))
            {
                log($"ABORT: EXE final redirect host '{finalUri.Host}' not allowed.");
                throw new UpdateVerificationException(
                    "Server mengarahkan download EXE ke host yang tidak diizinkan. " +
                    "Update dibatalkan.");
            }

            // Sanity-check: must be a binary (not an HTML error page)
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                log($"ABORT: EXE download returned HTML (content-type={contentType}).");
                throw new InvalidOperationException(
                    $"Server mengembalikan halaman HTML, bukan file EXE.\n" +
                    $"Coba download manual dari:\n{info.HtmlUrl}");
            }

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
        }

        // Validate downloaded file size (must be > 10 MB to be a valid EXE)
        var fileInfo = new FileInfo(tempExe);
        if (fileInfo.Length < 10 * 1024 * 1024)
        {
            log($"ABORT: downloaded EXE too small ({fileInfo.Length} bytes).");
            TryDelete(tempExe);
            throw new InvalidOperationException(
                $"File yang didownload terlalu kecil ({fileInfo.Length / 1024} KB) — " +
                $"kemungkinan terjadi error saat download.\n" +
                $"Coba lagi atau download manual dari:\n{info.HtmlUrl}");
        }
        log($"EXE downloaded OK. size={fileInfo.Length:N0} bytes, path={tempExe}");

        // ── Download SHA-256 manifest ────────────────────────────────────
        progress?.Report((100, "Memverifikasi keamanan file..."));

        string manifestBody;
        try
        {
            using var manifestClient = MakeManifestClient();
            using var manifestResp   = await manifestClient.GetAsync(manifestUri)
                                                           .ConfigureAwait(false);
            manifestResp.EnsureSuccessStatusCode();

            var finalManifestUri = manifestResp.RequestMessage?.RequestUri ?? manifestUri;
            if (!UpdateVerifier.IsAllowedDownloadHost(finalManifestUri))
            {
                log($"ABORT: manifest final redirect host '{finalManifestUri.Host}' not allowed.");
                TryDelete(tempExe);
                throw new UpdateVerificationException(
                    "Server mengarahkan download manifest ke host yang tidak diizinkan. " +
                    "Update dibatalkan.");
            }

            manifestBody = await manifestResp.Content.ReadAsStringAsync().ConfigureAwait(false);
            await File.WriteAllTextAsync(tempSha, manifestBody).ConfigureAwait(false);
            log($"Manifest downloaded OK. bytes={manifestBody.Length}, path={tempSha}");
        }
        catch (UpdateVerificationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log($"ABORT: failed to download SHA-256 manifest. {ex.GetType().Name}: {ex.Message}");
            TryDelete(tempExe);
            TryDelete(tempSha);
            throw new UpdateVerificationException(
                "Gagal mengunduh file verifikasi keamanan (SHA-256 manifest). " +
                "Update dibatalkan. Coba lagi nanti atau hubungi support.", ex);
        }

        // ── Verify hash ──────────────────────────────────────────────────
        try
        {
            UpdateVerifier.VerifyOrThrow(tempExe, manifestBody);
            log("OK: SHA-256 hash matches manifest. Proceeding with apply.");
        }
        catch (UpdateVerificationException ex)
        {
            log($"ABORT: SHA-256 verification FAILED. {ex.Message}");
            TryDelete(tempExe);
            TryDelete(tempSha);
            throw;
        }

        // Manifest file is no longer needed once verification has passed.
        TryDelete(tempSha);

        progress?.Report((100, "Verifikasi keamanan OK. Mempersiapkan pembaruan..."));

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
        log($"Updater PS1 written to '{ps1Path}'. Launching elevated.");

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

    /// <summary>
    /// HTTP client for the SHA-256 manifest text file. Small file — short timeout.
    /// </summary>
    private static HttpClient MakeManifestClient(int timeout = 30)
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
        c.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PanelCalculator", AppVersion));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("text/plain"));
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

    /// <summary>
    /// Returns an action that appends one line to today's update log,
    /// located at %AppData%\PanelCalculator\logs\update-yyyy-MM-dd.log.
    /// All logging failures are swallowed (logging must never break update).
    /// </summary>
    private static Action<string> OpenUpdateLog()
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PanelCalculator",
                "logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, $"update-{DateTime.Now:yyyy-MM-dd}.log");

            return msg =>
            {
                try
                {
                    File.AppendAllText(logPath,
                        $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
                }
                catch
                {
                    // Swallow — logging must never break update.
                }
            };
        }
        catch
        {
            return _ => { };
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
