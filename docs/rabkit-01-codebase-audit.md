# Rabkit 01 Codebase Audit

_Generated 2026-05-28 dari workflow `generic-rab-calculator-plan` (4 agents)._

---

Now I have a complete picture. Producing the audit.

# Audit Codebase Panel Calculator — Generic vs Panel-Specific

## 1. File / Class / Method Classification

Legend: **A** = panel-specific (PT TTS / panel listrik domain), **B** = generic-reusable (any RAB industry).

### PanelCalculator.Core

| File / Class | A/B | Alasan |
|---|---|---|
| `Core/Models/Product.cs` | **B** | Entitas generik (Category/Code/Name/Spec/Price/Vendor) — cuma comment di line 12 sebut MCB/Box/RCCB; data shape applicable ke katalog material apa pun. |
| `Core/Models/Estimation.cs` | **B** | Header RAB generik (ClientName, NomorSurat, ProjectName, SubTotal, Margin1-3, Tax, PPh, ShippingCost, Status) — semua field reusable, tidak mention panel. |
| `Core/Models/EstimationDetail.cs` | **A**(ringan) | Generik (Qty/UnitPrice/Adj1-3/Satuan) tapi `Section` default `"Material Utama"` dan dipakai dengan vocab section panel (Box/Incoming/Outgoing) di UI. Bisa di-genericize. |
| `Core/Models/User.cs` | **B** | Auth user dengan role Admin/Operator — generic. |
| `Core/Models/AppSettings.cs` | **B** | KV store generik. |
| `Core/Services/CalculationService.cs` | **B** | Pure math (line total, subtotal, margin 3-tier, tax, final) — zero domain coupling. Premium reuse candidate. |
| `Core/Services/CombinedQuotationCalculator.cs` | **B** | Multi-line aggregator (DPP + PPN once + sum PPh) — istilah "panel" hanya di docstring/`PanelLine`/`PanelSubtotal` (renaming kosmetik); logic generic. |
| `Core/Services/TerbilangFormatter.cs` | **B** | Number-to-words Bahasa Indonesia + suffix " rupiah" — reusable di semua RAB Indonesia. |
| `Core/Security/LicensePayload.cs` | **B** | Ed25519 + Crockford Base32 license envelope — generic. |
| `Core/Security/LicenseService.cs` | **B (tapi `PublicKeyBase64` di line 34 hardcoded PT TTS)** | Logic generic; satu konstanta produksi yang harus di-config-kan per produk. |
| `Core/Security/PasswordHasher.cs` | **B** | BCrypt + SHA-256 legacy fallback — generic. |

### PanelCalculator.Data

| File / Class | A/B | Alasan |
|---|---|---|
| `Data/PanelCalculatorContext.cs` | **A** (naming) / **B** (schema) | DbContext name + namespace coupling; tapi tabel & relasi generic. Seed default di line 87–95 berisi default `CompanyName="PT Electrical Supplies"` (placeholder generik). |
| `Data/Repositories/BaseRepository.cs` | **B** | CRUD generic. |
| `Data/Repositories/IRepository.cs` | **B** | Interface generic. |
| `Data/Repositories/ProductRepository.cs` | **B** | Search/GetByCategory/GetByReferenceCode — generic. |
| `Data/Repositories/EstimationRepository.cs` | **B** | GetByEstimationNumber/Status/Client/DateRange — generic. |
| `Data/DataSeeding/ProductSeeder.cs` | **B** | Upsert CSV+Excel + Indonesian decimal converter + bilingual header aliases (kategori/kode/harga). Reusable as-is. |
| `Data/DataSeeding/EstimationCsvImporter.cs` | **B** | 3-section CSV importer (meta/items/summary) — generic. |
| `Data/Security/MachineKeyProvider.cs` | **A** (pepper only) / **B** (logic) | WMI + MachineGuid hashing generic; constant `"PanelCalculator.v1"` (line 56) and the XOR pepper `"TTS-PanelCalc-pepper-2026-v1"` (lines 174-183) are app-branded — extract to product config. |
| `Data/Security/DbMigrator.cs` | **B** | Plain→SQLCipher migration via `sqlcipher_export` — generic. |
| `Data/Security/UpdateVerifier.cs` | **B** (logic) / **A** (asset filename literal only at PdfLetterExport-side) | SHA-256 + host allowlist generic; `AllowedDownloadHosts` is GitHub-only which is generic (vendor-agnostic). |
| `Data/Migrations/ProductsIndexMigrator.cs` | **B** | Rebuild Products table to relax UNIQUE → composite — generic schema migration tool. Comment mentions PT TTS but logic is pure SQLite. |

