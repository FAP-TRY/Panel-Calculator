using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using PanelCalculator.WinForms.Services;
using Xunit;

namespace PanelCalculator.Tests.GoldenMaster;

/// <summary>
/// Golden master regression suite for the PDF / Word / Excel quotation
/// renderers. For each of the five fixed samples the suite generates the
/// artifact in a temp file, extracts a normalized text fingerprint
/// (text-only, no metadata timestamps), SHA-256 hashes the fingerprint,
/// and compares it to the baseline stored in
/// <c>docs/golden-master-hashes-v1.2.9.json</c>.
///
/// <para>
/// The strategy intentionally skips binary-byte hashing because the
/// underlying libraries (iText7, DocX, ClosedXML) embed wall-clock
/// timestamps into the PDF info dictionary / DOCX docProps / XLSX core
/// properties — those make naive hashing flaky. The text-only fingerprint
/// captures every value the renderer actually puts on the page (numbers,
/// labels, signer name, section dividers) which is what matters for the
/// Week-1 brand-abstraction refactor.
/// </para>
///
/// <para>
/// Baseline regeneration: run the suite once with the environment variable
/// <c>GOLDEN_MASTER_REGENERATE=1</c> set. The first run with the variable
/// will overwrite the JSON baseline; subsequent runs without the variable
/// will assert the recorded hashes match.
/// </para>
/// </summary>
[Trait("Category", "GoldenMaster")]
public class GoldenMasterTest
{
    private static readonly Dictionary<string, string> EmptySettings = new();

    /// <summary>True when the suite should overwrite the baseline file
    /// instead of asserting hashes match. Wired through the
    /// <c>GOLDEN_MASTER_REGENERATE</c> env var.</summary>
    private static bool IsRegenerateMode =>
        string.Equals(Environment.GetEnvironmentVariable("GOLDEN_MASTER_REGENERATE"),
            "1", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Sample1_SingleSimple_RoundTripsAllThreeFormats()
        => AssertSingle(GoldenMasterSamples.Sample1_SingleSimple());

    [Fact]
    public void Sample2_MultiPanelMedium_RoundTripsAllThreeFormats()
        => AssertCombined(GoldenMasterSamples.Sample2_MultiPanelMedium());

    [Fact]
    public void Sample3_ComplexDiscount_RoundTripsAllThreeFormats()
        => AssertSingle(GoldenMasterSamples.Sample3_ComplexDiscount());

    [Fact]
    public void Sample4_LargeMultiSection_RoundTripsAllThreeFormats()
        => AssertCombined(GoldenMasterSamples.Sample4_LargeMultiSection());

    [Fact]
    public void Sample5_SimpleZeroShipping_RoundTripsAllThreeFormats()
        => AssertSingle(GoldenMasterSamples.Sample5_SimpleZeroShipping());

    // ══════════════════════════════════════════════════════════════════════
    //  ASSERTION DRIVERS
    // ══════════════════════════════════════════════════════════════════════

    private static void AssertSingle(GoldenMasterSamples.SinglePanelSample s)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"gm_{s.Name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        var pdfPath  = Path.Combine(tmpDir, $"{s.Name}.pdf");
        var docxPath = Path.Combine(tmpDir, $"{s.Name}.docx");
        var xlsxPath = Path.Combine(tmpDir, $"{s.Name}.xlsx");

        try
        {
            // ── PDF ──
            PdfLetterExport.Generate(
                pdfPath, s.EstimationNumber, s.ClientName, s.ContactPhone,
                s.Company, s.Address, s.Perihal,
                GoldenMasterSamples.FixedCreatedDate, s.Notes,
                GoldenMasterSamples.ToPdfItems(s.Items),
                s.Subtotal, s.Margin1Percent, s.Margin2Percent, s.Margin3Percent,
                s.MarginAmount, s.ShippingCost,
                s.TaxPercent, s.TaxAmount, s.PphPercent, s.PphAmount, s.Total,
                EmptySettings);

            // ── Word ──
            WordLetterExport.Generate(
                docxPath, s.EstimationNumber, s.ClientName, s.ContactPhone,
                s.Company, s.Address, s.Perihal,
                GoldenMasterSamples.FixedCreatedDate, s.Notes,
                GoldenMasterSamples.ToWordItems(s.Items),
                s.Subtotal, s.MarginAmount, s.ShippingCost,
                s.TaxPercent, s.TaxAmount, s.PphPercent, s.PphAmount, s.Total,
                EmptySettings);

            // ── Excel ──
            ExcelLetterExport.Generate(
                xlsxPath, s.EstimationNumber, s.ClientName, s.ContactPhone,
                s.Company, s.Address, s.Perihal,
                GoldenMasterSamples.FixedCreatedDate, s.Notes,
                GoldenMasterSamples.ToExcelItems(s.Items),
                s.Subtotal, s.MarginAmount, s.ShippingCost,
                s.TaxPercent, s.TaxAmount, s.PphPercent, s.PphAmount, s.Total,
                EmptySettings);

            CompareWithBaseline(s.Name, pdfPath, docxPath, xlsxPath);
        }
        finally
        {
            TryCleanupDir(tmpDir);
        }
    }

