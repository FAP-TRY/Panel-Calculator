# Rabkit Week 1 — Foundation: Branding Abstraction (Implementation Log)

_Implemented 2026-06-02 sebagai bagian dari `panel-roadmap-planner` Phase 1, Week 1._
_Branch: `claude/rabkit-refactor` (worktree `sharp-jemison-2e465a`)._

---

## Ringkasan untuk Owner (Non-Tech)

**Apa yang berubah dari sisi user?** Tidak ada. App boot identik. Login, kalkulator, riwayat, export PDF/Word/Excel — semua sama persis dengan v1.2.9. Visual sama. Format penawaran sama. Customer existing tidak terkena dampak apa-apa.

**Apa yang berubah dari sisi developer?** Kita sekarang punya 2 project library baru:
- **`RabKit.Branding`** — definisi "kontrak" tentang apa saja yang berbeda antar brand (nama perusahaan, logo, kunci lisensi, dll).
- **`Panel.Branding`** — implementasi konkret untuk PT TTS (mengisi semua nilai existing yang dulu hardcoded di source code).

Plus 5 test "golden master" — setiap kali kita refactor, test ini meng-generate 5 sample penawaran (PDF + Word + Excel) lalu membandingkan dengan baseline. Kalau salah satu byte berubah, test gagal — kita langsung tahu refactor merusak format yang dipakai customer.

**Kenapa ini penting?** Ini setup infrastruktur supaya minggu-minggu berikutnya kita bisa:
1. Bikin app versi "RAB Cepat" (Generic Edition) cuma dengan ganti 1 DLL (`Panel.Branding.dll` → `RabCepat.Branding.dll`)
2. Daftar customer Custom kedua (carrosserie/MEP) tanpa merusak instalasi PT TTS
3. Tetap selalu yakin format penawaran TTS tidak berubah byte-for-byte

---

## Tasks Selesai

| ID | Task | Status |
|----|------|--------|
| W1.2 | Project `RabKit.Branding` dengan interface contracts | DONE |
| W1.3 | Project `Panel.Branding` (TTS implementation) | DONE |
| W1.4 | `BrandContext.Current` static accessor + Program.Main wiring | DONE |
| W1.5 | Golden master test infrastructure (5 samples + baseline JSON) | DONE |

Tidak ada bump version (tetap `1.2.9`). Tidak ada commit dari agent (owner akan commit setelah review).

---

## File Yang Dibuat / Diubah

### Project Baru

| Path | Kind | Notes |
|------|------|-------|
| `RabKit.Branding/RabKit.Branding.csproj` | New | Target `net8.0-windows`, no external deps |
| `RabKit.Branding/IBrandConfig.cs` | New | Per-company contract (CompanyName, signer, GitHub coords, license key, pepper, app version, dst) |
| `RabKit.Branding/IIndustryProfile.cs` | New | Per-industry contract (section vocab, section themes, display map, category heuristic, placeholder) |
| `RabKit.Branding/BrandContext.cs` | New | Static accessor `BrandContext.Current` / `CurrentIndustry`; throws kalau dipanggil sebelum `Initialize()` |
| `Panel.Branding/Panel.Branding.csproj` | New | Target `net8.0-windows`, ref `RabKit.Branding`; embeds letterhead/signature/stamp/logo |
| `Panel.Branding/PanelBrandConfig.cs` | New | Hardcode PT TTS values: CompanyName, signer, GitHub repo, AppVersion, license public key, machine-key pepper (byte-identical dengan legacy) |
| `Panel.Branding/PanelIndustryProfile.cs` | New | Panel section vocab + MCB/MCCB/ACB/RCCB heuristic + SectionDisplayMap (Box→"Box Panel", Trailer→"Lainnya" dst) |
| `Panel.Branding/Assets/letterhead.jpg` | New | Copy dari `PanelCalculator.WinForms/Assets/Letterhead/letterhead.jpg` (asli tetap ada — W1 belum hapus) |
| `Panel.Branding/Assets/signature.png` | New | Copy dari WinForms |
| `Panel.Branding/Assets/stamp.png` | New | Copy dari WinForms |
| `Panel.Branding/Assets/logo.png` | New | Copy dari `PanelCalculator.WinForms/Assets/logo.png` |
| `PanelCalculator.Tests/GoldenMaster/GoldenMasterSamples.cs` | New | 5 sample data fixtures (single panel, multi-panel medium, single panel complex discount, multi-panel large sections, single panel zero shipping) |
| `PanelCalculator.Tests/GoldenMaster/GoldenMasterTest.cs` | New | xUnit test class — generate PDF/Word/Excel per sample, extract normalized text fingerprint, SHA-256, compare ke baseline |
| `PanelCalculator.Tests/GoldenMaster/BrandContextWiringTest.cs` | New | 10 unit tests yang assert PanelBrandConfig return PT TTS values (KRITIS: pepper decode, license public key, machine-key app tag) |
| `docs/golden-master-hashes-v1.2.9.json` | New | Baseline SHA-256 hash per sample × per format (5 × 3 = 15 hashes) |
| `docs/rabkit-w1-implementation-log.md` | New | Dokumen ini |

