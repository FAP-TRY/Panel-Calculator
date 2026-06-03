using System;
using System.IO;
using System.Text;
using RabKit.Branding;

namespace PanelCalculator.Core.Security;

/// <summary>
/// Binary encoding for the license payload that travels between the issuer
/// and the customer machine.
///
/// <para>
/// Two on-wire layouts coexist for backward compatibility with existing
/// PT TTS installs (every byte of V1 still decodes):
/// </para>
///
/// <para>
/// <strong>V1 (legacy — pre-v1.3.0, still issued OK by old keygen):</strong>
/// <code>
///   byte[0]              = format version = 1
///   byte[1..8]           = hardware fingerprint (8 bytes)
///   byte[9..16]          = issue date, unix seconds (little-endian Int64)
///   byte[17]             = customer name length N (1 byte, max 255)
///   byte[18..18+N]       = customer name (UTF-8)
///   byte[18+N..18+N+64]  = Ed25519 signature over bytes [0..18+N]
/// </code>
/// </para>
///
/// <para>
/// <strong>V2 (introduced in v1.3.0 for multi-edition support):</strong>
/// adds an edition+tier+industry+expiry+features tail BEFORE the signature.
/// <code>
///   byte[0]              = format version = 2
///   byte[1..8]           = hardware fingerprint (8 bytes)
///   byte[9..16]          = issue date, unix seconds (little-endian Int64)
///   byte[17]             = customer name length N (1 byte)
///   byte[18..18+N]       = customer name (UTF-8)
///   byte[18+N]           = editionId length E (1 byte)
///   byte[19+N..]         = editionId (UTF-8)
///   byte[..]             = tier length T (1 byte)  + tier UTF-8
///   byte[..]             = industry length I (1 byte) + industry UTF-8
///   byte[..]             = expiresAt UTC ticks (little-endian Int64; 0 = never)
///   byte[..]             = features bitmask (1 byte; see bit layout below)
///   byte[..+64]          = Ed25519 signature over preceding bytes
/// </code>
/// Features bitmask:
/// <list type="bullet">
///   <item>bit 0: WatermarkOutput</item>
///   <item>bit 1: AllowBrandingOverride</item>
///   <item>bit 2: AllowMarketplacePacks</item>
///   <item>bit 3: RequiresOnlineActivation</item>
///   <item>bit 4-7: reserved (must be 0)</item>
/// </list>
/// </para>
///
/// <para>
/// The whole blob is then Base32-encoded (Crockford-style alphabet — no
/// I/L/O/U to avoid ambiguous chars when dictated) and grouped 5 chars per
/// block, separated by dashes for easy reading over WhatsApp / phone.
/// </para>
///
/// <para>
/// The decoder is tolerant: it strips dashes, whitespace, and is case-
/// insensitive, so customers can type / paste however they like. Decoder
/// auto-detects V1 vs V2 via byte[0], and existing V1 licenses keep
/// decoding identically — only the new <see cref="DecodedLicense.Version"/>
/// reflects what was on the wire.
/// </para>
/// </summary>
internal static class LicensePayload
{
    /// <summary>Legacy V1 payload version (single-tenant TTS licenses).</summary>
    internal const byte FormatVersionV1 = 1;

    /// <summary>V2 payload — adds edition/tier/industry/expiry/features tail.</summary>
    internal const byte FormatVersionV2 = 2;

    /// <summary>
    /// Currently-emitted format version. New <see cref="BuildSignablePayload(byte[], string, DateTime)"/>
    /// calls (the V1-arity overload) keep using V1 for byte-identical
    /// backward compatibility with the existing keygen tooling. The new
    /// V2 overload emits V2.
    /// </summary>
    internal const byte FormatVersion = FormatVersionV1;

    /// <summary>Length of Ed25519 signature in bytes.</summary>
    internal const int SignatureLength = 64;

    /// <summary>Length of hardware fingerprint in bytes (matches MachineKeyProvider).</summary>
    internal const int FingerprintLength = 8;

