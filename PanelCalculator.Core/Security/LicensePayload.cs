using System;
using System.IO;
using System.Text;

namespace PanelCalculator.Core.Security;

/// <summary>
/// Binary encoding for the license payload that travels between PT TTS
/// (the issuer) and the customer machine.
///
/// On-wire layout (little-endian, no padding):
///
///   byte[0]      = format version (currently 1)
///   byte[1..8]   = hardware fingerprint (8 bytes)
///   byte[9..16]  = issue date, unix seconds (8-byte signed long, little-endian)
///   byte[17]     = customer name length N (1 byte, max 255)
///   byte[18..18+N] = customer name (UTF-8)
///   byte[18+N..18+N+64] = Ed25519 signature over bytes [0..18+N]
///
/// The whole blob is then Base32-encoded (Crockford-style alphabet — no I/L/O/U
/// to avoid ambiguous chars when dictated) and grouped 5 chars per block,
/// separated by dashes for easy reading over WhatsApp / phone.
///
/// Example final form (dummy):
///   K7F3M-B9P2X-N4QT8-V5R7W-A2HJD-K3LM7-...
///
/// The decoder is tolerant: it strips dashes, whitespace, and is case-
/// insensitive, so customers can type / paste however they like.
/// </summary>
internal static class LicensePayload
{
    /// <summary>Current payload format version. Bumped if layout changes.</summary>
    internal const byte FormatVersion = 1;

    /// <summary>Length of Ed25519 signature in bytes.</summary>
    internal const int SignatureLength = 64;

    /// <summary>Length of hardware fingerprint in bytes (matches MachineKeyProvider).</summary>
    internal const int FingerprintLength = 8;

    // Crockford-style Base32 alphabet — 32 chars, NO I L O U.
    // Picked over RFC 4648 specifically because customers will dictate these
    // over WhatsApp; "I" vs "1" and "O" vs "0" cause endless support calls.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Builds the raw payload bytes (without signature) that the issuer
    /// will Ed25519-sign. Splitting this out lets both the generator and the
    /// validator regenerate the exact same bytes from the same inputs.
    /// </summary>
    internal static byte[] BuildSignablePayload(
        byte[] fingerprint,
        string customerName,
        DateTime issueDateUtc)
    {
        if (fingerprint == null || fingerprint.Length != FingerprintLength)
            throw new ArgumentException($"fingerprint must be {FingerprintLength} bytes", nameof(fingerprint));
        if (customerName == null) throw new ArgumentNullException(nameof(customerName));

        var nameBytes = Encoding.UTF8.GetBytes(customerName);
        if (nameBytes.Length > 255)
            throw new ArgumentException("customerName too long (max 255 UTF-8 bytes)", nameof(customerName));

        var issueUnix = new DateTimeOffset(
            DateTime.SpecifyKind(issueDateUtc, DateTimeKind.Utc)
        ).ToUnixTimeSeconds();

        using var ms = new MemoryStream();
        ms.WriteByte(FormatVersion);
        ms.Write(fingerprint, 0, FingerprintLength);
        Span<byte> dateBuf = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(dateBuf, issueUnix);
        ms.Write(dateBuf);
        ms.WriteByte((byte)nameBytes.Length);
        ms.Write(nameBytes, 0, nameBytes.Length);
        return ms.ToArray();
    }

    /// <summary>
    /// Combines payload + signature into the final user-facing license key string
    /// (Base32, grouped 5 chars per block, dash-separated).
    /// </summary>
    internal static string Encode(byte[] payload, byte[] signature)
    {
        if (signature == null || signature.Length != SignatureLength)
            throw new ArgumentException($"signature must be {SignatureLength} bytes", nameof(signature));

        var combined = new byte[payload.Length + signature.Length];
        Buffer.BlockCopy(payload, 0,    combined, 0,              payload.Length);
        Buffer.BlockCopy(signature, 0, combined, payload.Length, signature.Length);
        var raw = ToBase32(combined);
        return Group(raw, 5, '-');
    }