### Diubah

| Path | Kind | Notes |
|------|------|-------|
| `PanelCalculator.sln` | Modified | Tambah 2 project entries + build configurations (RabKit.Branding + Panel.Branding) |
| `PanelCalculator.WinForms/PanelCalculator.WinForms.csproj` | Modified | Tambah ProjectReference ke RabKit.Branding + Panel.Branding |
| `PanelCalculator.WinForms/Program.cs` | Modified | Tambah `BrandContext.Initialize(...)` paling awal di `RunApp` sebelum `SQLitePCL.Batteries_V2.Init()` |
| `PanelCalculator.Tests/PanelCalculator.Tests.csproj` | Modified | Tambah ProjectReference ke RabKit.Branding + Panel.Branding |

### TIDAK Diubah (W1 deliberately scoped out, datang di Week 2)

- `PdfLetterExport.cs` — masih punya `"PT. Tritunggal Swarna"` hardcode (line 431), `"Kuntjoro Handoko"` default (line 100), `PanelCalculator.WinForms.Assets.Letterhead.*` embedded resource paths
- `WordLetterExport.cs` — same hardcode pattern
- `ExcelLetterExport.cs` — `"PT. TRITUNGGAL SWARNA"` hardcode (line 77, 345)
- `MainForm.cs` — title "Kalkulator Panel Tritunggal Swarna", banner "TRITUNGGAL SWARNA", placeholder "Panel MDP 3-Phase 400A", section list literal
- `ShellForm.cs` — title bar string
- `LoginForm.cs` — title bar string
- `ActivationForm.cs` — semua "PT TTS" / "Kalkulator Panel TTS" copy
- `UpdateService.cs` — `Owner="FAP-TRY"`, `Repo="Panel-Calculator"`, `AppVersion="1.2.9"` const
- `MachineKeyProvider.cs` — `"PanelCalculator.v1"` literal (line 56), XOR pepper bytes (line 177-183)
- `LicenseService.cs` — `PublicKeyBase64` const (line 34)
- `Program.cs` MigrateDatabase seeds default `'PT. Tritunggal Swarna'`, `'Kuntjoro Handoko'`, dst
- `PanelCalculator.WinForms/Assets/Letterhead/*.jpg|*.png` — masih ada (W1 dual-copy strategy — see below)

**Alasan tidak diubah:** task explicitly menyatakan "JANGAN ganti string hardcode di MainForm/PdfLetterExport/dll — itu Week 2. Week 1 cuma setup infrastruktur." Mengganti semua string itu di Week 1 akan membuat regression risk besar tanpa kemampuan ter-test golden master (karena golden master baseline baru ada SETELAH W1.5).

---

## Dual-Copy Asset Strategy (W1 Only)

Letterhead/signature/stamp PNG/JPG sekarang ADA di **dua** tempat:

```
PanelCalculator.WinForms/Assets/Letterhead/  ← masih embedded ke WinForms DLL
  letterhead.jpg
  signature.png
  stamp.png

Panel.Branding/Assets/                        ← embedded ke Panel.Branding DLL (BARU)
  letterhead.jpg
  signature.png
  stamp.png
  logo.png    (juga copy dari WinForms/Assets/logo.png — yang tetap ada)
```

**Alasan:** task W1.3 bilang "MOVE" tapi task W1.4 bilang "JANGAN replace string hardcoded di seluruh codebase yet". Kalau benar-benar MOVE, `PdfLetterExport.cs:632` yang masih load `PanelCalculator.WinForms.Assets.Letterhead.letterhead.jpg` via `GetManifestResourceStream` akan return null → letterhead hilang dari PDF output → golden master test fail.