### PanelCalculator.WinForms — Forms

| File / Class | A/B | Alasan |
|---|---|---|
| `Forms/MainForm.cs` (`MainForm` shell) | **A** | Title `"Kalkulator Panel Tritunggal Swarna"` (line 104), brand banner `"TRITUNGGAL SWARNA"` (line 153), hardcoded section list `Box/Incoming/Outgoing/Trailer/Karoseri/Jasa` (lines 37-41, 479-480), section dark-tinted palette per kategori (lines 1980-2021), placeholder "Panel MDP 3-Phase 400A" (line 601), category/EstNumber prefix `EST-YYYYMMDD-###` (line 1429). UI logic itself (grid, margin tiers, save flow) is generic. |
| `Forms/MainForm.Designer.cs` | **B** | Just designer scaffolding, no domain text. |
| `Forms/EstimationHistoryForm.cs` | **B** | Generic list/filter/export — domain-free; uses `EstimationNumber` & `NomorSurat` from model. |
| `Forms/SettingsForm.cs` | **B (mostly)** | UI generic (Import CSV/Excel, user mgmt, update check, license display). One panel-domain leak: family→category heuristic in `FamilyToCategory` (lines 925–944) hard-codes MCB/MCCB/ACB/RCCB/Busbar/VSD/ATS/Surge — pure panel domain. |
| `Forms/SaveEstimationDialog.cs` | **B** | Generic save (client/phone/company/address/notes). |
| `Forms/ProductEditDialog.cs` | **B** | Generic product editor (uses CSV columns). |
| `Forms/CombineEstimationsDialog.cs` | **A** (docstring/labels) | UI labeling "Surat Penawaran Multi-Panel" but the multi-line aggregation flow is generic. |
| `Forms/ShippingCalculatorDialog.cs` | **B** | Indonesian ekspedisi calc (Manual/Per Kg/Kubikasi/Per Unit) — applicable to any logistics RAB. |
| `Forms/DashboardForm.cs` | **B** | Kanban Draft→Approved — generic CRM/pipeline. |
| `Forms/ReportsForm.cs` | **B** | Generic reports (Ringkasan Penjualan, Pipeline Status). |
| `Forms/LoginForm.cs` | **A** (title only) | Title "Kalkulator Panel Tritunggal Swarna" (line 30). Auth logic generic. |
| `Forms/ShellForm.cs` | **A** (title + brand) | Title `$"Kalkulator Panel Tritunggal Swarna — v{...}"` (line 52), logo embedded resource path `PanelCalculator.WinForms.Assets.logo.png` (line 86). |
| `Forms/ActivationForm.cs` | **A** (copy) | Hardcoded "PT TTS", "PT Tritunggal Swarna", "Kalkulator Panel TTS" copy (lines 14, 82, 117, 135, 217, 259, 280); `SupportWhatsAppNumber = "628XXXXXXXXXX"` placeholder (line 25). Activation flow itself is generic. |
| `Forms/UserManagementForm.cs` | **B** | Generic user CRUD. |

### PanelCalculator.WinForms — Services

| File / Class | A/B | Alasan |
|---|---|---|
| `Services/LicenseGate.cs` | **B** | License gating + dev-bypass flag + env var enforce — generic. |
| `Services/UpdateService.cs` | **A** | Hardcoded `Owner="FAP-TRY"`, `Repo="Panel-Calculator"` (lines 29-30), asset names `PanelCalculator.exe` + `.sha256` (lines 35-36), AppDataDir `"PanelCalculator"` (line 385), `AppVersion="1.2.9"` (line 26). Update mechanic itself is generic. |
| `Services/PdfLetterExport.cs` | **A** | PT TTS letterhead pixel-match: hardcoded literal `"PT. Tritunggal Swarna"` (line 431), default signer `"Kuntjoro Handoko"` + title `"Direktur"` + city `"Bandung"` (lines 100-102, 186-188, 401), section-display mapping `Box/Incoming/Outgoing/Lainnya` (lines 591–615), embedded resource paths `PanelCalculator.WinForms.Assets.Letterhead.{letterhead.jpg,signature.png,stamp.png}` (lines 452-453, 632), conditional phrase `"Harga loco Pabrik"` when city=Bandung (line 368), copy "DP 30% saat PO kami terima…" (line 370). |
| `Services/WordLetterExport.cs` | **A** | Same pattern as PdfLetterExport — hardcoded signer/city/section labels (lines 86, 88, 92, 150-162, 181, 246, 406-460), embedded letterhead/signature/stamp resources. |
| `Services/ExcelLetterExport.cs` | **A** | Hardcoded company default `"PT. TRITUNGGAL SWARNA"` (lines 77, 345), offer city `"Bandung"` (lines 82, 349), copy "Penawaran Harga Multi-Panel" (line 372), same section list as Pdf/Word. |
| `Services/PdfQuotationExport.cs` | **A** (lite) | Section→color palette hardcodes panel domain vocab (Box/Incoming/Outgoing/Trailer/Karoseri/Jasa, lines 27-46, 146-147, 313-318). Color/layout logic generic, but the keys are panel-specific. |

