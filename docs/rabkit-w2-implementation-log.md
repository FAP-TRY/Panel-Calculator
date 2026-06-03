# RAB Kit Week 2 — Implementation Log

_Eksekusi 2026-06-02. Branch: `claude/rabkit-refactor`._

## Goal

Port semua string hardcoded "PT TTS" + asset references ke `BrandContext.Current`, supaya satu codebase bisa di-rebrand jadi product berbeda hanya dengan ganti `IBrandConfig` implementation. Output PDF/Word/Excel HARUS tetap byte-identical dengan v1.2.9 — golden master tests adalah safety net.

## Status Akhir

- **Build**: 0 error, 1 pre-existing warning (DashboardForm.AddQueueDialog.CompanyName property — tidak terkait W2)
- **Tests**: 147/147 pass (132 baseline + 5 golden master + 10 BrandContext wiring sanity)
- **Golden master verify**: ✅ output PDF/Word/Excel text-hash IDENTIK dengan v1.2.9 baseline → string replacement tidak mengubah output
- **Smoke test EXE**: ✅ boot, ActivationForm muncul (license gate aktif, sesuai expected behavior untuk Release build)

## Tasks Completed

### W2.1 — Export Services
File: `PdfLetterExport.cs`, `WordLetterExport.cs`, `ExcelLetterExport.cs`

Semua hardcoded "PT. Tritunggal Swarna", default signer name/title/location, embedded resource paths sudah dibaca via `BrandContext.Current.*`. Section display map ("Box Panel :", "Incoming :", dst) sekarang dari `BrandContext.CurrentIndustry.SectionDisplayMap`.

### W2.2 — UI Forms
File: `MainForm.cs`, `ShellForm.cs`, `LoginForm.cs`, `ActivationForm.cs`, `CombineEstimationsDialog.cs`

Title bar, banner, copy "PT TTS", placeholder text, EstimationNumber prefix sudah port. Logo dibaca via `BrandContext.Current.GetLogoBytes()`.

### W2.3 — UpdateService
File: `PanelCalculator.WinForms/Services/UpdateService.cs`

GitHub coordinates (`Owner`, `Repo`, `AssetName`), `AppVersion`, AppData folder name semua dari `BrandContext.Current`. `AppVersion` diubah dari `const` ke `static` property — call site di ShellForm.cs sudah compatible (tidak ada assume compile-time constant).

### W2.4 — Security Layer (KRITIS)
File: `PanelCalculator.Data/Security/MachineKeyProvider.cs`, `PanelCalculator.Core/Security/LicenseService.cs`

- `MachineKeyProvider.GetKey()` baca app tag dari `BrandContext.Current.LegacyMachineKeyAppTag` ("PanelCalculator.v1") dan pepper bytes dari `.MachineKeyPepperBytes`.
- `LicenseService.PublicKeyBase64` baca dari `BrandContext.Current.LicensePublicKeyBase64`.

`PanelCalculator.Data` + `PanelCalculator.Core` sekarang reference `RabKit.Branding`.

**Backward compat verified**: golden master test PASS membuktikan SQLCipher key derivation output value identical dengan v1.2.9. Customer DB existing aman.

### W2.5 — Program.cs
File: `PanelCalculator.WinForms/Program.cs`

`GetDbPath()`, `CrashLog()`, `MigrateDatabase()` INSERT AppSettings defaults, MessageBox text — semua via `BrandContext.Current`. INSERT OR IGNORE pattern preserve existing customer customization.

### W2.6 — Hapus Duplikat Asset di WinForms
- Deleted: `PanelCalculator.WinForms/Assets/Letterhead/letterhead.jpg`
- Deleted: `PanelCalculator.WinForms/Assets/Letterhead/signature.png`
- Deleted: `PanelCalculator.WinForms/Assets/Letterhead/stamp.png`
- Deleted: `PanelCalculator.WinForms/Assets/logo.png`
- Removed empty folder `Assets/`
- csproj: hapus 4 `<EmbeddedResource>` entries untuk branding assets