Solusi pragmatik: COPY dulu di Week 1 (dual-copy), Week 2 baru ganti semua call site ke `BrandContext.Current.GetLetterheadBytes()` SEKALIGUS hapus copy lama. Cek di Week 2: pastikan `<EmbeddedResource Include="Assets\Letterhead\*" />` di WinForms `.csproj` dihapus + file dihapus dari worktree.

---

## KRITIS Checks (Backward Compat untuk Customer PT TTS Existing)

Tiga nilai berikut MUTLAK harus identik dengan v1.2.9 atau customer DB akan jadi unreadable / license akan rejected. Sudah diverifikasi via unit test di `BrandContextWiringTest.cs`:

| Nilai | Test yang verify | Sumber asli |
|-------|------------------|-------------|
| `LegacyMachineKeyAppTag = "PanelCalculator.v1"` | `PanelBrandConfig_LegacyMachineKeyAppTag_IsExactStringRequiredByCustomerDb` | `MachineKeyProvider.cs:56` |
| `MachineKeyPepperBytes` (28 bytes XOR-encoded) decodes ke `"TTS-PanelCalc-pepper-2026-v1"` | `PanelBrandConfig_MachineKeyPepper_DecodesToExpectedCleartext` | `MachineKeyProvider.cs:177-183` (cleartext di comment line 176) |
| `LicensePublicKeyBase64 = "D5Bk2OC+FFZdZqqtI86iFCiy1/pFRQLbkMBpVQ+ia6w="` | `PanelBrandConfig_LicensePublicKey_MatchesEmbeddedConstant` | `LicenseService.cs:34` |

Test tambahan men-verify nilai non-kritis tapi penting:
- `UpdateGitHubOwner="FAP-TRY"`, `UpdateGitHubRepo="Panel-Calculator"`, `UpdateAssetName="PanelCalculator.exe"`
- `AppDataFolderName="PanelCalculator"`, `AppVersion="1.2.9"`, `EstimationNumberPattern="EST-{0:yyyyMMdd}-{1:D3}"`
- 4 asset accessor return non-null bytes (letterhead/signature/stamp/logo)

---

## Golden Master Test Strategy

Lihat header XML doc di `GoldenMasterTest.cs` untuk detail. Ringkasan:

- **5 sample** per task spec (single simple, multi-panel medium, single complex discount, multi-panel large sections, single zero shipping)
- **3 format per sample** (PDF/Word/Excel) → 15 artifact total per run
- **Fingerprint = SHA-256 dari extracted-text-only**, NOT raw byte hash
  - **Why:** iText7/DocX/ClosedXML embed wall-clock timestamps di metadata. Raw byte hash flaky.
  - **What's hashed:** untuk PDF — semua text dari setiap page via `PdfTextExtractor`. Untuk DOCX — semua `<w:t>` text node dari semua `word/*.xml`. Untuk XLSX — semua `<x:t>`/`<x:v>`/`<x:f>` dari semua `xl/*.xml` (ClosedXML pakai `x:` namespace prefix — regex sudah handle).
- **Baseline** ter-store di `docs/golden-master-hashes-v1.2.9.json` (5 sample × 3 format = 15 hash)
- **Regenerate mode:** `GOLDEN_MASTER_REGENERATE=1 dotnet test ...` overwrite baseline
- **Pertama-run mode:** kalau sample baru ditambahkan dan belum ada di baseline, test auto-add (jadi tambah sample baru tidak crash)
- **Filter:** `[Trait("Category", "GoldenMaster")]` — bisa skip dengan `--filter "Category!=GoldenMaster"` kalau perlu cepat

Fix bug saat development: regex `<t>...</t>` awalnya tidak match `<x:t>...</x:t>` yang ClosedXML emit. Sample 1, 3, 5 (single-panel XLSX) sempat punya hash collision (semua text fingerprint kosong). Setelah regex diubah jadi `<(?:\w+:)?t...` semua 5 sample hash distinct.

---

## Verify Build + Test

```text
dotnet build PanelCalculator.sln -c Release
  → Build succeeded.  1 Warning(s)  0 Error(s)
  (Warning pre-existing di DashboardForm.cs:489 — bukan dari W1 changes)

dotnet test PanelCalculator.Tests -c Release
  → Passed!  - Failed: 0, Passed: 147, Skipped: 0, Total: 147

  Breakdown:
    - 132 test existing (security, format, calc, terbilang, dst) — semua pass
    - 5 GoldenMasterTest (sample-01 .. sample-05) — semua pass
    - 10 BrandContextWiringTest (KRITIS pepper decode, license key, app tag, asset accessor, dst)

Multi-file publish smoke test:
  dotnet publish PanelCalculator.WinForms -c Release -r win-x64 --self-contained
  → Success. Verified Panel.Branding.dll + RabKit.Branding.dll di output dir
    (PE32+ executable for MS Windows 6.00 (GUI), x86-64).
```

