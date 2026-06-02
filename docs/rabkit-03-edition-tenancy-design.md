# Rabkit 03 Edition Tenancy Design

_Generated 2026-05-28 dari workflow `generic-rab-calculator-plan` (4 agents)._

---

# Konfigurasi Multi-Edition: Custom vs Generic RAB Calculator

## 1. Configuration Model

**Prinsip:** 3-layer config dengan precedence jelas — compile-time constant terkunci untuk security-critical, signed manifest untuk per-edition lock, DB untuk per-user runtime.

### Layer A — Compile-time (immutable, baked ke EXE)

```csharp
// PanelCalculator.Core/Configuration/BuildConstants.cs
public static class BuildConstants
{
    public const string ProductFamily   = "RABCalculator";
    public const string ProductVersion  = "1.3.0";
    // Public key Ed25519 untuk verify license + manifest signature
    public const string LicensePublicKey  = "MCowBQYDK2VwAyEA...";
    public const string ManifestPublicKey = "MCowBQYDK2VwAyEA...";
}
```

Hanya 1 build artifact `RABCalculator.exe`. Edition ditentukan oleh **manifest yang dibundel di installer**, bukan flag di EXE.

### Layer B — Edition Manifest (signed JSON, read-only, bundled di installer)

File: `%ProgramFiles%\TritunggalSwarna\RABCalculator\edition.manifest.json`

```json
{
  "schemaVersion": 1,
  "editionId": "custom-tts-panel-v1",
  "editionTier": "Custom",
  "industry": "panel-electrical",
  "customer": {
    "legalName": "PT Tritunggal Swarna",
    "displayName": "Tritunggal Swarna",
    "npwp": "01.234.567.8-901.000",
    "address": "Jl. Industri No. 123, Bandung 40123",
    "phone": "022-1234567"
  },
  "defaults": {
    "signerName": "Kuntjoro Handoko",
    "signerTitle": "Direktur",
    "signerCity": "Bandung",
    "currency": "IDR",
    "pphRate": 0.025,
    "ppnRate": 0.11
  },
  "libraryLockId": "panel-electrical-tts-2026Q2",
  "branding": {
    "logoPath": "branding/logo.png",
    "letterheadPath": "branding/letterhead.jpg",
    "footerBannerPath": "branding/footer.jpg",
    "signatureStampPath": "branding/cap-stempel.png",
    "primaryColor": "#1F4E79"
  },
  "featureOverrides": {
    "allowLibraryEdit": false,
    "allowBrandingEdit": false,
    "allowMultiCompany": false
  },
  "issuedAt": "2026-06-01T00:00:00Z",
  "signature": "BASE64_ED25519_OVER_CANONICAL_JSON"
}
```

Untuk Generic Edition, manifest sangat ringkas:

```json
{
  "schemaVersion": 1,
  "editionId": "generic-v1",
  "editionTier": "Generic",
  "industry": null,
  "customer": null,
  "defaults": { "currency": "IDR", "pphRate": 0.025, "ppnRate": 0.11 },
  "libraryLockId": null,
  "branding": null,
  "featureOverrides": {
    "allowLibraryEdit": true,
    "allowBrandingEdit": true,
    "allowMultiCompany": true
  },
  "issuedAt": "2026-06-01T00:00:00Z",
  "signature": "BASE64_ED25519_OVER_CANONICAL_JSON"
}
```

**Alasan signed manifest:** customer Custom edition tidak bisa swap manifest jadi Generic (untuk dapat tombol "Edit Library"), dan customer Generic tidak bisa fake jadi Custom untuk skip pricing tier.

### Layer C — Runtime config di SQLite

Tabel `Settings` existing dipakai untuk **per-user mutable** state (signer name override, last used filter, dll). Untuk Custom edition, write ke field yang dikunci akan ditolak oleh `IEditionPolicy` (lihat §5).

```sql
-- Tambahan kolom di Settings (atau pakai key-value yang sudah ada)
-- Key examples:
--   signer.name, signer.title, signer.city
--   ui.lastCategory, ui.lastVendor
--   branding.logoOverridePath  (Generic only)
--   company.profileId          (Generic multi-company)
```

---

## 2. Edition Detection

Single source of truth: `EditionContext` di-load di `Program.Main` sebelum form apapun:

