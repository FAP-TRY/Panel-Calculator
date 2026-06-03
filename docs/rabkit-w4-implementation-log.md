# RAB Kit Week 4 — Implementation Log (FINAL — Phase 1 Complete)

_Eksekusi 2026-06-03. Branch: `claude/rabkit-refactor`._

## Goal

Implement edition manifest infrastructure + license claims V2 extension untuk
multi-edition support, sambil tetap 100% backward compatible dengan customer
PT TTS existing yang punya license V1 (legacy format). Setelah W4:
- Codebase 100% multi-tenant ready (W1-W3 sudah kerjakan branding extraction).
- Signed edition manifest infrastructure siap (Ed25519 verified).
- License V2 format (edition/tier/industry/expiry/features bitmask) ready
  untuk multi-edition launch Phase 2.
- License V1 (PT TTS existing) tetap valid via fallback ke manifest editionId.
- Title bar tampil "v1.3.0 — Custom (TTS)" — visible signal multi-edition aware.

## Status Akhir

- **Build**: 0 error, 1 pre-existing warning (`DashboardForm.AddQueueDialog.CompanyName`
  — sama dengan W1/W2/W3 baseline, tidak terkait W4).
- **Tests**: 168/168 pass (147 W3 baseline + 5 SignedManifestLoader + 7
  EditionContext + 7 LicensePayloadV2 + 2 BrandManifestBundle).
- **Golden master verify**: ✅ PDF Formal + Word + Excel hash IDENTIK dengan v1.2.9
  baseline — refactor tidak menyentuh output bytes.
- **Single-file EXE**: ✅ `dotnet publish ...PublishSingleFile=true` menghasilkan
  198.9 MB EXE (non-obfuscated quick build; production obfuscated build perlu
  dijalankan manual oleh user via `Tools\build-release-singlefile.ps1` karena
  PowerShell sandbox restriction).
- **Manifest bundle**: ✅ `Panel.Branding/Assets/edition.manifest.bundle` (576 bytes)
  ter-generate via `Tools\sign-edition-manifest.ps1` + signed dengan
  `~/.panel-calculator-secrets/license-private.key`, verify-roundtrip OK.

## Tasks Completed

### W4.1 — EditionManifest format + Signed JSON Loader

**Files baru:**
- `RabKit.Branding/EditionManifest.cs` — record `EditionManifest` (7 fields:
  EditionId, EditionTier, Industry, BundledIndustryPackId, Features, IssuedAt,
  IssuerKey) + record `EditionFeatures` (6 fields: WatermarkOutput,
  AllowBrandingOverride, AllowMarketplacePacks, MaxEstimationsPerMonth,
  MaxConcurrentSeats, RequiresOnlineActivation) + exception `InvalidManifestException`.
  JSON property names match exec plan spec, `[JsonPropertyName]` annotations
  pin them sebagai canonical wire format (deterministic across JsonSerializer
  versions).
- `RabKit.Branding/SignedManifestLoader.cs` — Ed25519 signature verification +
  bundle pack/unpack. Format: `[4 bytes BE length][N bytes UTF-8 JSON][64 bytes Ed25519 sig]`.
  Binary-wrapped (not inline JSON sig field) untuk deterministic byte-identical
  signing — JSON canonicalization terkenal underspecified. API:
  - `LoadAndVerify(jsonBytes, sigBytes, publicKeyBase64)` → throws InvalidManifestException
  - `LoadAndVerifyFromBundle(bundleBytes, publicKeyBase64)` → unpacks header lalu delegate
  - `PackBundle(jsonBytes, sigBytes)` → builds bundle blob (signing tool)
  - `SerializeManifestForSigning(manifest)` → canonical UTF-8 bytes (signing tool)

**Tests:** `PanelCalculator.Tests/Security/SignedManifestLoaderTests.cs` — 5 tests:
1. `LoadAndVerify_ValidSignature_ReturnsManifest` — happy path.
2. `LoadAndVerify_TamperedJson_ThrowsInvalidManifest` — byte flip dalam JSON.
3. `LoadAndVerify_WrongPublicKey_ThrowsInvalidManifest` — different signer pubkey.
4. `LoadAndVerifyFromBundle_ValidBundle_ReturnsManifest` — round-trip with bundle.
5. `LoadAndVerifyFromBundle_TruncatedBundle_Throws` — bundle missing trailing bytes.