    // Crockford-style Base32 alphabet — 32 chars, NO I L O U.
    // Picked over RFC 4648 specifically because customers will dictate these
    // over WhatsApp; "I" vs "1" and "O" vs "0" cause endless support calls.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    // ─────────────────────────────────────────────────────────────────────
    //  V1 builder (LEGACY — keep byte-identical with v1.2.x keygen tooling)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the raw V1 payload bytes (without signature) that the issuer
    /// will Ed25519-sign. Splitting this out lets both the generator and the
    /// validator regenerate the exact same bytes from the same inputs.
    ///
    /// <para>
    /// <strong>Backward compat anchor</strong> — every byte this overload
    /// emits MUST stay identical with the v1.2.x implementation. PT TTS
    /// customers whose existing license was issued by the legacy tool
    /// re-derive the same bytes here on every startup; any drift would
    /// reject every existing license.
    /// </para>
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
        ms.WriteByte(FormatVersionV1);
        ms.Write(fingerprint, 0, FingerprintLength);
        Span<byte> dateBuf = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(dateBuf, issueUnix);
        ms.Write(dateBuf);
        ms.WriteByte((byte)nameBytes.Length);
        ms.Write(nameBytes, 0, nameBytes.Length);
        return ms.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  V2 builder (new — edition/tier/industry/expiry/features tail)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the raw V2 payload bytes — extends V1 with editionId, tier,
    /// industry, optional expiry, and features bitmask. Used by the v1.3.0+
    /// keygen tool when the admin issues a license for a specific edition
    /// (Custom, Generic-Basic, Generic-Pro, Lifetime, etc.).
    /// </summary>
    /// <param name="fingerprint">8-byte hardware fingerprint (see <see cref="FingerprintLength"/>).</param>
    /// <param name="customerName">Customer name, UTF-8 length ≤255.</param>
    /// <param name="issueDateUtc">Issue date (truncated to whole seconds).</param>
    /// <param name="editionId">Edition identifier (e.g. "custom-tts-panel-v1"). Required, length ≤255.</param>
    /// <param name="tier">Tier name (e.g. "custom", "generic-basic"). Length ≤255.</param>
    /// <param name="industry">Industry slug (e.g. "panel-electrical"). Length ≤255.</param>
    /// <param name="expiresAtUtc">Optional expiry; null = never expires (lifetime / perpetual).</param>
    /// <param name="features">Feature flags packed into bitmask byte.</param>
    internal static byte[] BuildSignablePayload(
        byte[] fingerprint,
        string customerName,
        DateTime issueDateUtc,
        string editionId,
        string tier,
        string industry,
        DateTime? expiresAtUtc,
        EditionFeatures features)
    {
        if (fingerprint == null || fingerprint.Length != FingerprintLength)
            throw new ArgumentException($"fingerprint must be {FingerprintLength} bytes", nameof(fingerprint));
        if (customerName == null) throw new ArgumentNullException(nameof(customerName));
        if (editionId == null) throw new ArgumentNullException(nameof(editionId));
        if (tier == null) throw new ArgumentNullException(nameof(tier));
        if (industry == null) throw new ArgumentNullException(nameof(industry));
        if (features == null) throw new ArgumentNullException(nameof(features));

        var nameBytes     = Encoding.UTF8.GetBytes(customerName);
        var editionBytes  = Encoding.UTF8.GetBytes(editionId);
        var tierBytes     = Encoding.UTF8.GetBytes(tier);
        var industryBytes = Encoding.UTF8.GetBytes(industry);

        if (nameBytes.Length > 255)     throw new ArgumentException("customerName too long (max 255 UTF-8 bytes)", nameof(customerName));
        if (editionBytes.Length > 255)  throw new ArgumentException("editionId too long (max 255 UTF-8 bytes)", nameof(editionId));
        if (tierBytes.Length > 255)     throw new ArgumentException("tier too long (max 255 UTF-8 bytes)", nameof(tier));
        if (industryBytes.Length > 255) throw new ArgumentException("industry too long (max 255 UTF-8 bytes)", nameof(industry));

        var issueUnix = new DateTimeOffset(
            DateTime.SpecifyKind(issueDateUtc, DateTimeKind.Utc)
        ).ToUnixTimeSeconds();

        long expiresTicks = 0;
        if (expiresAtUtc.HasValue)
        {
            expiresTicks = DateTime.SpecifyKind(expiresAtUtc.Value, DateTimeKind.Utc).Ticks;
            if (expiresTicks == 0)
                throw new ArgumentException("expiresAt cannot be DateTime.MinValue (reserved for 'never')", nameof(expiresAtUtc));
        }

        byte featuresBits = PackFeaturesBitmask(features);

        using var ms = new MemoryStream();
        ms.WriteByte(FormatVersionV2);
        ms.Write(fingerprint, 0, FingerprintLength);

        Span<byte> dateBuf = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(dateBuf, issueUnix);
        ms.Write(dateBuf);

        ms.WriteByte((byte)nameBytes.Length);
        ms.Write(nameBytes, 0, nameBytes.Length);

        ms.WriteByte((byte)editionBytes.Length);
        ms.Write(editionBytes, 0, editionBytes.Length);

        ms.WriteByte((byte)tierBytes.Length);
        ms.Write(tierBytes, 0, tierBytes.Length);

        ms.WriteByte((byte)industryBytes.Length);
        ms.Write(industryBytes, 0, industryBytes.Length);

        Span<byte> expiresBuf = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(expiresBuf, expiresTicks);
        ms.Write(expiresBuf);

        ms.WriteByte(featuresBits);

        return ms.ToArray();
    }