```csharp
public sealed class EditionContext
{
    public EditionManifest Manifest { get; }
    public LicenseClaims  License  { get; }
    public IEditionPolicy Policy   { get; }   // gate semua mutation

    public static EditionContext Load(string installDir, string licensePath)
    {
        var manifestPath = Path.Combine(installDir, "edition.manifest.json");
        var manifest = SignedManifestLoader.LoadAndVerify(
            manifestPath, BuildConstants.ManifestPublicKey);

        var license = LicenseVerifier.LoadAndVerify(
            licensePath, BuildConstants.LicensePublicKey);

        // Cross-check: license harus match manifest editionId
        if (license.EditionId != manifest.EditionId)
            throw new LicenseEditionMismatchException();

        if (manifest.EditionTier == "Custom" &&
            license.Industry != manifest.Industry)
            throw new LicenseEditionMismatchException();

        IEditionPolicy policy = manifest.EditionTier switch
        {
            "Custom"  => new CustomEditionPolicy(manifest, license),
            "Generic" => new GenericEditionPolicy(manifest, license),
            _ => throw new InvalidEditionException(manifest.EditionTier)
        };
        return new EditionContext(manifest, license, policy);
    }
}
```

**Indicator UI:** title bar selalu menampilkan tier — `Kalkulator RAB v1.3.0 — Custom (PT TTS)` atau `Kalkulator RAB v1.3.0 — Generic (Pro)`. Mencegah end-user dan support bingung saat troubleshooting.

**Default fallback:** kalau `edition.manifest.json` hilang (mis. file di-tamper), app refuse boot dengan pesan "Manifest edisi tidak valid — hubungi support". Tidak ada "demo mode anonymous".

---

## 3. License Design Extension

License Ed25519 existing diperluas dengan claims berikut. Format JWT-style (header.payload.signature, base64url) supaya gampang debug:

```json
{
  "sub": "license-uuid-here",
  "customerName": "PT Tritunggal Swarna",
  "issuedAt":  "2026-06-01T00:00:00Z",
  "expiresAt": "2027-06-01T00:00:00Z",
  "hardwareFingerprint": "sha256-abc...",

  // BARU
  "editionId":   "custom-tts-panel-v1",
  "editionTier": "Custom",
  "industry":    "panel-electrical",
  "licenseModel": "perpetual",
  "seats": 5,
  "features": {
    "maxEstimationsPerMonth": -1,
    "maxProductsInLibrary":   -1,
    "exportPdf":     true,
    "exportWord":    true,
    "exportExcel":   true,
    "multiCompany":  false,
    "customBranding": false,
    "apiAccess":     false,
    "watermarkOutput": false
  },
  "graceDays": 14
}
```

Contoh Generic tier "Basic":

```json
{
  "editionTier": "Generic",
  "industry": null,
  "licenseModel": "subscription-monthly",
  "seats": 1,
  "features": {
    "maxEstimationsPerMonth": 50,
    "maxProductsInLibrary":   2000,
    "exportPdf":     true,
    "exportWord":    false,
    "exportExcel":   true,
    "multiCompany":  false,
    "customBranding": true,
    "watermarkOutput": true
  },
  "graceDays": 7
}
```

**Enforcement points:**

| Claim | Enforced di |
|---|---|
| `expiresAt` + `graceDays` | App boot (`LoginForm.OnLoad`) |
| `hardwareFingerprint` | App boot, re-check setiap 24 jam |
| `editionId` ≠ manifest | App boot (§2 cross-check) |
| `maxEstimationsPerMonth` | `EstimationRepository.Save()` — count this month, reject kalau lewat |
| `maxProductsInLibrary` | Import wizard + `ProductEditDialog.Save` |
| `exportWord/exportExcel` | Hide tombol di toolbar + reject di service layer |
| `watermarkOutput` | `PdfLetterExport` overlay diagonal "TRIAL/BASIC" 30% opacity |
| `seats` | License server activation count (online check 1x/minggu) |

**Grace period:** 14 hari setelah expiry untuk Custom (Customer enterprise butuh waktu renewal PO), 7 hari untuk Generic subscription. Setelah grace habis, app jadi read-only — bisa lihat riwayat tapi tidak bisa simpan estimasi baru.

---

## 4. Branding System

### Struktur Folder