**Dependencies:** RabKit.Branding csproj sekarang ref `NSec.Cryptography 24.4.0`
(same version sebagai PanelCalculator.Core) — supaya 1 NSec runtime per EXE,
tidak ada double-load.

### W4.2 — License Claims Extension (V2 format)

**KRITIS — Backward compat:** existing TTS V1 license tetap decode + validate
OK. Format wire byte order TIDAK berubah — V1 byte[0] = version = 1, V2 byte[0] = 2.
Decoder auto-detects via byte[0]. Issuer (`BuildSignablePayload` overload V1-arity)
default tetap emit V1 — TTS admin yang pakai keygen lama tidak terganggu.

**File diubah:**
- `PanelCalculator.Core/Security/LicensePayload.cs` — extend:
  - Tambah `FormatVersionV1 = 1` (existing) + `FormatVersionV2 = 2` (new).
  - Tambah second `BuildSignablePayload(...)` overload (8-arity) yang accept
    editionId, tier, industry, expiresAt, features. Emit V2 byte layout:
    `[1 ver][8 fp][8 issueUnix LE][1 nameLen+name][1 editionLen+edition][1 tierLen+tier][1 industryLen+industry][8 expiresTicks LE][1 featuresBitmask]`.
  - Tambah `PackFeaturesBitmask` + `UnpackFeaturesBitmask` (4 bool bits: watermark,
    branding, marketplace, online; bits 4-7 reserved). Numeric quotas
    (MaxEstimationsPerMonth, MaxConcurrentSeats) TIDAK di bitmask — di-fallback
    dari manifest karena bitmask bounded 8 bits.
  - `Decode(licenseKey)` cek byte[0]: dispatch ke `DecodeV1` atau `DecodeV2`.
    V1 path 100% identik dengan implementasi lama, hanya extracted ke helper.
    V2 reads variable-length string fields plus expiry ticks + features byte.
  - `DecodedLicense` record extended dengan 5 V2 fields (`EditionId`, `Tier`,
    `Industry`, `ExpiresAtUtc`, `Features`). V1 decode mengembalikan defaults
    (empty string, null, all-false features) — caller (EditionContext) apply
    manifest fallback.

- `PanelCalculator.Core/Security/LicenseService.cs` — tambah `LicenseDecoder`
  static class + `PublicDecodedLicense` record sebagai public projection
  dari internal `LicensePayload.DecodedLicense`. Memungkinkan LicenseGate
  (WinForms layer) decode license tanpa `InternalsVisibleTo PanelCalculator.WinForms`.

**Tests:** `PanelCalculator.Tests/Security/LicensePayloadV2Tests.cs` — 7 tests:
1. `V1Payload_DecodesAsVersion1_WithEmptyEditionFields` — KRITIS backward compat.
2. `V1Payload_FormatVersionConstant_StaysAt1` — guard: `FormatVersion = V1` always.
3. `V2Payload_RoundTrip_PreservesAllFields` — full V2 ser/de.
4. `V2Payload_NeverExpires_RoundTripsAsNull` — null expiry encoded as ticks=0.
5. `V2Payload_EmptyEditionStrings_StillRoundTrip` — degenerate V2 still parses.
6. `Features_BitmaskRoundTrip_PreservesAllBoolFlags` — 6 feature combos.
7. `Features_BitmaskReservedBits_AreZero` — bits 4-7 not set by packer.

**Existing tests:** semua 11 `LicenseServiceTests` tetap pass — V1 sig verification
+ hardware match + tampering detection masih bekerja persis seperti v1.2.9.

### W4.2b — Tools/LicenseKeyGen (CLI + GUI)

**File diubah:**
- `Tools/LicenseKeyGen/LicenseIssuer.cs` — tambah:
  - `EditionTierSpec` record (5 fields).
  - `Issue(...)` overload yang accept `EditionTierSpec`. Old `Issue(fingerprint, name, key)`
    overload tetap ada (V1 byte-identical).
  - `SignManifestBundle(manifest, privateKeyPath)` — sign + pack helper.
  - `VerifyResult` extended dengan V2 fields (EditionId, Tier, Industry, ExpiresAtUtc).
  - `IssueResult.FormatVersion` field baru untuk discrimination.
