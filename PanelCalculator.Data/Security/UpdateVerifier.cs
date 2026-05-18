using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PanelCalculator.Data.Security;

/// <summary>
/// Pure helper utilities for verifying that an auto-update payload (the
/// downloaded EXE) is genuine. Lives in <c>PanelCalculator.Data</c> instead
/// of <c>PanelCalculator.WinForms</c> so it can be unit-tested without
/// pulling in WinForms.
///
/// Verification scheme (intentionally pragmatic, no code-signing cert):
///   1. Each GitHub Release publishes a manifest file
///      <c>PanelCalculator.exe.sha256</c> alongside the EXE.
///   2. Manifest content is one line in the same format produced by
///      `sha256sum`: <c>&lt;64-hex-digest&gt;  PanelCalculator.exe</c>
///   3. The client downloads both files, recomputes SHA-256 of the EXE
///      locally, and refuses to install if the hashes do not match exactly
///      (case-insensitive comparison on the hex digest).
///   4. Defence-in-depth: only release-asset URLs hosted by GitHub itself
///      are accepted (<c>github.com</c> / <c>objects.githubusercontent.com</c>).
/// </summary>
public static class UpdateVerifier
{
    /// <summary>
    /// Hosts considered acceptable for release-asset downloads.
    /// Anything outside this list is treated as a potential redirect-hijack.
    /// </summary>
    public static readonly string[] AllowedDownloadHosts = new[]
    {
        "github.com",
        "objects.githubusercontent.com",
    };

    /// <summary>
    /// Compute the SHA-256 hex digest (lowercase, 64 chars) for a file.
    /// </summary>
    public static string ComputeSha256(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath kosong.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File untuk hashing tidak ditemukan.", filePath);

        using var stream = File.OpenRead(filePath);
        return ComputeSha256(stream);
    }

    /// <summary>
    /// Compute the SHA-256 hex digest (lowercase, 64 chars) for raw bytes.
    /// </summary>
    public static string ComputeSha256(byte[] bytes)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Compute the SHA-256 hex digest (lowercase, 64 chars) for a stream.
    /// The stream is read to the end but is NOT disposed by this method.
    /// </summary>
    public static string ComputeSha256(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        using var sha = SHA256.Create();
        var digest = sha.ComputeHash(stream);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// Parse the expected hex digest out of a sha256-manifest body.
    ///
    /// Accepts any of these forms (whitespace-tolerant):
    ///   <c>abc...def</c>                           — bare 64-hex
    ///   <c>abc...def  PanelCalculator.exe</c>      — sha256sum-style (2 spaces)
    ///   <c>abc...def *PanelCalculator.exe</c>      — sha256sum binary marker
    ///   <c>SHA256: abc...def</c>                   — labelled
    /// Multi-line manifests: the first line containing a 64-hex token wins.
    /// </summary>
    /// <returns>Lowercase 64-char hex digest.</returns>
    /// <exception cref="FormatException">If no 64-hex token can be parsed.</exception>
    public static string ParseManifest(string manifestBody)
    {
        if (manifestBody == null)
            throw new ArgumentNullException(nameof(manifestBody));

        // Match any 64 consecutive hex chars (case-insensitive) anywhere in the body.
        var match = Regex.Match(manifestBody, "(?i)\\b[0-9a-f]{64}\\b");
        if (!match.Success)
        {
            throw new FormatException(
                "File manifest SHA-256 tidak berisi digest 64-karakter heksadesimal yang valid.");
        }

        return match.Value.ToLowerInvariant();
    }

    /// <summary>
    /// True if the hashes match (case-insensitive, ignores surrounding whitespace).
    /// </summary>
    public static bool HashesMatch(string expectedHex, string actualHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex) || string.IsNullOrWhiteSpace(actualHex))
            return false;
        return string.Equals(
            expectedHex.Trim(),
            actualHex.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True if <paramref name="uri"/> is HTTPS and its host is in
    /// <see cref="AllowedDownloadHosts"/> (exact or sub-domain match).
    /// </summary>
    public static bool IsAllowedDownloadHost(Uri uri)
    {
        if (uri == null) return false;
        if (!uri.IsAbsoluteUri) return false;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;

        var host = uri.Host;
        return AllowedDownloadHosts.Any(allowed =>
            host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verify a downloaded file against an expected manifest body.
    /// Throws <see cref="UpdateVerificationException"/> on any mismatch.
    /// Designed to be called immediately after the EXE download completes
    /// and BEFORE the updater script is launched.
    /// </summary>
    public static void VerifyOrThrow(string downloadedFilePath, string manifestBody)
    {
        var expected = ParseManifest(manifestBody);
        var actual   = ComputeSha256(downloadedFilePath);
        if (!HashesMatch(expected, actual))
        {
            throw new UpdateVerificationException(
                "Hash SHA-256 file update yang didownload TIDAK SAMA dengan manifest resmi. " +
                "Update dibatalkan untuk keamanan. " +
                $"Diharapkan: {expected}, " +
                $"Didapat:    {actual}.");
        }
    }
}

/// <summary>
/// Thrown when an update payload fails its security verification.
/// The caller MUST treat this as fatal — delete the downloaded file and
/// surface a clear error to the user without offering a "retry / ignore"
/// option.
/// </summary>
public sealed class UpdateVerificationException : Exception
{
    public UpdateVerificationException(string message) : base(message) { }
    public UpdateVerificationException(string message, Exception inner) : base(message, inner) { }
}