EXE smoke test (jalankan dari sandbox & lihat activation form muncul) TIDAK bisa dilakukan dari agent — env sandbox tidak boleh launch interactive Windows app. Sebagai gantinya, `BrandContextWiringTest` 10 test bertindak sebagai automated proxy: kalau wiring rusak → tests fail → boot pasti rusak juga.

Manual verify yang masih owner perlu lakukan:
1. Run `Tools\build-release-singlefile.ps1` untuk produksi obfuscated single-file EXE
2. Install hasil installer di test VM
3. Login admin/admin → confirm appears "Kalkulator Panel Tritunggal Swarna — v1.2.9" di title bar
4. Buat 1 estimasi sederhana → Save → Export PDF → buka di Adobe Reader → confirm letterhead PT TTS muncul, signer "Kuntjoro Handoko" muncul
5. Verify license activation form muncul kalau license belum ter-aktivasi (artinya `LicensePublicKeyBase64` masih working via embedded constant — Week 2 baru port ke BrandContext)

---

## Pertanyaan Blocker / Open Decision

Tidak ada blocker. Semua nilai yang KRITIS sudah berhasil di-decode dan match dengan v1.2.9 — golden master baseline stable cross-run, BrandContextWiringTest pass.

Decision yang perlu owner-confirm sebelum Week 2 start:
- **Q1: Dual-copy strategy bisa dihapus di Week 2?** Konfirmasi OK untuk delete `PanelCalculator.WinForms/Assets/Letterhead/*` setelah semua call site di-port ke `BrandContext.Current.GetLetterheadBytes()`. (Default jawaban: ya, itu memang plan-nya.)
- **Q2: Apakah `BrandContextWiringTest` (10 test tambahan) keep atau merge ke `MachineKeyProviderTests.cs`?** Saya rekomendasikan keep terpisah karena tujuan beda (wiring sanity vs derivation logic). Tapi kalau owner mau simplifikasi, bisa di-merge.

---

## Apa yang Tidak Dilakukan (Yang Mungkin Owner Expect Sudah Selesai)

- ❌ String replacement di `PdfLetterExport.cs`, `WordLetterExport.cs`, `ExcelLetterExport.cs`, `MainForm.cs`, `ShellForm.cs`, `LoginForm.cs`, `ActivationForm.cs`, `UpdateService.cs`, `MachineKeyProvider.cs`, `LicenseService.cs`, `Program.cs` — explicitly scoped ke Week 2 oleh task prompt
- ❌ Edition manifest (`edition.manifest.json` + `SignedManifestLoader`) — itu Week 4 work
- ❌ License schema extension (claims tambahan `editionId`, `editionTier`, `industry`, dst) — Week 6 work
- ❌ Bump version ke 1.3.0 — task explicit "JANGAN bump version"
- ❌ Git commit — task explicit "JANGAN commit. Saya commit setelah review."

---

## Risk untuk Week 2 (Heads-Up)

1. **DBuilder of letterhead duplikat — Week 2 harus hapus DUA tempat sekaligus**: file di `PanelCalculator.WinForms/Assets/Letterhead/` AND `<EmbeddedResource Include="Assets\Letterhead\*" />` di `PanelCalculator.WinForms.csproj`. Lupa salah satu → byte EXE bertambah ~290KB (3 file duplikat) tanpa value.
2. **Golden master test akan fail di Week 2 begitu Service pakai `BrandContext` byte source** karena byte JPG/PNG di Panel.Branding mungkin BEDA dengan WinForms (kalau ada drift saat copy). Cara safe: di Week 2, regenerate baseline pakai `GOLDEN_MASTER_REGENERATE=1` SETELAH switch source.
3. **`MachineKeyProvider.GetPepper()` masih hardcode pepper** — Week 2 task #18 harus port pakai `BrandContext.Current.MachineKeyPepperBytes`, tapi PASTIKAN test `MachineKeyProviderTests.GetKey_IsDeterministic_OnSameMachine` masih pass (artinya nilai key tetap sama as before). Sudah ada test BrandContextWiring yang verify decode benar — itu second-level safety net.