    /// <summary>Packs <see cref="EditionFeatures"/> bool flags into a single byte.</summary>
    internal static byte PackFeaturesBitmask(EditionFeatures features)
    {
        byte b = 0;
        if (features.WatermarkOutput)         b |= 0b0000_0001;
        if (features.AllowBrandingOverride)   b |= 0b0000_0010;
        if (features.AllowMarketplacePacks)   b |= 0b0000_0100;
        if (features.RequiresOnlineActivation) b |= 0b0000_1000;
        // bits 4-7 reserved
        return b;
    }

    /// <summary>
    /// Reverse of <see cref="PackFeaturesBitmask"/>. Counts are NOT in the
    /// bitmask (they're carried separately in the manifest for V1 fallback
    /// callers) — V2 license features are bool flags only.
    /// </summary>
    internal static EditionFeatures UnpackFeaturesBitmask(byte b)
    {
        return new EditionFeatures(
            WatermarkOutput:          (b & 0b0000_0001) != 0,
            AllowBrandingOverride:    (b & 0b0000_0010) != 0,
            AllowMarketplacePacks:    (b & 0b0000_0100) != 0,
            MaxEstimationsPerMonth:   0,    // not encoded in bitmask
            MaxConcurrentSeats:       1,    // not encoded in bitmask
            RequiresOnlineActivation: (b & 0b0000_1000) != 0);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Encode / Decode
    // ─────────────────────────────────────────────────────────────────────

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
    /// <see cref="FormatException"/> on malformed input. Auto-detects V1 vs V2
    /// via the first byte.
    /// </summary>
    internal static DecodedLicense Decode(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
            throw new FormatException("License key is empty.");

        var stripped = StripFormatting(licenseKey);
        var bytes = FromBase32(stripped);

        // Minimum V1 size: 1 (version) + 8 (fp) + 8 (date) + 1 (name length) + 0 + 64 (sig) = 82
        const int minSizeV1 = 1 + FingerprintLength + 8 + 1 + 0 + SignatureLength;
        if (bytes.Length < minSizeV1)
            throw new FormatException($"License too short (need at least {minSizeV1} bytes, got {bytes.Length}).");

        int offset = 0;
        byte version = bytes[offset++];

        if (version == FormatVersionV1)
            return DecodeV1(bytes, offset);

        if (version == FormatVersionV2)
            return DecodeV2(bytes, offset);

        throw new FormatException(
            $"Unsupported license format version: {version}. " +
            $"This build supports V{FormatVersionV1} and V{FormatVersionV2}.");
    }

    private static DecodedLicense DecodeV1(byte[] bytes, int offset)
    {
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

        // V1 has no edition/tier/industry — caller (LicenseService) applies
        // fallback values from EditionManifest.
        return new DecodedLicense(
            payload,
            signature,
            FormatVersionV1,
            fingerprint,
            customerName,
            DateTimeOffset.FromUnixTimeSeconds(issueUnix).UtcDateTime,
            EditionId: "",
            Tier: "",
            Industry: "",
            ExpiresAtUtc: null,
            Features: new EditionFeatures());
    }

    private static DecodedLicense DecodeV2(byte[] bytes, int offset)
    {
        // V2 minimum after the version byte:
        //   8 (fp) + 8 (date) + 1 (nameLen) + 0 + 1 (editionIdLen) + 0
        //   + 1 (tierLen) + 0 + 1 (industryLen) + 0 + 8 (expires) + 1 (features) + 64 (sig)
        // = 93 bytes total minimum (with all variable-length fields empty)
        const int minSizeV2 = 1 + FingerprintLength + 8 + 1 + 1 + 1 + 1 + 8 + 1 + SignatureLength;
        if (bytes.Length < minSizeV2)
            throw new FormatException($"V2 license too short (need at least {minSizeV2} bytes, got {bytes.Length}).");

        var fingerprint = new byte[FingerprintLength];
        Buffer.BlockCopy(bytes, offset, fingerprint, 0, FingerprintLength);
        offset += FingerprintLength;

        long issueUnix = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
            new ReadOnlySpan<byte>(bytes, offset, 8));
        offset += 8;

        // Helper: read a length-prefixed UTF8 string
        string ReadStr(ref int o, string fieldName)
        {
            if (o >= bytes.Length)
                throw new FormatException($"V2 license: unexpected EOF before {fieldName} length byte.");
            int len = bytes[o++];
            if (o + len > bytes.Length)
                throw new FormatException($"V2 license: {fieldName} length {len} would read past end of payload.");
            string s = Encoding.UTF8.GetString(bytes, o, len);
            o += len;
            return s;
        }

        string customerName = ReadStr(ref offset, "customerName");
        string editionId    = ReadStr(ref offset, "editionId");
        string tier         = ReadStr(ref offset, "tier");
        string industry     = ReadStr(ref offset, "industry");

        if (offset + 8 + 1 + SignatureLength > bytes.Length)
            throw new FormatException("V2 license: not enough bytes left for expires+features+signature.");

        long expiresTicks = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(
            new ReadOnlySpan<byte>(bytes, offset, 8));
        offset += 8;

        byte featuresBits = bytes[offset++];

        if (offset + SignatureLength != bytes.Length)
            throw new FormatException("V2 license payload length mismatch — possible tampering or corruption.");

        var signature = new byte[SignatureLength];
        Buffer.BlockCopy(bytes, offset, signature, 0, SignatureLength);

        int payloadLen = bytes.Length - SignatureLength;
        var payload = new byte[payloadLen];
        Buffer.BlockCopy(bytes, 0, payload, 0, payloadLen);

        DateTime? expiresAt = expiresTicks == 0
            ? null
            : DateTime.SpecifyKind(new DateTime(expiresTicks), DateTimeKind.Utc);

        return new DecodedLicense(
            payload,
            signature,
            FormatVersionV2,
            fingerprint,
            customerName,
            DateTimeOffset.FromUnixTimeSeconds(issueUnix).UtcDateTime,
            EditionId: editionId,
            Tier: tier,
            Industry: industry,
            ExpiresAtUtc: expiresAt,
            Features: UnpackFeaturesBitmask(featuresBits));
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

    /// <summary>
    /// Container returned by <see cref="Decode"/>. V1 licenses leave the
    /// edition/tier/industry/expiry/features fields at their defaults
    /// (empty strings, null, all-false flags); callers must apply manifest
    /// fallback values for V1 to be useful in a v1.3.0+ multi-edition
    /// world.
    /// </summary>
    internal sealed record DecodedLicense(
        byte[]   Payload,
        byte[]   Signature,
        byte     Version,
        byte[]   HardwareFingerprint,
        string   CustomerName,
        DateTime IssueDateUtc,
        string   EditionId,
        string   Tier,
        string   Industry,
        DateTime? ExpiresAtUtc,
        EditionFeatures Features);
}