### Theme / Controls / Program

| File / Class | A/B | Alasan |
|---|---|---|
| `Theme/AppTheme.cs` | **B** | Dark-pro design tokens — no domain text. |
| `Controls/RoundedPanel.cs` | **B** | Generic rounded panel. |
| `Program.cs` | **A** (mostly hardcoded paths) | AppData dir `"PanelCalculator"` (lines 63, 297), crash log title `"Kalkulator Panel — Crash"` (line 48) / `"Kalkulator Panel — Migrasi Gagal"` (line 108), seed AppSettings defaults `SignerName=Kuntjoro Handoko / SignerTitle=Direktur / OfferLocation=Bandung / CompanyName=PT. Tritunggal Swarna` (lines 246-249), default admin seed generic. |

### Installer / Build

| File | A/B | Alasan |
|---|---|---|
| `PanelCalculator.iss` | **A** | AppName/Publisher/InstallDir all `Tritunggal Swarna` (lines 2, 5, 7, 21). |
| `PanelCalculator.WinForms.csproj` | **A** (asset embed only) | Embedded resources reference PT TTS letterhead JPG/PNG. |

---

## 2. Hardcoded PT TTS Strings — Extract to Config

These literals are sprinkled across the codebase and MUST be promoted to `BrandConfig` / `appsettings.json` / DB settings to support multi-tenant deployment:

### A. Company identity
| String | Location |
|---|---|
| `"PT. Tritunggal Swarna"` | `PdfLetterExport.cs:431`, `Program.cs:249`, `ActivationForm.cs:82,217` |
| `"PT. TRITUNGGAL SWARNA"` | `ExcelLetterExport.cs:77,345` |
| `"TRITUNGGAL SWARNA"` (logo brand band) | `MainForm.cs:153` |
| `"Tritunggal Swarna"` (window title) | `MainForm.cs:104`, `LoginForm.cs:30`, `ShellForm.cs:52`, `PanelCalculator.iss:2,5,7,21` |
| `"PT TTS"` / `"Kalkulator Panel TTS"` | `ActivationForm.cs:14,117,135,259,280`, `PdfLetterExport.cs:21,385`, `WordLetterExport.cs:17` |
| Default seed `CompanyName="PT Electrical Supplies"`, `CompanyAddress="Jakarta, Indonesia"`, `CompanyPhone="+62-21-xxxx-xxxx"` | `PanelCalculatorContext.cs:90,93,94` |

### B. Default signer / location
| String | Location |
|---|---|
| Default `SignerName="Kuntjoro Handoko"` | `PdfLetterExport.cs:100,186`, `WordLetterExport.cs:86,150`, `Program.cs:246` |
| Default `SignerTitle="Direktur"` | `PdfLetterExport.cs:101`, `Program.cs:247` |
| Default `OfferLocation="Bandung"` | `PdfLetterExport.cs:102,188,360,401`, `WordLetterExport.cs:88,152,406,438`, `ExcelLetterExport.cs:82,349`, `Program.cs:248` |
| Conditional phrase `"Harga loco Pabrik"` when city == Bandung | `PdfLetterExport.cs:368`, `WordLetterExport.cs:414` |

