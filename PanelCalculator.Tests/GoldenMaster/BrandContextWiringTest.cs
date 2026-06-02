using RabKit.Branding;
using Xunit;

namespace PanelCalculator.Tests.GoldenMaster;

/// <summary>
/// Sanity tests for the Week-1 branding-abstraction wiring. Make sure
/// <see cref="BrandContext"/> resolves to PT TTS values when initialized
/// with the production <c>PanelBrandConfig</c> + <c>PanelIndustryProfile</c>.
///
/// <para>
/// These act as a cheap "the app would boot" smoke check — the real
/// EXE smoke test (run the published EXE, see activation form) is in
/// the manual verification path in <c>docs/rabkit-w1-implementation-log.md</c>.
/// </para>
/// </summary>
[Trait("Category", "GoldenMaster")]
public class BrandContextWiringTest
{
    private static void EnsureInitialized()
    {
        if (BrandContext.IsInitialized) return;
        BrandContext.Initialize(
            new PanelBranding.PanelBrandConfig(),
            new PanelBranding.PanelIndustryProfile());
    }

    [Fact]
    public void PanelBrandConfig_ReturnsPtTtsCompanyIdentity()
    {
        EnsureInitialized();

        Assert.Equal("PT. Tritunggal Swarna", BrandContext.Current.CompanyName);
        Assert.Equal("TTS",                   BrandContext.Current.CompanyShortName);
        Assert.Equal("Kuntjoro Handoko",      BrandContext.Current.DefaultSignerName);
        Assert.Equal("Direktur",              BrandContext.Current.DefaultSignerTitle);
        Assert.Equal("Bandung",               BrandContext.Current.DefaultOfferLocation);
    }

    [Fact]
    public void PanelBrandConfig_LegacyMachineKeyAppTag_IsExactStringRequiredByCustomerDb()
    {
        // KRITIS: SQLCipher DB on existing customer machines was derived
        // with this exact tag string. Changing it bricks every install.
        EnsureInitialized();
        Assert.Equal("PanelCalculator.v1", BrandContext.Current.LegacyMachineKeyAppTag);
    }

    [Fact]
    public void PanelBrandConfig_MachineKeyPepper_DecodesToExpectedCleartext()
    {
        // KRITIS: XOR-decode (mask 0x5A) must produce the literal
        // "TTS-PanelCalc-pepper-2026-v1" — the cleartext mixed into
        // MachineKeyProvider.GetKey() for every existing customer DB.
        EnsureInitialized();
        var pepperBytes = BrandContext.Current.MachineKeyPepperBytes;
        Assert.Equal(28, pepperBytes.Length);

        var sb = new System.Text.StringBuilder(pepperBytes.Length);
        for (int i = 0; i < pepperBytes.Length; i++)
            sb.Append((char)(pepperBytes[i] ^ 0x5A));

        Assert.Equal("TTS-PanelCalc-pepper-2026-v1", sb.ToString());
    }

    [Fact]
    public void PanelBrandConfig_LicensePublicKey_MatchesEmbeddedConstant()
    {
        // Mirrors PanelCalculator.Core.Security.LicenseService.PublicKeyBase64.
        // Until Week-2 ports the LicenseService to read from BrandContext,
        // both must stay in lock-step.
        EnsureInitialized();
        Assert.Equal("D5Bk2OC+FFZdZqqtI86iFCiy1/pFRQLbkMBpVQ+ia6w=",
            BrandContext.Current.LicensePublicKeyBase64);
    }

    [Fact]
    public void PanelBrandConfig_GitHubUpdateChannel_PointsAtRealRepo()
    {
        EnsureInitialized();
        Assert.Equal("FAP-TRY",            BrandContext.Current.UpdateGitHubOwner);
        Assert.Equal("Panel-Calculator",   BrandContext.Current.UpdateGitHubRepo);
        Assert.Equal("PanelCalculator.exe", BrandContext.Current.UpdateAssetName);
    }

    [Fact]
    public void PanelBrandConfig_AppIdentity_MatchesExistingInstall()
    {
        EnsureInitialized();
        Assert.Equal("Kalkulator Panel Tritunggal Swarna", BrandContext.Current.AppDisplayName);
        Assert.Equal("PanelCalculator",                    BrandContext.Current.AppDataFolderName);
        Assert.Equal("1.2.9",                              BrandContext.Current.AppVersion);
        Assert.Equal("EST-{0:yyyyMMdd}-{1:D3}",            BrandContext.Current.EstimationNumberPattern);
    }

    [Fact]
    public void PanelBrandConfig_AssetBytes_NonEmptyForBrandedImages()
    {
        EnsureInitialized();
        var logo       = BrandContext.Current.GetLogoBytes();
        var letterhead = BrandContext.Current.GetLetterheadBytes();
        var signature  = BrandContext.Current.GetSignatureBytes();
        var stamp      = BrandContext.Current.GetStampBytes();

        Assert.NotNull(logo);
        Assert.NotNull(letterhead);
        Assert.NotNull(signature);
        Assert.NotNull(stamp);
        Assert.True(logo!.Length       > 1000, $"logo bytes too small: {logo.Length}");
        Assert.True(letterhead!.Length > 50_000, $"letterhead bytes too small: {letterhead.Length}");
        Assert.True(signature!.Length  > 5_000,  $"signature bytes too small: {signature.Length}");
        Assert.True(stamp!.Length      > 5_000,  $"stamp bytes too small: {stamp.Length}");
    }

    [Fact]
    public void PanelIndustryProfile_HasPanelSectionVocab()
    {
        EnsureInitialized();
        var sections = BrandContext.CurrentIndustry.Sections;
        Assert.Contains("Box",      sections);
        Assert.Contains("Incoming", sections);
        Assert.Contains("Outgoing", sections);
        Assert.Contains("Trailer",  sections);
        Assert.Contains("Karoseri", sections);
        Assert.Contains("Jasa",     sections);
    }

    [Fact]
    public void PanelIndustryProfile_SectionDisplayMap_HandlesPdfAliases()
    {
        EnsureInitialized();
        var map = BrandContext.CurrentIndustry.SectionDisplayMap;
        Assert.Equal("Box Panel", map["Box"]);
        Assert.Equal("Incoming",  map["Material Utama"]);
        Assert.Equal("Outgoing",  map["Material Pendukung"]);
        Assert.Equal("Lainnya",   map["Trailer"]);
        Assert.Equal("Lainnya",   map["Karoseri"]);
        Assert.Equal("Lainnya",   map["Jasa"]);
    }

    [Fact]
    public void PanelIndustryProfile_CategoryFromFamily_PreservesMcbHeuristic()
    {
        EnsureInitialized();
        var ip = BrandContext.CurrentIndustry;
        Assert.Equal("MCB",    ip.CategoryFromFamily("MCB 1P 16A"));
        Assert.Equal("MCCB",   ip.CategoryFromFamily("MCCB EasyPact CVS"));
        Assert.Equal("RCCB",   ip.CategoryFromFamily("RCCB 4P 25A"));
        Assert.Equal("ACB",    ip.CategoryFromFamily("ACB Masterpact NW"));
        Assert.Equal("Busbar", ip.CategoryFromFamily("Busbar Linergy"));
        Assert.Equal("Box",    ip.CategoryFromFamily("Box Pragma Schneider"));
        Assert.Equal("ATS",    ip.CategoryFromFamily("ATS Transfer Switch"));
    }
}