```
%ProgramFiles%\TritunggalSwarna\RABCalculator\
├── RABCalculator.exe
├── edition.manifest.json
├── branding/                 ← Custom: dibundel di installer, read-only
│   ├── logo.png              ← header/title bar (PNG transparent, ≥256px)
│   ├── letterhead.jpg        ← PDF banner atas (JPEG, 2480×400px @300dpi)
│   ├── footer.jpg            ← PDF banner bawah
│   ├── cap-stempel.png       ← Signature stamp (PNG transparent)
│   └── theme.json            ← warna primary/secondary, font (opsional)

%AppData%\PanelCalculator\
├── PanelCalculator.db
└── branding-override/        ← Generic only, user-uploaded
    ├── logo.png
    └── letterhead.jpg
```

### Format & Validation

| Asset | Format | Size limit | Validation |
|---|---|---|---|
| logo | PNG 32-bit (alpha) | ≤500KB, ≥256×256 | magic bytes, dimensi |
| letterhead | JPEG/PNG | ≤2MB, aspect 4.6:1 | dimensi + DPI |
| stamp | PNG 32-bit | ≤300KB | alpha channel required |
| theme.json | JSON | ≤5KB | schema validation |

### Loading Strategy

```csharp
public interface IBrandingProvider
{
    Image GetLogo();
    Image GetLetterhead();
    Image GetFooterBanner();
    Image GetSignatureStamp();
    ThemeColors GetTheme();
    CompanyProfile GetCompanyProfile();
}

// Custom edition: read dari install dir, cache di memory, refuse user upload
// Generic edition: priority chain
//   1. branding-override/ user uploaded
//   2. fallback ke generic default di Assets/ (embedded resource)
```

**Penting:** untuk Custom edition, `IBrandingProvider` di-resolve ke `BundledBrandingProvider` yang **immutable** — Settings form tidak punya "Upload Logo" button sama sekali (lihat §5).

---

## 5. Settings UI Diferensiasi

Pakai 1 SettingsForm dengan tab/section yang **dynamically hidden** berdasarkan `EditionContext.Policy`. Cleaner daripada 2 form terpisah.

### Custom Edition (PT TTS sekarang)

```
┌─ Settings ─────────────────────────────────────────┐
│ [Profil Perusahaan]   ← READ-ONLY, info display    │
│   Nama:    PT Tritunggal Swarna                    │
│   NPWP:    01.234.567.8-901.000                    │
│   Alamat:  Jl. Industri No. 123, Bandung           │
│   (Edisi Custom — kontak support untuk perubahan)  │
│                                                    │
│ [Signer Default]      ← EDITABLE per-user          │
│   Nama:    [Kuntjoro Handoko_____]                 │
│   Jabatan: [Direktur_____________]                 │
│   Kota:    [Bandung______________]                 │
│                                                    │
│ [User Management]     ← admin only                 │
│ [Cek Update]                                       │
│ [Tentang]  Versi 1.3.0 — Custom (PT TTS)           │
└────────────────────────────────────────────────────┘
```

Tidak ada: Upload Logo, Edit Library, Import CSV (kecuali admin sync dari developer), Edit NPWP/alamat.

### Generic Edition

```
┌─ Settings ─────────────────────────────────────────┐
│ [Profil Perusahaan]   ← FULLY EDITABLE             │
│   Logo:    [logo.png         ] [Upload...] [Hapus] │
│   Letterhead PDF: [...] [Upload...]                │
│   Nama:    [____________________]                  │
│   NPWP:    [____________________]                  │
│   Alamat:  [____________________]                  │
│   Phone:   [____________________]                  │
│   Warna primer: [#1F4E79  ] [Pilih...]             │
│                                                    │
│ [Library Produk]                                   │
│   Total produk: 1.234                              │
│   [Import CSV/Excel...] [Export...] [Edit Manual]  │
│   Industri preset: [Panel Listrik ▼] [Apply]       │
│                                                    │
│ [Multi-Company]  ← Pro tier only                   │
│   Profile aktif: [PT ABC ▼] [+ Tambah Profile]     │
│                                                    │
│ [Subscription]                                     │
│   Tier: Pro (sampai 1 Juni 2027)                   │
│   Sisa estimasi bulan ini: 38/100  [Upgrade...]    │
│                                                    │
│ [User Management]                                  │
│ [Cek Update]                                       │
└────────────────────────────────────────────────────┘
```

### Implementasi gate