    /// <summary>
    /// Reverse of <see cref="Encode"/>. Returns the parsed structure or throws
    /// <see cref="FormatException"/> on malformed input.
    /// </summary>
    internal static DecodedLicense Decode(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            throw new FormatException("License key is empty.");

        var stripped = StripFormatting(licenseKey);
        var bytes = FromBase32(stripped);

        // Minimum size: 1 (version) + 8 (fp) + 8 (date) + 1 (name length) + 0 + 64 (sig) = 82
        const int minSize = 1 + FingerprintLength + 8 + 1 + 0 + SignatureLength;
        if (bytes.Length < minSize)
            throw new FormatException($"License too short (need at least {minSize} bytes, got {bytes.Length}).");

        int offset = 0;
        byte version = bytes[offset++];
        if (version != FormatVersion)
            throw new FormatException($"Unsupported license format version: {version}.");

        var fingerprint = new byte[FingerprintLength];
        Buffer.BlockCopy(bytes, offset, fingerprint, 0, FingerprintLength);
        offset += FingerprintLength;

        long issueUnix = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
            new ReadOnlySpan<byte>(bytes, offset, 8));
        offset += 8;

        int nameLen = bytes[offset++];
        if (offset + nameLen + SignatureLength != bytes.Length)
            throw new FormatException("License payload length mismatch — possible tampering or corruption.");

        string customerName = Encoding.UTF8.GetString(bytes, offset, nameLen);
        offset += nameLen;

        var signature = new byte[SignatureLength];
        Buffer.BlockCopy(bytes, offset, signature, 0, SignatureLength);

        int payloadLen = bytes.Length - SignatureLength;
        var payload = new byte[payloadLen];
        Buffer.BlockCopy(bytes, 0, payload, 0, payloadLen);

        return new DecodedLicense(
            payload,
            signature,
            version,
            fingerprint,
            customerName,
            DateTimeOffset.FromUnixTimeSeconds(issueUnix).UtcDateTime);
    }

    /// <summary>
    /// Inserts <paramref name="sep"/> every <paramref name="groupSize"/> chars.
    /// </summary>
    internal static string Group(string input, int groupSize, char sep)
    {
        if (groupSize <= 0) return input;
        var sb = new StringBuilder(input.Length + input.Length / groupSize);
        for (int i = 0; i < input.Length; i++)
        {
            if (i > 0 && i % groupSize == 0) sb.Append(sep);
            sb.Append(input[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Removes whitespace, dashes, and underscores, and uppercases what's left.
    /// Customer-friendly: they can paste with any formatting.
    /// </summary>
    internal static string StripFormatting(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '-' || c == '_' || char.IsWhiteSpace(c)) continue;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    // ───────────────────────── Base32 (Crockford-ish, no padding) ─────────────────────

    internal static string ToBase32(byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                int idx = (buffer >> (bitsLeft - 5)) & 0x1F;
                bitsLeft -= 5;
                sb.Append(Alphabet[idx]);
            }
        }
        if (bitsLeft > 0)
        {
            int idx = (buffer << (5 - bitsLeft)) & 0x1F;
            sb.Append(Alphabet[idx]);
        }
        return sb.ToString();
    }

    internal static byte[] FromBase32(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();

        // Build inverse lookup once per call (tiny constant cost; alphabet is 32 chars).
        Span<int> map = stackalloc int[128];
        for (int i = 0; i < 128; i++) map[i] = -1;
        for (int i = 0; i < Alphabet.Length; i++) map[Alphabet[i]] = i;

        var output = new MemoryStream((s.Length * 5) / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var rawC in s)
        {
            var c = char.ToUpperInvariant(rawC);
            if (c >= 128 || map[c] < 0)
                throw new FormatException($"Invalid base32 character: '{rawC}'.");
            buffer = (buffer << 5) | map[c];
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                int b = (buffer >> (bitsLeft - 8)) & 0xFF;
                output.WriteByte((byte)b);
                bitsLeft -= 8;
            }
        }
        // Trailing < 8 bits are padding, discard.
        return output.ToArray();
    }

    /// <summary>Container returned by <see cref="Decode"/>.</summary>
    internal sealed record DecodedLicense(
        byte[]   Payload,
        byte[]   Signature,
        byte     Version,
        byte[]   HardwareFingerprint,
        string   CustomerName,
        DateTime IssueDateUtc);
}
