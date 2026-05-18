using System;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PanelCalculator.Data.Security;

[SupportedOSPlatform("windows")]

/// <summary>
/// Derives a stable, machine-bound 32-byte key used as the SQLCipher passphrase
/// for the application database.
///
/// Inputs combined and hashed (SHA-256):
///   - Win32_BaseBoard.SerialNumber          (motherboard serial)
///   - Win32_Processor.ProcessorId           (CPU id)
///   - HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid
///   - Hardcoded pepper (lightly obfuscated; proper code-level obfuscation comes later)
///
/// If a primary identifier is missing or empty, a placeholder constant is used so
/// the derivation never throws and stays deterministic on the same machine.
/// The resulting key is returned as a lowercase hex string of 64 characters.
/// </summary>
public static class MachineKeyProvider
{
    // Cached result — WMI queries are slow (~100ms first call) and the value
    // never changes during process lifetime.
    private static string? _cachedKey;
    private static readonly object _gate = new();

    /// <summary>
    /// Returns the 64-char lowercase hex SQLCipher key for the current machine.
    /// Thread-safe and idempotent.
    /// </summary>
    public static string GetKey()
    {
        if (_cachedKey != null) return _cachedKey;
        lock (_gate)
        {
            if (_cachedKey != null) return _cachedKey;

            var board   = SafeWmi("SELECT SerialNumber FROM Win32_BaseBoard",   "SerialNumber",  "BOARD-MISSING");
            var cpu     = SafeWmi("SELECT ProcessorId  FROM Win32_Processor",  "ProcessorId",   "CPU-MISSING");
            var machineGuid = SafeRegistry(
                @"SOFTWARE\Microsoft\Cryptography",
                "MachineGuid",
                "GUID-MISSING");

            var pepper = GetPepper();

            using var sha = SHA256.Create();
            var material = string.Join("|", new[]
            {
                "PanelCalculator.v1",
                board.Trim(),
                cpu.Trim(),
                machineGuid.Trim(),
                pepper
            });
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
            _cachedKey = Convert.ToHexString(hash).ToLowerInvariant();
            return _cachedKey;
        }
    }

    /// <summary>
    /// Reset cache. Test-only — production should never need this.
    /// </summary>
    internal static void ResetCache()
    {
        lock (_gate) { _cachedKey = null; }
    }

    /// <summary>
    /// Hardware fingerprint bytes used for license binding (8 bytes / 64 bits).
    /// Derived from the same inputs as <see cref="GetKey"/> (motherboard +
    /// CPU + MachineGuid + app pepper), taking the leading 8 bytes of the
    /// SHA-256 hash. Identical bytes produce identical hex display, so the
    /// license generator and the running app stay perfectly in sync.
    /// </summary>
    public static byte[] GetHardwareFingerprintBytes()
    {
        var hex = GetKey(); // 64-char lowercase hex of SHA-256
        var bytes = Convert.FromHexString(hex);
        var fp = new byte[8];
        Array.Copy(bytes, 0, fp, 0, 8);
        return fp;
    }

    /// <summary>
    /// Customer-display version of the hardware fingerprint:
    /// 16 hex characters in 4 groups of 4 separated by dashes,
    /// e.g. "A3F7-9C2B-1E4D-8F60". Short, dictate-able over WhatsApp / phone.
    /// </summary>
    public static string GetHardwareFingerprintDisplay()
    {
        var fp = GetHardwareFingerprintBytes();
        var hex = Convert.ToHexString(fp).ToUpperInvariant();
        return $"{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}-{hex.Substring(12, 4)}";
    }

    /// <summary>
    /// Parses a customer-display fingerprint (e.g. "A3F7-9C2B-1E4D-8F60",
    /// case-insensitive, dashes optional) back into the 8 raw bytes.
    /// Throws <see cref="FormatException"/> if the input isn't 16 hex chars.
    /// Used by the license generator tool when the admin pastes the
    /// customer's fingerprint.
    /// </summary>
    public static byte[] ParseHardwareFingerprintDisplay(string display)
    {
        if (display == null) throw new ArgumentNullException(nameof(display));
        var hex = display.Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
        if (hex.Length != 16)
            throw new FormatException($"Hardware fingerprint must be 16 hex characters (got {hex.Length}).");
        return Convert.FromHexString(hex);
    }

    // ────────────────────────────────────────────────────────────────────────
    // WMI / Registry helpers — all swallow errors and return the fallback so
    // a half-broken machine still produces a deterministic key on that box.
    // ────────────────────────────────────────────────────────────────────────

    private static string SafeWmi(string query, string property, string fallback)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var value = obj[property]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value) &&
                        !value.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase) &&
                        !value.Equals("Default string", StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }
        }
        catch
        {
            // ignore — fall through to fallback
        }
        return fallback;
    }

    private static string SafeRegistry(string keyPath, string valueName, string fallback)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                       .OpenSubKey(keyPath);
            var value = key?.GetValue(valueName)?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        catch
        {
            // ignore
        }
        return fallback;
    }

    /// <summary>
    /// Returns a constant pepper string. The bytes are XOR-obfuscated to keep
    /// the literal out of the compiled string-table; this is a *tiny* speed
    /// bump for casual reverse-engineering. Proper obfuscation (Obfuscar
    /// HideStrings or string-encryption tool) is tracked as a separate item.
    /// </summary>
    private static string GetPepper()
    {
        // XOR mask — single byte 0x5A applied to each pepper byte.
        // Original cleartext: "TTS-PanelCalc-pepper-2026-v1" (28 chars)
        byte[] obf = new byte[]
        {
            0x0E, 0x0E, 0x09, 0x77, 0x0A, 0x3B, 0x34, 0x3F,
            0x36, 0x19, 0x3B, 0x36, 0x39, 0x77, 0x2A, 0x3F,
            0x2A, 0x2A, 0x3F, 0x28, 0x77, 0x68, 0x6A, 0x68,
            0x6C, 0x77, 0x2C, 0x6B
        };
        var sb = new StringBuilder(obf.Length);
        for (int i = 0; i < obf.Length; i++)
        {
            sb.Append((char)(obf[i] ^ 0x5A));
        }
        return sb.ToString();
    }
}