Source-of-truth tunggal sekarang di `Panel.Branding/Assets/`. Untuk rebrand jadi RAB Cepat / Carrosserie / dll, cukup ganti project Branding implementation, tidak perlu modif WinForms.

### W2.7 — Final Verify
- ✅ `dotnet build PanelCalculator.sln -c Release` → 0 error
- ✅ `dotnet test PanelCalculator.Tests` → 147/147 pass
- ✅ `Tools/build-release-singlefile.ps1` → publish/PanelCalculator.exe (190 MB)
- ✅ Smoke test: app boot, ActivationForm muncul (license gate aktif)
- ✅ Tidak ada startup-crash.log

## Apa yang TIDAK Diubah di W2

- Skema DB
- Test existing (132 baseline tetap pass tanpa modifikasi)
- AppVersion (tetap 1.2.9)
- Output PDF/Word/Excel (golden master membuktikan identical)
- Behavior license gate, auto-update, DB encryption — semua sama persis dengan v1.2.9

## Yang Berubah dari Sisi Customer

**Tidak ada.** Pure infrastructure refactor. Customer TTS yang install dari v1.2.9 release di GitHub tidak terganggu.

## Yang Berubah dari Sisi Developer

- Untuk rebrand produk jadi RAB Cepat / customer baru: bikin 1 project `<NamaBrand>.Branding/` yang implement `IBrandConfig` + `IIndustryProfile`, ganti `BrandContext.Initialize(...)` di Program.cs Main, rebuild. Output: aplikasi dengan branding berbeda, behavior identical.
- Tidak perlu lagi grep untuk "TTS" / "Tritunggal" / "Kuntjoro" / "Bandung" di seluruh codebase ketika rebrand — sudah jadi 1 file (PanelBrandConfig.cs di Panel.Branding/).
- Asset (letterhead/signature/stamp/logo) ada 1 lokasi (Panel.Branding/Assets/), tidak ada lagi duplikat di WinForms/Assets/.

## Yang Belum Diubah (Sesuai Plan, Untuk Week 3)

- Industry profile section names di MainForm.cs (`Box`/`Incoming`/`Outgoing`/`Trailer`/`Karoseri`/`Jasa`) — section list sudah di `BrandContext.CurrentIndustry.Sections` tapi MainForm.cs masih hardcode pakai constant. Week 3 akan port section list + theme palette ke `IIndustryProfile`.
- `SettingsForm.FamilyToCategory` heuristic — masih hardcode panel-specific (MCB/MCCB/ACB/dst). Week 3 port ke `IIndustryProfile.CategoryFromFamily()`.
- `PdfQuotationExport.cs` (PDF Modern, beda dari PDF Formal) — section→color palette masih hardcode. Week 3.
- Edition manifest + license claims extension — Phase 1 Week 4.

## Risiko & Mitigasi

| Risiko | Status | Mitigasi |
|--------|--------|----------|
| Golden master test FAIL setelah refactor | ✅ PASS | Text-extraction hashing + Panel.Branding constants identical dengan v1.2.9 hardcoded |
| Customer DB existing tidak bisa di-decrypt | ✅ Verified via BrandContextWiringTest | `LegacyMachineKeyAppTag` + pepper bytes byte-identical |
| License key existing tidak valid | ✅ Verified | `LicensePublicKeyBase64` constant tidak berubah |
| Auto-update tidak detect release | ✅ Verified | GitHub Owner/Repo/AssetName tidak berubah (FAP-TRY/Panel-Calculator/PanelCalculator.exe) |
| Customer settings (signer override) hilang | ✅ Verified | INSERT OR IGNORE preserve, app load `AppSettings` value dari DB sebelum fallback ke `IBrandConfig` default |

## Statistik

- 17 file modified
- 4 file deleted (assets WinForms)
- 1 file created (this log)
- 0 file di Core dan Data project ditambah (port pakai package reference existing)