    private static void AssertCombined(GoldenMasterSamples.MultiPanelSample s)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"gm_{s.Name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        var pdfPath  = Path.Combine(tmpDir, $"{s.Name}.pdf");
        var docxPath = Path.Combine(tmpDir, $"{s.Name}.docx");
        var xlsxPath = Path.Combine(tmpDir, $"{s.Name}.xlsx");

        try
        {
            var inputs = GoldenMasterSamples.ToCombinedExportInputs(s);

            PdfLetterExport.GenerateCombined(
                pdfPath, s.NomorSurat, s.ClientName, s.ContactPhone,
                s.Company, s.Address, s.Perihal,
                GoldenMasterSamples.FixedCreatedDate, s.Notes,
                inputs.pdf, inputs.summary, EmptySettings);

            WordLetterExport.GenerateCombined(
                docxPath, s.NomorSurat, s.ClientName, s.ContactPhone,
                s.Company, s.Address, s.Perihal,
                GoldenMasterSamples.FixedCreatedDate, s.Notes,
                inputs.word, inputs.summary, EmptySettings);

            ExcelLetterExport.GenerateCombined(
                xlsxPath, s.NomorSurat, s.ClientName, s.ContactPhone,
                s.Company, s.Address, s.Perihal,
                GoldenMasterSamples.FixedCreatedDate, s.Notes,
                inputs.excel, inputs.summary, EmptySettings);

            CompareWithBaseline(s.Name, pdfPath, docxPath, xlsxPath);
        }
        finally
        {
            TryCleanupDir(tmpDir);
        }
    }

    /// <summary>
    /// Hashes the three artifacts and either rewrites the baseline (regenerate
    /// mode) or asserts every hash matches. Diagnostic on failure includes
    /// sample name, format, expected hash, and actual hash so the dev can
    /// inspect manually which renderer drifted.
    /// </summary>
    private static void CompareWithBaseline(string sampleName,
        string pdfPath, string docxPath, string xlsxPath)
    {
        var pdfHash  = Sha256Hex(NormalizePdfText(pdfPath));
        var docxHash = Sha256Hex(NormalizeDocxText(docxPath));
        var xlsxHash = Sha256Hex(NormalizeXlsxText(xlsxPath));

        var baseline = LoadOrCreateBaseline();
        if (IsRegenerateMode)
        {
            baseline.samples[sampleName] = new BaselineEntry
            {
                pdfsha256  = pdfHash,
                docxsha256 = docxHash,
                xlsxsha256 = xlsxHash,
            };
            SaveBaseline(baseline);
            return;
        }

        // ── Strict-compare mode ──
        if (!baseline.samples.TryGetValue(sampleName, out var expected))
        {
            // First-time run: also write the baseline so subsequent runs
            // assert. This makes adding a new sample painless.
            baseline.samples[sampleName] = new BaselineEntry
            {
                pdfsha256  = pdfHash,
                docxsha256 = docxHash,
                xlsxsha256 = xlsxHash,
            };
            SaveBaseline(baseline);
            return;
        }

        AssertHash(sampleName, "pdf",  expected.pdfsha256,  pdfHash);
        AssertHash(sampleName, "docx", expected.docxsha256, docxHash);
        AssertHash(sampleName, "xlsx", expected.xlsxsha256, xlsxHash);
    }

    private static void AssertHash(string sample, string format,
        string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new Xunit.Sdk.XunitException(
                $"[golden-master] Mismatch on sample '{sample}' format '{format}'.\n" +
                $"  expected: {expected}\n" +
                $"  actual:   {actual}\n" +
                $"To refresh the baseline (intentional change), re-run the suite " +
                $"with GOLDEN_MASTER_REGENERATE=1.");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TEXT-EXTRACTION HELPERS — stable across timestamp drift
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extract page text from the PDF (iText PdfTextExtractor), then
    /// normalize whitespace so iText layout micro-differences don't break
    /// the hash. Returns the concatenated text of every page, separated by
    /// a page marker.
    /// </summary>
    private static string NormalizePdfText(string pdfPath)
    {
        var sb = new StringBuilder();
        using var reader = new PdfReader(pdfPath);
        using var pdf    = new PdfDocument(reader);
        int pages = pdf.GetNumberOfPages();
        for (int p = 1; p <= pages; p++)
        {
            sb.Append("===PAGE ").Append(p).Append("===\n");
            var text = PdfTextExtractor.GetTextFromPage(pdf.GetPage(p));
            sb.Append(NormalizeWhitespace(text)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Extract every <c>&lt;w:t&gt;</c> text node from the document.xml
    /// inside a DOCX zip. Header text (letterhead image labels) lives
    /// in header*.xml entries so we include those too. The regex accepts
    /// an optional namespace prefix to stay robust against future DocX
    /// library changes.
    /// </summary>
    private static string NormalizeDocxText(string docxPath)
    {
        var sb = new StringBuilder();
        using var archive = ZipFile.OpenRead(docxPath);
        // Sort entries for stability across DocX library versions
        var entries = archive.Entries
            .Where(e => e.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
        foreach (var e in entries)
        {
            sb.Append("===").Append(e.FullName).Append("===\n");
            using var sr = new StreamReader(e.Open(), Encoding.UTF8);
            var xml = sr.ReadToEnd();
            foreach (Match m in Regex.Matches(xml, @"<(?:\w+:)?t(?:\s[^>]*)?>([^<]*)</(?:\w+:)?t>"))
                sb.Append(m.Groups[1].Value).Append('\n');
        }
        return NormalizeWhitespace(sb.ToString());
    }

    /// <summary>
    /// Extract sharedStrings.xml + every sheet*.xml cell value/inline
    /// string from an XLSX zip. Cell values (&lt;v&gt; nodes) and
    /// shared string entries (&lt;si&gt;&lt;t&gt; nodes) give us every
    /// number and label the workbook renders.
    ///
    /// <para>
    /// ClosedXML emits its OOXML with the <c>x:</c> namespace prefix
    /// (e.g. <c>&lt;x:t&gt;...&lt;/x:t&gt;</c>) rather than the bare
    /// element name, so the regexes accept an optional <c>(?:\w+:)?</c>
    /// prefix on every interesting element.
    /// </para>
    /// </summary>
    private static string NormalizeXlsxText(string xlsxPath)
    {
        var sb = new StringBuilder();
        using var archive = ZipFile.OpenRead(xlsxPath);
        var entries = archive.Entries
            .Where(e =>
                e.FullName.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();
        foreach (var e in entries)
        {
            sb.Append("===").Append(e.FullName).Append("===\n");
            using var sr = new StreamReader(e.Open(), Encoding.UTF8);
            var xml = sr.ReadToEnd();
            foreach (Match m in Regex.Matches(xml, @"<(?:\w+:)?t(?:\s[^>]*)?>([^<]*)</(?:\w+:)?t>"))
                sb.Append("T:").Append(m.Groups[1].Value).Append('\n');
            foreach (Match m in Regex.Matches(xml, @"<(?:\w+:)?v(?:\s[^>]*)?>([^<]*)</(?:\w+:)?v>"))
                sb.Append("V:").Append(m.Groups[1].Value).Append('\n');
            foreach (Match m in Regex.Matches(xml, @"<(?:\w+:)?f(?:\s[^>]*)?>([^<]*)</(?:\w+:)?f>"))
                sb.Append("F:").Append(m.Groups[1].Value).Append('\n');
        }
        return NormalizeWhitespace(sb.ToString());
    }

    private static string NormalizeWhitespace(string s)
    {
        // Collapse all runs of whitespace into a single space.
        // Trim leading/trailing whitespace per line so DocX/iText layout
        // micro-spacing doesn't affect the hash.
        var lines = s.Split('\n', StringSplitOptions.None)
            .Select(l => Regex.Replace(l, @"\s+", " ").Trim())
            .Where(l => l.Length > 0);
        return string.Join("\n", lines);
    }

    private static string Sha256Hex(string s)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  BASELINE FILE I/O
    // ══════════════════════════════════════════════════════════════════════

    private sealed class BaselineEntry
    {
        // Lowercase property names to match the on-disk JSON keys
        // ("pdf-sha256" / "docx-sha256" / "xlsx-sha256" rendered via
        // JsonPropertyName below).
        public string pdfsha256  { get; set; } = "";
        public string docxsha256 { get; set; } = "";
        public string xlsxsha256 { get; set; } = "";
    }

    private sealed class Baseline
    {
        public Dictionary<string, BaselineEntry> samples { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string BaselinePath()
    {
        // Walk up from the test assembly dir until we find the solution
        // root (where PanelCalculator.sln lives).
        var dir = Path.GetDirectoryName(typeof(GoldenMasterTest).Assembly.Location)!;
        while (dir != null && !File.Exists(Path.Combine(dir, "PanelCalculator.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            throw new InvalidOperationException(
                "Could not locate PanelCalculator.sln from test assembly directory.");
        return Path.Combine(dir, "docs", "golden-master-hashes-v1.2.9.json");
    }

    private static Baseline LoadOrCreateBaseline()
    {
        var path = BaselinePath();
        if (!File.Exists(path))
            return new Baseline();
        try
        {
            var raw = File.ReadAllText(path, Encoding.UTF8);
            // Parse the on-disk schema {"samples": {key: {"pdf-sha256":..., "docx-sha256":..., "xlsx-sha256":...}}}
            // into our internal Baseline shape.
            using var doc = JsonDocument.Parse(raw);
            var b = new Baseline();
            if (doc.RootElement.TryGetProperty("samples", out var samplesElem) &&
                samplesElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var sample in samplesElem.EnumerateObject())
                {
                    var entry = new BaselineEntry();
                    foreach (var prop in sample.Value.EnumerateObject())
                    {
                        switch (prop.Name)
                        {
                            case "pdf-sha256":  entry.pdfsha256  = prop.Value.GetString() ?? ""; break;
                            case "docx-sha256": entry.docxsha256 = prop.Value.GetString() ?? ""; break;
                            case "xlsx-sha256": entry.xlsxsha256 = prop.Value.GetString() ?? ""; break;
                        }
                    }
                    b.samples[sample.Name] = entry;
                }
            }
            return b;
        }
        catch
        {
            // Corrupt baseline → start fresh; the regenerate mode will rewrite it.
            return new Baseline();
        }
    }

    private static void SaveBaseline(Baseline b)
    {
        var path = BaselinePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var serializable = new
        {
            // Match the exact JSON schema documented in W1.5:
            //   { "samples": { "<name>": { "pdf-sha256": "...", "docx-sha256": "...", "xlsx-sha256": "..." } } }
            samples = b.samples.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>
                {
                    ["pdf-sha256"]  = kv.Value.pdfsha256,
                    ["docx-sha256"] = kv.Value.docxsha256,
                    ["xlsx-sha256"] = kv.Value.xlsxsha256,
                },
                StringComparer.Ordinal),
        };
        var json = JsonSerializer.Serialize(serializable,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + "\n", Encoding.UTF8);
    }

    private static void TryCleanupDir(string dir)
    {
        // Honor KEEP_SAMPLE=1 for debugging — same convention as PdfLetterExportTests.
        if (string.Equals(Environment.GetEnvironmentVariable("KEEP_SAMPLE"), "1",
                StringComparison.OrdinalIgnoreCase))
            return;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