```csharp
public interface IEditionPolicy
{
    bool CanEditBranding { get; }
    bool CanEditCompanyProfile { get; }
    bool CanEditLibrary { get; }
    bool CanUseMultiCompany { get; }
    bool CanExportFormat(ExportFormat fmt);
    void EnforceEstimationQuota(int currentMonthCount);
    Image ApplyWatermarkIfNeeded(Image source);
}

// SettingsForm.OnLoad
panelBranding.Visible       = policy.CanEditBranding;
panelCompany.ReadOnly       = !policy.CanEditCompanyProfile;
btnImportLibrary.Visible    = policy.CanEditLibrary;
panelMultiCompany.Visible   = policy.CanUseMultiCompany;
panelSubscription.Visible   = manifest.EditionTier == "Generic";
```

Policy juga dipakai di MainForm (toolbar export buttons), EstimationRepository (quota), PdfLetterExport (watermark).

---

## 6. Pricing & Distribution

### Custom Edition

| Item | Detail |
|---|---|
| Model | Per-perusahaan perpetual + maintenance |
| License initial | Rp 25–75 juta (tergantung industri & jumlah produk di library) |
| Annual maintenance | 18% dari license (update + support) |
| Seats | 3–10 default, additional Rp 2 juta/seat/tahun |
| Onboarding | Termasuk: library curation, branding setup, training 2 sesi |
| Distribution | Installer custom dengan manifest+library pre-loaded, dikirim via download link bertoken |
| Update | Manual approval — developer push patch, customer schedule install |
| Library updates | 2x/tahun (vendor pricelist refresh), included in maintenance |
| Target | Perusahaan enterprise dengan industri tertentu yang library-nya butuh kurasi (panel listrik, MEP, carrosserie, dll) |

### Generic Edition

| Tier | Harga | Fitur kunci |
|---|---|---|
| **Free / Trial** | Rp 0 — 30 hari | 20 estimasi/bulan, 500 produk, watermark output, no Word/Excel export |
| **Basic** | Rp 149rb/bulan atau Rp 1.5jt/tahun | 100 estimasi/bulan, 2.000 produk, PDF+Excel, custom branding, 1 user |
| **Pro** | Rp 399rb/bulan atau Rp 3.9jt/tahun | Unlimited estimasi, 10.000 produk, semua format export, 3 users, multi-company (3 profile) |
| **Business** | Rp 899rb/bulan atau Rp 8.9jt/tahun | Unlimited semua, 10 users, multi-company unlimited, API access, prioritas support |
| **Lifetime Pro** | Rp 9.9jt sekali bayar | Pro features, 1 tahun update gratis, lifetime license terlock ke versi tahun pembelian |

**Distribution Generic:**
- Single installer `RABCalculator-Generic-Setup.exe` di website
- License activation online (gumroad/Xendit checkout → email license key → paste di app)
- Online check 1x/minggu untuk subscription tier; offline grace 7 hari
- Lifetime tier offline forever, hanya online check 1x saat aktivasi

**Upsell path:** Free → Basic (popup saat hit quota) → Pro (saat butuh multi-company atau Word export) → Custom Edition (saat user butuh industry library curated + branding embedded permanen).

---

## Ringkasan Migrasi Path dari Codebase Sekarang

| Step | Effort | Komponen |
|---|---|---|
| 1. Extract company info dari hardcode → `edition.manifest.json` | 1 hari | Search "Tritunggal Swarna", "Kuntjoro", letterhead path → ganti ke `EditionContext.Manifest.Customer.*` |
| 2. Tambah `SignedManifestLoader` (Ed25519) | 2 hari | Reuse infrastructure license verifier existing |
| 3. Tambah `IEditionPolicy` + 2 implementasi | 2 hari | Custom + Generic policy class |
| 4. Refactor SettingsForm pakai policy gates | 2 hari | Hide/show panel based on `policy.Can*` |
| 5. Extend license claims schema + `LicenseVerifier` | 1 hari | Sudah punya Ed25519 infra, tinggal tambah field |
| 6. Generic edition branding upload + override path | 3 hari | New `IBrandingProvider` impl + UI |
| 7. Quota enforcement (estimation count/month) | 1 hari | Repository layer |
| 8. Watermark untuk Free/Basic | 1 hari | `PdfLetterExport` overlay |
| 9. Activation server (Xendit + license issuer) | 5 hari | Web service terpisah, Node/Go simple |
| 10. Generic installer + landing page | 3 hari | Inno Setup variant + marketing site |

**Total ~3 minggu engineering** untuk dual-edition launch, dengan codebase tunggal dan zero risk merusak Custom edition PT TTS yang sudah jalan.