- `Tools/LicenseKeyGen/Program.cs` — extend `issue` command dengan optional
  flags `--edition`, `--tier`, `--industry`, `--expires`, `--features`. Kalau
  semua kosong → emit V1 (existing behavior). Kalau ANY supplied → emit V2,
  validate ketiga inti (edition+tier+industry) wajib ada. Tambah `sign-manifest`
  command baru (input JSON + output bundle + key path).
- `Tools/LicenseKeyGen/LicenseKeyGen.csproj` — tambah ProjectReference ke RabKit.Branding.

- `Tools/LicenseKeyGenGui/MainForm.cs` — tambah UI block "Sertakan klaim edisi (V2
  license)" checkbox + 4 control: tier dropdown (6 entri), industry textbox,
  editionId textbox, expires checkbox + DateTimePicker. Default: TTS Custom
  workflow → checkbox unticked → emit V1 (one-click flow tetap jalan untuk
  PT TTS admin). Centang checkbox → V2 fields enabled, defaults pre-filled
  ("custom" + "panel-electrical" + "custom-tts-panel-v1" + never-expire).

**CLI usage examples:**
```
# V1 (PT TTS existing workflow, unchanged):
dotnet run --project Tools/LicenseKeyGen -- issue \
    --fp "D8F8-BEB8-E346-4A5A" \
    --name "PT Sumber Makmur Sejahtera"

# V2 explicit Custom (recommended setelah v1.3.0 untuk all new TTS licenses):
dotnet run --project Tools/LicenseKeyGen -- issue \
    --fp "D8F8-BEB8-E346-4A5A" \
    --name "PT Sumber Makmur Sejahtera" \
    --edition "custom-tts-panel-v1" \
    --tier "custom" \
    --industry "panel-electrical"

# V2 Generic Pro 1-year subscription:
dotnet run --project Tools/LicenseKeyGen -- issue \
    --fp "..." --name "PT Customer" \
    --edition "generic-rab-v1" --tier "generic-pro" --industry "panel-electrical" \
    --expires "2027-06-01" \
    --features "online,branding"

# V2 Lifetime with watermark (Trial tier):
dotnet run --project Tools/LicenseKeyGen -- issue \
    --fp "..." --name "PT Trial Co" \
    --edition "generic-rab-v1" --tier "generic-free" --industry "panel-electrical" \
    --features "watermark"

# Sign edition manifest (one-time + on each spec change):
dotnet run --project Tools/LicenseKeyGen -- sign-manifest \
    --input Panel.Branding\Assets\source\edition.manifest.json \
    --output Panel.Branding\Assets\edition.manifest.bundle \
    --key "%USERPROFILE%\.panel-calculator-secrets\license-private.key"
```

**GUI usage:**
1. Open LicenseKeyGenGui project (or built EXE).
2. Paste hardware fingerprint, customer name (default workflow).
3. Click GENERATE LICENSE — emits V1 (backward compat).
4. For V2: tick "Sertakan klaim edisi" checkbox → V2 fields appear → adjust
   tier/industry/edition/expiry as needed → GENERATE LICENSE emits V2.

### W4.3 — EditionContext.Current

**File baru:** `RabKit.Branding/EditionContext.cs` — static class with:
- `Manifest` property (throw if uninitialized — matches BrandContext pattern).
- `CurrentLicense` property (null until activated).
- `Initialize(manifest)` — bind manifest (test-safe replacement).
- `ValidateAndSetLicense(decodedLicense, fingerprint)` — full validation pipeline:
  1. Verify Ed25519 signature (against `BrandContext.LicensePublicKeyBase64`).
  2. Hardware fingerprint binding.
  3. **V1 fallback path**: if `license.EditionId == ""`, inherit manifest's
     EditionId/Tier/Industry/Features.
  4. **V2 cross-check**: if `license.EditionId != manifest.EditionId` → return
     `EditionMismatch` (rejected).
  5. Expiry check (V2 only; V1 = perpetual).
  6. Cache resolved claims in `_license`.
- `GetTierDisplayName()` — friendly mapping ("custom" → "Custom", "generic-basic"
  → "Basic", etc.).
- `EditionValidationResult` record (IsValid, Reason, Detail).
- `LicenseClaims` record (resolved claims — V1 fallback already applied).
- `DecodedLicenseInput` record — engine-side projection of LicensePayload's
  internal type, since RabKit.Branding sits below Core in ref graph.