### C. Branded assets / paths
| Resource | Location |
|---|---|
| `PanelCalculator.WinForms.Assets.Letterhead.letterhead.jpg` | `PdfLetterExport.cs:632`, `WordLetterExport.cs:246` |
| `PanelCalculator.WinForms.Assets.Letterhead.signature.png` | `PdfLetterExport.cs:452`, `WordLetterExport.cs:459` |
| `PanelCalculator.WinForms.Assets.Letterhead.stamp.png` | `PdfLetterExport.cs:453`, `WordLetterExport.cs:460` |
| `PanelCalculator.WinForms.Assets.logo.png` | `MainForm.cs:145`, `ShellForm.cs:86` |
| AppData dir literal `"PanelCalculator"` | `Program.cs:63,297`, `UpdateService.cs:21,385`, `DbMigrator` log path |
| DB filename `PanelCalculator.db` | `Program.cs:297` |

### D. Auto-update / branding
| String | Location |
|---|---|
| GitHub `Owner="FAP-TRY"`, `Repo="Panel-Calculator"` | `UpdateService.cs:29-30` |
| Asset names `PanelCalculator.exe` + `.sha256` | `UpdateService.cs:35-36,107-108,135-138` |
| `AppVersion="1.2.9"` (should be assembly version) | `UpdateService.cs:26` |
| Ed25519 production public key (PT TTS keypair) | `LicenseService.cs:34` |
| MachineKey pepper "TTS-PanelCalc-pepper-2026-v1" | `MachineKeyProvider.cs:174-183` |
| MachineKey constant `"PanelCalculator.v1"` | `MachineKeyProvider.cs:56` |
| WhatsApp placeholder `"628XXXXXXXXXX"` + greeting "Halo PT Tritunggal Swarna…" | `ActivationForm.cs:25,217` |

### E. Panel-domain vocab leaking into UI/export
| Domain token | Location |
|---|---|
| Section names `Box / Incoming / Outgoing / Trailer / Karoseri / Jasa / Material Utama / Material Pendukung / Material Lainnya` | `MainForm.cs:37-41,479-480,1980-2021`, `PdfLetterExport.cs:30-34,591-615`, `WordLetterExport.cs:36-37,598-619`, `ExcelLetterExport.cs:25-29`, `PdfQuotationExport.cs:146-147,313-318` |
| Category heuristics (MCB/MCCB/ACB/RCCB/Busbar/VSD/ATS/Surge/Kontaktor/Motor CB/Box) | `SettingsForm.cs:925-944` |
| Placeholder `"Panel MDP 3-Phase 400A"` | `MainForm.cs:601` |
| EstimationNumber prefix pattern `EST-YYYYMMDD-###` | `MainForm.cs:1429` |
| File-name templates `SuratPenawaran_*.pdf` / `Penawaran_*.pdf` | `MainForm.cs:1538-1539` |
| Title `"Pipeline Estimasi"` / Status flow Antri/Draft/Tunggu/Approved | `DashboardForm.cs:19-25,50` (kanban steps — borderline generic) |

---

## 3. Rekomendasi: Project Structure Baru (Extract Generic Engine)

Pisahkan menjadi **engine generic** (`RabKit.*`) + **vertical pack** (`Panel.*`) + **app shell** yang bisa di-rebrand per industri:

```
RabKit.Core/                       — pure domain (zero UI, zero industry vocab)
  Models/
    Product.cs                     ← rename: kolom tetap, drop comment "MCB/Box"
    Estimation.cs                  ← header generic (no change)
    EstimationDetail.cs            ← Section dijadikan string bebas, tanpa default
    User.cs, AppSettings.cs
  Calc/
    LineTotal, Margin3Tier, Tax, CombinedQuotation (PanelLine → QuotationLine)
  Format/
    TerbilangFormatter.cs          ← rupiah-able, parametrize currency suffix
  Security/
    LicensePayload, LicenseService (PublicKey via IBrandConfig)
    PasswordHasher

RabKit.Data/                       — schema + repos + import/export
  RabKitContext.cs                 ← DbSet generic; seed via IBrandConfig
  Repositories/{Product,Estimation,Base,IRepository}
  Seeding/{ProductSeeder, EstimationCsvImporter, IndonesianDecimalConverter}
  Security/{DbMigrator, MachineKeyProvider (pepper via IBrandConfig),
            UpdateVerifier}
  Migrations/ProductsIndexMigrator

RabKit.WinForms/                   — generic UI primitives
  Theme/AppTheme.cs
  Controls/RoundedPanel.cs
  Forms/
    LoginForm, ShellForm (title via IBrandConfig)
    MainForm (sections via IIndustryProfile.Sections)
    EstimationHistoryForm, SettingsForm, SaveEstimationDialog
    ProductEditDialog, CombineEstimationsDialog
    ShippingCalculatorDialog, DashboardForm, ReportsForm
    UserManagementForm, ActivationForm (copy via IBrandConfig)
  Services/
    LicenseGate, UpdateService (owner/repo via IBrandConfig)

RabKit.Export/                     — generic quotation renderers (pluggable template)
  ILetterTemplate                  ← interface for letterhead/signer/copy
  Pdf/PdfLetterExport.cs           ← takes ILetterTemplate
  Pdf/PdfModernExport.cs           ← color palette via ILetterTemplate
  Word/WordLetterExport.cs
  Excel/ExcelLetterExport.cs

RabKit.Branding/                   — abstraction layer
  IBrandConfig
    string AppName             // "Kalkulator Panel TTS" / "Carrosserie Calc"
    string CompanyName, Address, Phone, NPWP
    string DefaultSignerName, SignerTitle, OfferLocation
    string GitHubOwner, GitHubRepo, AssetName, ManifestName
    string Ed25519PublicKeyBase64
    string LicensePepper, MachineKeyAppTag, AppDataFolderName
    byte[] LetterheadJpg, SignaturePng, StampPng, LogoPng
    string WhatsAppSupportNumber, WhatsAppGreeting
  IIndustryProfile
    string[] DefaultSections             // panel: Box/Incoming/…  carro: Wall/Frame/Roof/…
    Dictionary<string,(Color bg, Color fg)> SectionColors
    Dictionary<string,string> SectionDisplayMap   // raw → letter label
    Func<string,string> CategoryHeuristic         // raw family → category
    string EstimationNumberPrefix, PlaceholderPerihal

# Vertical packs (one per industry — replaces hardcoded brand)
Panel.Branding/                    ← PT TTS letterhead, signer, Ed25519 key,
                                     panel section vocab, MCB/MCCB heuristic
Carrosserie.Branding/              ← future: trailer/genset enclosure pack
Generic.Branding/                  ← Jakarta dummy brand for demos

# App
RabKit.Shell.exe/                  ← thin entry: loads IBrandConfig +
                                     IIndustryProfile via DI/config flag
                                     and runs RabKit.WinForms.ShellForm
```

Key refactors needed to enable this split:

1. **`PdfLetterExport`/`WordLetterExport`/`ExcelLetterExport`** — accept `ILetterTemplate` instead of reading `settings` dict + embedded resources. Section display map (Box/Incoming/Outgoing → Lainnya) becomes data-driven.
2. **`PanelCalculatorContext`** — rename, move seed defaults out of `OnModelCreating` into a `SeedFromBrandConfig(IBrandConfig)` method called from `Program.RunApp`.
3. **`MachineKeyProvider`** — replace `"PanelCalculator.v1"` and pepper with values from `IBrandConfig` (read once at startup, cached).
4. **`LicenseService.PublicKeyBase64`** — convert from `const` to `IBrandConfig.Ed25519PublicKeyBase64` lookup.
5. **`UpdateService`** — `Owner`/`Repo`/`AssetName`/`ManifestAssetName`/`AppVersion` from `IBrandConfig`.
6. **`MainForm.Sections` array + section color switches** — read from `IIndustryProfile`. The dark-tinted palette (lines 1980-2021) becomes a `Dictionary<string, SectionTheme>`.
7. **`SettingsForm.FamilyToCategory`** — move to `IIndustryProfile.CategoryHeuristic` (panel pack keeps the MCB/MCCB rules; carrosserie pack supplies SPCC/UNP/IWF rules).
8. **`ActivationForm` copy** — every "PT TTS" / "Tritunggal Swarna" / WhatsApp greeting flows through `IBrandConfig`. The form itself stays.
9. **Assets** — `Assets/Letterhead/*` and `Assets/logo.png` migrate out of `RabKit.WinForms` into vertical brand pack (`Panel.Branding/Assets/...`) loaded via `IBrandConfig` byte streams instead of `GetManifestResourceStream`.
10. **AppData folder + DB filename** — derived from `IBrandConfig.AppDataFolderName` so two verticals can coexist on the same machine without colliding.

Estimated reuse ratio after refactor: ~75% of current LOC moves to `RabKit.*` (generic), ~25% stays in `Panel.Branding` (PT TTS letterhead pixel-match, panel section vocab, MCB heuristic, PT TTS Ed25519 keypair, GitHub release coordinates).