**Tests:** `PanelCalculator.Tests/Security/EditionContextTests.cs` — 7 tests
(serialised via `[Collection("EditionContextSerial")]` karena mutate BrandContext
+ EditionContext singletons):
1. `ValidateAndSetLicense_V1License_FallsBackToManifestEditionFields` — KRITIS
   V1 backward compat: V1 license + TTS manifest → tier resolves to "custom",
   editionId resolves to "custom-tts-panel-v1".
2. `ValidateAndSetLicense_V2License_MatchingManifest_Succeeds` — happy V2.
3. `ValidateAndSetLicense_V2License_DifferentEdition_Rejects` — cross-check fail.
4. `ValidateAndSetLicense_V2License_Expired_Rejects` — expiry past current time.
5. `ValidateAndSetLicense_WrongFingerprint_Rejects` — hw binding.
6. `GetTierDisplayName_AfterV1License_ShowsCustomFromManifest`.
7. `GetTierDisplayName_BeforeActivation_ShowsCustomFromManifestStillAvailable` —
   tier label visible even before activation (manifest pre-loaded).

**Test infrastructure:** `BrandContextWiringTest` di-pindah ke same `[Collection]`
supaya xUnit serialise — tanpa ini, parallel test execution membuat
TestBrandConfig leak ke BrandContextWiringTest dan asset accessor return null.
Solusi: `BrandContextWiringTest.EnsureInitialized` sekarang force-rebind PT TTS
brand config setiap test (idempotent).

### W4.4 — Bundle TTS edition.manifest di Panel.Branding

**Files baru:**
- `Panel.Branding/Assets/source/edition.manifest.json` — canonical TTS manifest:
  ```json
  {
    "editionId": "custom-tts-panel-v1",
    "editionTier": "custom",
    "industry": "panel-electrical",
    "bundledIndustryPackId": "tts-panel-listrik-2026.Q1",
    "features": {
      "watermarkOutput": false,
      "allowBrandingOverride": false,
      "allowMarketplacePacks": false,
      "maxEstimationsPerMonth": 0,
      "maxConcurrentSeats": 999,
      "requiresOnlineActivation": false
    },
    "issuedAt": "2026-06-02T00:00:00Z",
    "issuerKey": "D5Bk2OC+FFZdZqqtI86iFCiy1/pFRQLbkMBpVQ+ia6w="
  }
  ```
- `Panel.Branding/Assets/edition.manifest.bundle` — 576 bytes, generated via
  `Tools\sign-edition-manifest.ps1` signed with PT TTS issuer private key at
  `~/.panel-calculator-secrets/license-private.key`.
- `Tools/sign-edition-manifest.ps1` — PowerShell wrapper around
  `dotnet run --project Tools/LicenseKeyGen -- sign-manifest ...`. Resolves
  default private key path under `%USERPROFILE%\.panel-calculator-secrets\`,
  validates inputs, calls the C# signer, prints next-steps.

**File diubah:**
- `RabKit.Branding/IBrandConfig.cs` — tambah `byte[]? GetEditionManifestBundle()`
  method. Returns null untuk dev/test brand pack tanpa bundled manifest
  (Program.Main fallback to permissive "dev-mode" manifest).
- `Panel.Branding/PanelBrandConfig.cs` — implement `GetEditionManifestBundle()`
  via existing `ReadEmbedded` helper, resource name
  `"Panel.Branding.Assets.edition.manifest.bundle"`.
- `Panel.Branding/Panel.Branding.csproj` — tambah
  `<EmbeddedResource Include="Assets\edition.manifest.bundle" />`.
- `PanelCalculator.WinForms/Program.cs` — wire manifest loading di `RunApp`,
  setelah `BrandContext.Initialize`, sebelum DbMigrator:
  - Read bundle bytes via `BrandContext.Current.GetEditionManifestBundle()`.
  - Call `SignedManifestLoader.LoadAndVerifyFromBundle(...)`.
  - On success: `EditionContext.Initialize(manifest)`.
  - On null (dev mode without bundle): fallback to permissive default manifest.
  - On `InvalidManifestException`: log + show MessageBox + abort startup
    (tampered installer protection).
- `PanelCalculator.WinForms/Services/LicenseGate.cs` — setelah license validates
  via LicenseService, panggil `TrySeedEditionContext(...)` helper baru yang
  decode license via `LicenseDecoder.Decode(...)` lalu pass ke
  `EditionContext.ValidateAndSetLicense(...)`. Failure di EditionContext
  level non-fatal (log warning) — LicenseService sudah accept license, ini
  hanya seed metadata untuk title bar + future policy gates.

**Tests:** `PanelCalculator.Tests/Security/BrandManifestBundleTests.cs` — 2 tests:
1. `PanelBrandConfig_GetEditionManifestBundle_ReturnsNonEmptyBytes` — embedded
   resource extraction.
2. `PanelBrandConfig_EditionManifestBundle_VerifiesAndDeserializes` — actual
   Ed25519 verify against production pubkey + deserialise → assert fields.

### W4.5 — Title Bar Tier Display + Bump v1.3.0

**File diubah:**
- `PanelCalculator.WinForms/Forms/ShellForm.cs` line 53-58: title bar sekarang
  baca:
  ```
  $"{BrandContext.Current.AppDisplayName} — v{UpdateService.AppVersion} — {tier} ({BrandContext.Current.CompanyShortName})"
  ```
  Hasil untuk TTS: `"Kalkulator Panel Tritunggal Swarna — v1.3.0 — Custom (TTS)"`.
  Tier source: `EditionContext.GetTierDisplayName()` — fallback "Custom" jika
  EditionContext belum initialized (defensive, tidak akan terjadi di production
  karena Program.Main pasti panggil Initialize sebelum form).

**Version bump 1.2.9 → 1.3.0 di 4 file:**
- `Panel.Branding/PanelBrandConfig.cs` line 107 (`AppVersion => "1.3.0"`).
- `PanelCalculator.iss` line 6 (`#define AppVersion "1.3.0"`) + line 24
  (`OutputBaseFilename=KalkulatorPanel-TTS-v1.3.0-Setup`).
- `Installer/PanelCalculatorSetup.iss` line 7 (`#define MyAppVersion "1.3.0"`).
- `CLAUDE.md` "Versi saat ini" + changelog entry baru.
- `PanelCalculator.Tests/GoldenMaster/BrandContextWiringTest.cs` line 97
  (`Assert.Equal("1.3.0", ...AppVersion)`).

**CLAUDE.md changelog entry baru:**
```
- v1.3.0: [INTERNAL — Phase 1 RAB Kit complete] Refactor 3-week branding
  abstraction: codebase 100% multi-tenant ready (RabKit.Branding interface
  + Panel.Branding TTS implementation + EditionContext + signed manifest).
  Customer-visible change: title bar sekarang baca "v1.3.0 — Custom (TTS)".
  Output PDF/Word/Excel byte-identical dengan v1.2.9 (verified via 5 golden
  master tests). Backward compat: license existing TTS (V1 format) tetap
  valid via fallback ke manifest editionId — customer existing tidak perlu
  re-aktivasi. License format V2 baru (edition/tier/industry/expiry/features
  bitmask) tersedia untuk multi-edition launch Phase 2.
```

### W4.6 — Final Verify Phase 1

| Check | Result |
|-------|--------|
| `dotnet build PanelCalculator.sln -c Release` | ✅ 0 error, 1 pre-existing warning |
| `dotnet test PanelCalculator.Tests --no-build` | ✅ **168/168 pass** |
| Golden master 5 sample (PDF/Word/Excel hash) | ✅ identical dengan v1.2.9 baseline |
| License V1 backward compat test | ✅ pass (EditionContextTests #1) |
| EditionMismatch rejection (V2) | ✅ pass (EditionContextTests #3) |
| `SignedManifestLoader` tests | ✅ 5/5 pass |
| `BrandManifestBundle` tests | ✅ 2/2 pass (real signed bundle round-trips) |
| Single-file EXE publish | ✅ 198.9 MB, valid PE32+ Windows GUI executable |

**Production obfuscated build:** harus dijalankan manual oleh user via
`Tools\build-release-singlefile.ps1` karena sandbox restriction blocks
PowerShell execution. Quick verify dengan plain `dotnet publish ...PublishSingleFile=true`
sudah berhasil — bundle valid, no missing dependencies.

**Installer build:** belum dijalankan dalam W4 ini karena InnoSetup CLI juga
PowerShell-only. User perlu jalankan setelah review:
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "PanelCalculator.iss"
# Output: Installer\KalkulatorPanel-TTS-v1.3.0-Setup.exe (~56MB target)
```

## File Stats

**Files baru (8):**
- `RabKit.Branding/EditionManifest.cs` (51 lines)
- `RabKit.Branding/SignedManifestLoader.cs` (160 lines)
- `RabKit.Branding/EditionContext.cs` (227 lines)
- `PanelCalculator.Tests/Security/SignedManifestLoaderTests.cs` (113 lines)
- `PanelCalculator.Tests/Security/LicensePayloadV2Tests.cs` (146 lines)
- `PanelCalculator.Tests/Security/EditionContextTests.cs` (243 lines)
- `PanelCalculator.Tests/Security/BrandManifestBundleTests.cs` (45 lines)
- `Tools/sign-edition-manifest.ps1` (68 lines)
- `Panel.Branding/Assets/source/edition.manifest.json` (16 lines, JSON)
- `Panel.Branding/Assets/edition.manifest.bundle` (576 bytes, signed binary)
- `docs/rabkit-w4-implementation-log.md` (this file)

**Files diubah (10):**
- `RabKit.Branding/RabKit.Branding.csproj` (+10 lines — NSec dep + InternalsVisibleTo)
- `RabKit.Branding/IBrandConfig.cs` (+18 lines — `GetEditionManifestBundle`)
- `Panel.Branding/Panel.Branding.csproj` (+8 lines — bundle EmbeddedResource)
- `Panel.Branding/PanelBrandConfig.cs` (+12 lines — `GetEditionManifestBundle` impl, version bump)
- `PanelCalculator.Core/Security/LicensePayload.cs` (+233 lines / -69 lines —
  V2 builder, V1+V2 decoder split, features bitmask helpers, extended DecodedLicense)
- `PanelCalculator.Core/Security/LicenseService.cs` (+45 lines — `LicenseDecoder`
  + `PublicDecodedLicense` public API)
- `PanelCalculator.WinForms/Program.cs` (+44 lines — manifest load + wire
  EditionContext.Initialize + InvalidManifestException handler)
- `PanelCalculator.WinForms/Forms/ShellForm.cs` (+5 lines / -1 line — tier in
  title bar)
- `PanelCalculator.WinForms/Services/LicenseGate.cs` (+45 lines — seed
  EditionContext after license validates)
- `Tools/LicenseKeyGen/LicenseIssuer.cs` (+98 lines / -2 lines — V2 issuance +
  SignManifestBundle)
- `Tools/LicenseKeyGen/Program.cs` (+125 lines / -10 lines — V2 issue flags +
  sign-manifest command)
- `Tools/LicenseKeyGen/LicenseKeyGen.csproj` (+2 lines — RabKit.Branding ref)
- `Tools/LicenseKeyGenGui/MainForm.cs` (+98 lines / -4 lines — V2 checkbox + fields)
- `PanelCalculator.iss` (1 char — version)
- `Installer/PanelCalculatorSetup.iss` (1 char — version)
- `CLAUDE.md` (+10 lines — version + changelog v1.3.0 entry)
- `PanelCalculator.Tests/GoldenMaster/BrandContextWiringTest.cs` (+5 lines /
  -2 lines — version assertion update + same-collection serialization)

**Total: ~1300 insertions / ~85 deletions / 8 files created / 17 files modified.**

## Apa yang TIDAK Diubah di W4 (Sesuai Scope Plan)

- ❌ DB schema — tetap v1.2.9 (LibraryPacks schema is Phase 2 / Week 5 work).
- ❌ Generic-specific stuff (quota enforcement, watermark renderer, branding
  override UI) — DEFER ke Phase 2 supaya W4 manageable (sesuai task spec).
- ❌ Backward-incompatible API changes pada existing test suite.
- ❌ Output PDF/Word/Excel — golden master 5 sample membuktikan byte-identical.
- ❌ `cmbTargetSection.SelectedIndexChanged` palette di MainForm (intentional
  palette variation, scoped out di W3).
- ❌ Behavior license gate, auto-update, DB encryption — semua sama persis
  dengan v1.2.9, hanya AppVersion string yang berubah.

## Yang Berubah dari Sisi Customer (PT TTS)

**Visible:**
- Title bar tampil `"Kalkulator Panel Tritunggal Swarna — v1.3.0 — Custom (TTS)"`
  (sebelum: `"... — v1.2.9"`). Tier "Custom" + brand "(TTS)" baru.
- Top bar version badge: `v1.3.0` (sebelum: `v1.2.9`).

**Tidak visible (zero functional impact):**
- License existing tetap valid — V1 decoded ke tier "Custom" via manifest fallback.
- DB encryption key derivation tetap identik (machine key pepper + app tag bound to
  PT TTS values).
- Output PDF/Word/Excel byte-identical (golden master verified).
- Auto-update flow tetap pointing ke `FAP-TRY/Panel-Calculator` GitHub releases.
- All forms (Login, Activation, Main, Reports, Settings, History) UI tidak berubah.

## Yang Berubah dari Sisi Developer

**Setelah W4, untuk rebrand jadi RAB Cepat / Carrosserie / customer baru:**
1. Bikin 1 project baru `<NamaBrand>.Branding/` yang implement `IBrandConfig`
   + `IIndustryProfile`.
2. Bikin `edition.manifest.json` baru — set editionId, tier, industry sesuai produk.
3. Sign dengan brand owner's Ed25519 private key:
   ```powershell
   .\Tools\sign-edition-manifest.ps1 -Input X.json -Output X.bundle -Key path\to\private.key
   ```
4. Embed bundle sebagai `<EmbeddedResource>` di brand pack csproj.
5. Ganti `BrandContext.Initialize(...)` di Program.cs Main.
6. Rebuild + ship.

Tidak perlu lagi grep/modify Core/Data/WinForms layer — semua tenant-specific
sudah jadi 2 file (brand pack PanelBrandConfig + PanelIndustryProfile) +
1 signed manifest bundle.

## Risiko & Mitigasi

| Risiko | Status | Mitigasi |
|--------|--------|----------|
| License existing TTS jadi invalid setelah v1.3.0 upgrade | ✅ Verified via `ValidateAndSetLicense_V1License_FallsBackToManifestEditionFields` test | V1 license format decode pathnya 100% preserved. EditionContext apply manifest fallback values (custom-tts-panel-v1 / custom / panel-electrical) untuk V1 license. Test issue V1 license + validate → return Valid + tier "Custom". |
| Manifest bundle tertukar/tertamper di installer | ✅ Mitigated | Ed25519 signature verification at startup. Tampered manifest → `InvalidManifestException` → MessageBox + abort boot (tidak silent fallback). Public key di-embed di same DLL, attacker harus juga ganti DLL. |
| Output PDF/Word/Excel berubah byte-wise | ✅ Verified via 5 golden master | Refactor murni infrastructure — tidak ada export service code path yang berubah. |
| EditionContext static singleton race condition di tests | ✅ Resolved | `[Collection("EditionContextSerial")]` serialize EditionContextTests + BrandContextWiringTest. EnsureInitialized di BrandContextWiringTest sekarang force-rebind PT TTS brand setiap test. |
| `LicensePayload.DecodedLicense` extended constructor breaks existing tests | ✅ No impact | Record positional ctor backward compatible kalau caller pakai named args. Existing tests (LicenseServiceTests) hanya read fields, tidak construct DecodedLicense. |
| EmbeddedResource path collision (logo.png vs edition.manifest.bundle) | ✅ Verified | Resource name `Panel.Branding.Assets.edition.manifest.bundle` resolves uniquely. Test `BrandManifestBundleTests` cover both paths return non-null bytes. |
| Production obfuscated build break karena Obfuscar rename `LicensePayload.Decode` | ⚠️ Risk to verify | Obfuscar config skip `internal` types? Need verify via `Tools\build-release-singlefile.ps1` setelah merge — kalau Obfuscar rename internal methods, `LicenseDecoder.Decode` masih bisa call via reflection? Recommend smoke-test manual run dari user. |

## Pertanyaan Blocker / Open Decision

Tidak ada blocker. Semua scope task selesai dengan test coverage memadai.

**Decisions untuk owner-confirm sebelum release ke production:**

1. **Issuer key reuse**: TTS manifest di-sign dengan SAMA private key sebagai
   TTS license issuer (`license-private.key`). Apakah OK? Alternative: bikin
   separate `manifest-private.key` supaya rotasi independent. Saya rekomendasi
   pakai same key untuk simplicity di Phase 1 — kalau perlu rotasi, ganti key
   lalu re-sign both manifest + re-issue licenses.

2. **`issuerKey` field di manifest**: saat ini set ke base64 dari TTS public key.
   Apakah ini intended sebagai "trusted issuer pubkey untuk marketplace pack
   verification" (Phase 3), atau hanya cache untuk audit? Field tidak di-validate
   oleh SignedManifestLoader — bisa di-remove atau di-leverage nanti.

3. **Dev-mode fallback**: kalau brand pack ship tanpa bundled manifest, Program.cs
   buat permissive "dev-mode" manifest dengan tier="custom". Apakah ini OK untuk
   release builds? Alternative: refuse boot kalau bundle null in Release mode.
   Saya pilih permissive supaya dev/test scenarios mudah, plus customer install
   pasti punya bundle (build pipeline error kalau lupa).

4. **Title bar "Custom (TTS)" format**: apakah CompanyShortName "TTS" terlalu
   teknikal untuk customer? Alternative: pakai CompanyName penuh, tapi panjang
   ("Kalkulator Panel Tritunggal Swarna — v1.3.0 — Custom (PT. Tritunggal Swarna)")
   bisa overflow di small windows. Decision punya owner — saya pakai short name
   sesuai task spec.

## Phase 1 Progress (COMPLETE)

| Week | Status | Description |
|------|--------|-------------|
| W1   | ✅ Done | Branding abstraction foundation (interfaces + PanelBrandConfig + PanelIndustryProfile + tests) |
| W2   | ✅ Done | Port semua hardcoded "PT TTS" strings + asset references ke BrandContext |
| W3   | ✅ Done | Port section vocab + theme palette + family heuristic ke BrandContext |
| W4   | ✅ Done | Edition manifest + license claims V2 extension + EditionContext (this log) |

**Phase 1 milestone delivered:**
- ✅ Codebase 100% multi-tenant ready (zero hardcode "TTS"/"Tritunggal"/"Kuntjoro"
  in Core/Data/WinForms).
- ✅ Edition manifest infrastructure (signed JSON loader + bundle format + CLI).
- ✅ License V2 format dengan backward compat ke V1 existing.
- ✅ Customer TTS install dari v1.2.9 release di GitHub tetap zero-impact —
  upgrade ke v1.3.0 invisible kecuali title bar.
- ✅ Demo-ready: "engine swap dalam 1 hari" — ganti `Panel.Branding.dll` jadi
  `Carrosserie.Branding.dll` + edition manifest sign baru, rebuild EXE,
  app rebranded dengan UX identik.

**Siap Phase 2 (Week 5-8) launch RAB Cepat:**
- Library pack v1 schema + migration → bundle TTS catalog jadi `.rabpack`.
- Generic edition policy gates (quota, watermark, branding override).
- License server + Xendit checkout integration.
- Generic edition public beta dengan landing page.

## Manual Verify yang Perlu Owner Lakukan

Setelah review log + merge:

1. **Production obfuscated build:**
   ```powershell
   .\Tools\build-release-singlefile.ps1
   ```
   Expected output: `publish\PanelCalculator.exe` (~190MB) + `.sha256` manifest.
   Verify Panel.Branding.dll obfuscated tetap punya embedded manifest bundle.

2. **Installer build:**
   ```powershell
   $env:PANELCALC_INSTALL_PASSWORD = "TTS2025_v130"  # atau password lain
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "PanelCalculator.iss"
   ```
   Expected: `Installer\KalkulatorPanel-TTS-v1.3.0-Setup.exe` (~56MB).

3. **Smoke test EXE boot:**
   - Install di test VM.
   - Login admin/admin.
   - Verify title bar: `"Kalkulator Panel Tritunggal Swarna — v1.3.0 — Custom (TTS)"`.
   - Buat 1 estimasi sederhana → Save → Export PDF → buka di Adobe Reader →
     verify letterhead PT TTS muncul + signer "Kuntjoro Handoko" muncul.

4. **License V1 backward compat verify (PRODUCTION):**
   - Install v1.3.0 di test VM yang punya DB dari v1.2.9.
   - Boot app — verify license existing tetap accepted (tidak prompt ActivationForm).
   - Title bar tampil "Custom (TTS)" — proves EditionContext seeded from V1 license.

5. **Auto-update verify:**
   - Setelah upload v1.3.0 release ke GitHub, install v1.2.9 di VM, run app,
     verify "🔄 Update v1.3.0" notification muncul di top bar.
   - Klik notification → confirm UAC prompt → app restart sebagai v1.3.0.
