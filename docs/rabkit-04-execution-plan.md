# Rabkit 04 Execution Plan

_Generated 2026-05-28 dari workflow `generic-rab-calculator-plan` (4 agents)._

---

# Rencana Eksekusi: Panel Calculator → RAB Kit Multi-Edition Platform

## 1. Executive Summary

**Visi:** Transformasi Panel Calculator v1.2.9 (single-tenant untuk PT TTS) menjadi platform RAB calculator multi-edition dalam 12 minggu, tanpa pernah mengganggu instalasi customer existing. Codebase tunggal melayani dua produk: **Custom Edition** (curated library + branding embedded, dijual ke enterprise per-industri) dan **Generic Edition** (subscription self-service untuk SMB). Yang dijual bukan lagi software, tapi **library knowledge** yang sudah terkurasi untuk industri tertentu — code engine-nya commodity, valuenya di pricelist + formulas yang up-to-date.

**Strategi:** Tiga fase berurutan, masing-masing punya milestone yang bisa di-demo atau dijual. Phase 1 (Week 1-4) zero-risk extraction — refactor internal jadi engine generic + brand pack TTS, output: installer TTS yang fungsional identik dengan v1.2.9 + arsitektur baru siap multi-tenant. Phase 2 (Week 5-8) launch Generic Edition untuk satu vertikal pioneer (panel listrik) dengan landing page, license server, subscription billing; ada 2 customer Custom baru ditargetkan onboarding di phase ini sebagai validasi market. Phase 3 (Week 9-12) bikin marketplace library — vendor/integrator bisa publish `.rabpack` mereka sendiri, contoh vertikal kedua (konstruksi sipil ATAU MEP) sebagai proof multi-industry.

**Timeline kasar:** Demo internal Custom-as-Engine di Week 4, launch Generic public beta di Week 8, marketplace MVP + vertikal kedua di Week 12. Setelah Week 12, masuk mode operate-and-iterate: customer Custom existing tetap dapat update bug fix via channel "tts-stable", customer baru Generic dapat fitur cepat via channel "generic-edge". Total engineering effort estimasi 12 minggu untuk 1 developer (atau 8 minggu untuk 1.5 dev kalau ada bantuan QA/installer specialist). Risiko terbesar: regression di customer PT TTS yang sudah pakai — mitigasi via golden master tests + parallel installation strategy (lihat Risk Register).

---

## 2. Phase-by-phase Plan

### Phase 1 — Zero-Risk Extraction (Week 1-4)

**Tujuan:** Refactor codebase jadi engine `RabKit.*` + brand pack `Panel.Branding`, sambil mempertahankan installer TTS yang behavior-identical dengan v1.2.9. Tidak ada user-facing feature baru — pure architecture work yang bisa di-validate dengan regression testing.

**Week 1 — Foundation: Branding Abstraction**

- Tambahkan project `RabKit.Branding` dengan interface `IBrandConfig` + `IIndustryProfile` (schema sudah ada di dokumen Edition Design).
- Buat `Panel.Branding` project (DLL terpisah) yang implement kedua interface dengan semua nilai PT TTS hardcode existing: company name, signer, Ed25519 public key, pepper, GitHub coordinates, section vocab (Box/Incoming/Outgoing/dst), MCB heuristic, embedded letterhead/signature/stamp PNG.
- Create `EditionContext.Load()` di `Program.Main` yang DI-inject `IBrandConfig` ke seluruh app via static accessor (`BrandContext.Current`). Sementara hardcode pakai `Panel.Branding` di Program.cs.
- **Deliverable:** Solution compile, app boot, behavior 100% identical dengan v1.2.9. Tidak ada perubahan UX.

**Week 2 — Surgical Replacements: Strings & Resources**

- Ganti semua hardcoded `"PT. Tritunggal Swarna"`, `"Kuntjoro Handoko"`, `"Bandung"`, dst di `PdfLetterExport`, `WordLetterExport`, `ExcelLetterExport`, `MainForm`, `ShellForm`, `LoginForm`, `ActivationForm`, `Program.cs` jadi `BrandContext.Current.CompanyName` dst.
- Embedded resource loading di export services: convert dari `GetManifestResourceStream(...letterhead.jpg)` jadi `BrandContext.Current.GetLetterheadBytes()` — `Panel.Branding` punya assets-nya, file storage path-nya internal di DLL itu.
- `UpdateService` ambil `Owner/Repo/AssetName/AppVersion` dari `IBrandConfig`. Tetap hit GitHub Releases yang sama — release strategy belum berubah di Phase 1.
- `MachineKeyProvider` pepper + app tag dari `IBrandConfig`. **KRITIS**: machine key derivation harus output value IDENTIK dengan v1.2.9 untuk customer existing (kalau berubah, SQLCipher DB mereka jadi unreadable). Implementasi: `IBrandConfig.LegacyMachineKeyAppTag` returns `"PanelCalculator.v1"` dan pepper string lama persis.
- **Deliverable:** Build single-file EXE dari refactored codebase, install di mesin test yang sudah punya DB v1.2.9 existing, verify: login berhasil, estimasi lama bisa di-load, export PDF byte-for-byte identical (atau visual-identical dengan pixel diff <1%).

**Week 3 — Sections, Heuristic, Industry Profile**

- Extract panel section list (`Box/Incoming/Outgoing/Trailer/Karoseri/Jasa/Lainnya`) dari `MainForm.cs` lines 37-41, 479-480, dan section color palette lines 1980-2021, jadi data di `Panel.Branding.PanelIndustryProfile.Sections + SectionThemes`.
- Extract `SettingsForm.FamilyToCategory` heuristic ke `IIndustryProfile.CategoryHeuristic`.
- Extract section display map (Box/Incoming/Outgoing/Lainnya) di PdfLetterExport/WordLetterExport jadi `IIndustryProfile.SectionDisplayMap`.
- Extract EstNumber prefix `EST-YYYYMMDD-###` jadi `IIndustryProfile.EstimationNumberPattern`.
- Extract placeholder "Panel MDP 3-Phase 400A" jadi `IIndustryProfile.PlaceholderPerihal`.
- **Deliverable:** `MainForm.cs` tidak punya satupun string panel-specific. Semua test scenario PT TTS masih lulus (lihat golden master test plan di Risk #1).

**Week 4 — Edition Manifest + Polish**

- Implement `SignedManifestLoader` (Ed25519 verify) yang load `edition.manifest.json` dari install dir. Untuk Phase 1, manifest TTS dibundel di installer dengan signature dari developer keypair.
- `EditionContext.Load()` cross-check license `editionId` vs manifest. Untuk customer existing yang punya license tanpa `editionId` claim, fallback ke implicit "custom-tts-panel-v1" (backward compat).
- Title bar tampilkan tier: `"Kalkulator Panel Tritunggal Swarna — v1.3.0 — Custom (PT TTS)"`.
- Golden master regression test: 5 estimasi sample PT TTS (single panel, multi-panel, dengan diskon, dengan PPh, dengan ongkir) — capture PDF/Word/Excel output byte hash di v1.2.9, harus match (atau visual-equivalent) di refactored build.
- Release v1.3.0 ke PT TTS sebagai **dark launch** — kasih installer ke 1-2 user TTS dulu (mis. admin) untuk soak test 1 minggu sebelum push ke semua user via auto-update.

**Phase 1 Milestone — Demo: "Same product, new engine"**

Pemilik bisa demo ke calon customer ke-2 (mis. perusahaan carrosserie) bahwa "engine yang sama yang sudah berjalan di PT TTS bisa di-rebrand untuk industri lain dalam 1 hari". Tampilkan: ganti `Panel.Branding.dll` jadi `Carrosserie.Branding.dll` (placeholder dengan letterhead generik + section Wall/Frame/Roof), rebuild EXE, demo app — UX sama, branding berbeda. Ini sales tool.

---

### Phase 2 — Generic Edition Launch (Week 5-8)

**Tujuan:** Bikin produk Generic Edition yang bisa dijual self-service ke SMB. Customer TTS tidak terganggu (mereka tetap di Custom track dengan installer `KalkulatorPanel-TTS-Setup.exe`). Phase ini menambah produk kedua `RABCalculator-Generic-Setup.exe` dengan license server + landing page.

**Week 5 — Library Pack v1 Schema + Migration**

- Implement `RabKit.LibraryPack` project dengan loader untuk `.rabpack` format (lihat dokumen Library Pack Design). Validate JSON Schema, verify Ed25519 publisher signature, verify SHA-256 per file.
- DB migration: tambah tabel `LibraryPacks`, `Categories`, `Vendors`, `Formulas`, `Templates`, plus kolom baru di `Products` (`PackId`, `CategoryId`, `Unit`, `PriceJson`, `SpecsJson`).
- Migration auto-attach existing TTS products ke pack `tts-panel-listrik@2026.Q1` dengan `TenantId=tts`. Backward compat 100% untuk app TTS — kalau `PackId` NULL (legacy), app pakai behavior lama (flat category string).
- Build `Panel.Branding` `.rabpack` artifact dari existing TTS catalog (~8.021 produk).
- **Deliverable:** Skrip `Tools/build-library-pack.ps1` yang generate `tts-panel-listrik-2026.Q1.rabpack` (signed). Skrip `Tools/migrate-db-to-libraryPacks.ps1` yang upgrade DB v1.2.x ke v1.3.0 schema. Test: run migration di copy DB customer TTS production, verify zero data loss + all estimasi load OK.

**Week 6 — Generic Edition Edition Manifest + Policy**

- Implement `GenericEditionPolicy` dan `CustomEditionPolicy` (lihat dokumen Edition Design). Wire policy gates di SettingsForm (hide branding upload buttons untuk Custom; show untuk Generic), MainForm (export format buttons), repositories (estimation quota check).
- Branding override system: untuk Generic edition, user upload logo/letterhead via SettingsForm → simpan di `%AppData%\RABCalc\branding-override\`. `IBrandingProvider` priority chain: user override → bundled defaults.
- License schema extension: tambah claims `editionId`, `editionTier`, `industry`, `licenseModel`, `seats`, `features` (lihat dokumen Edition Design §3).
- License verifier validate claims + enforce `expiresAt + graceDays`, `hardwareFingerprint`, `editionId vs manifest`. Quota enforcement di repository layer.
- Watermark renderer di `PdfLetterExport` untuk tier yang `features.watermarkOutput=true` (Free/Trial).
- **Deliverable:** Build dua installer dari codebase yang sama: `KalkulatorPanel-TTS-vX.Y.Z-Setup.exe` (manifest Custom + bundled TTS pack) dan `RABCalculator-Generic-vX.Y.Z-Setup.exe` (manifest Generic + empty/demo pack). Boot kedua installer di mesin yang sama, verify mereka coexist (beda AppData folder, beda DB).

**Week 7 — License Server + Activation Flow**

- Standalone web service (Node.js atau Go, deployed di Cloudflare Workers atau VPS murah) dengan endpoint:
  - `POST /activate` — input license key + hardware fingerprint, output signed license JWT.
  - `POST /heartbeat` — weekly online check, validate subscription masih aktif.
  - `POST /renew` — bump expiry setelah payment confirmed via Xendit webhook.
  - `GET /downloads/:tier` — kasih installer link bertoken.
- Xendit checkout integration (Indonesia-native): Free trial sign-up, Basic/Pro/Business subscription via card/transfer, lifetime one-time.
- Email automation (via Resend/SendGrid): welcome email dengan license key + installer link, expiry reminder 7 hari sebelum.
- License key format: 5x5 char Crockford Base32 grouped `XXXXX-XXXXX-XXXXX-XXXXX` (mudah diketik manual).
- **Deliverable:** Landing page sederhana di `rabcalc.id` (atau domain serupa) dengan pricing table + checkout. Test flow end-to-end: bayar Rp 1, terima email, install app, paste license, app aktif.

**Week 8 — Generic Edition Public Beta**

- Pre-built libraries untuk Generic users yang signup: panel listrik (subset 500 SKU dari Schneider/Himel public catalog, anonymized — bukan harga TTS net), MEP starter (200 SKU plumbing), konstruksi rumah tinggal (300 SKU). User pilih industry preset saat first run.
- In-app upgrade prompts: saat user hit quota free trial (20 estimasi), popup "Upgrade to Basic untuk unlimited" dengan deep link ke checkout.
- Analytics minimal: track activation, conversion free→paid, churn (Mixpanel atau Plausible self-hosted).
- Documentation: video tutorial 5-menit "From zero to first quotation" + FAQ tertulis.
- **Soft launch:** post ke 1-2 grup Telegram/WA komunitas kontraktor + LinkedIn personal pemilik. Target: 30 free trial signup minggu pertama, 2-3 paid conversion bulan pertama.

**Phase 2 Milestone — Demo: "Public Generic SaaS"**

Pemilik punya 2 produk yang dijual: (a) Custom Edition existing — sales motion enterprise (target carrosserie/MEP company kedua via outbound), (b) Generic Edition Basic/Pro — self-service web checkout, baseline revenue Rp 1-3 juta/bulan recurring dari 10-20 SMB customer pertama. Yang dijual di Custom: library curation + branding bundling. Yang dijual di Generic: tool + bring-your-own-library.

---

### Phase 3 — Marketplace & Multi-Industry (Week 9-12)

**Tujuan:** Buka platform untuk 3rd-party library publisher (vendor distributor, konsultan industri) supaya bisa publish `.rabpack` mereka sendiri dan dijual via marketplace. Tambah vertikal kedua sebagai proof-of-concept multi-industry — sekaligus jadi reference customer untuk industri itu.

**Week 9 — Marketplace MVP Backend**

- Web app `marketplace.rabcalc.id`: publisher signup, upload `.rabpack`, validation server-side (schema + signature + virus scan), review queue (admin manual approve di MVP).
- Pricing per pack: publisher set harga (Rp 199rb/tahun, Rp 1.5jt one-time, dst), marketplace ambil 30% commission.
- Discovery: browse by industry, search, ratings/reviews (basic).
- License binding: pack purchase ter-attach ke tenant license — kalau license expire, pack akses hilang. Pack bisa di-bundle dengan license existing (Pro tier includes "1 pack of choice").
- **Deliverable:** Marketplace live dengan 1 pack: `tts-panel-listrik` di-list sebagai showcase (mungkin gratis untuk Pro tier, atau Rp 500rb/tahun standalone).

**Week 10 — Second Vertical Pack: Konstruksi Sipil**

- Partner dengan konsultan QS/estimator construction (atau pemilik sendiri yang riset) untuk kurasi library `konstruksi-sipil-rumah-tinggal-2026.Q1.rabpack`: 500-1000 SKU material struktur/dinding/finishing/atap dengan harga Jawa Barat 2026 Q1, formula plat lantai, template rumah type 36/72.
- Branding pack `Sipil.Branding` dengan section vocab Struktur/Dinding/Atap/Finishing dan industry profile yang sesuai.
- Demo deployment di 1 customer pilot konstruksi (kalau pemilik bisa dapat lead) — Custom Edition variant.
- **Deliverable:** Bukti app sama, dengan pack berbeda, melayani industri berbeda. Marketing material: side-by-side screenshot panel listrik vs konstruksi sipil.

**Week 11 — Polish + Library Update Mechanism**

- In-app "Library Updates" tab: cek update pack baru (versi quarterly), download diff (JSON Patch) untuk hemat bandwidth, apply incremental.
- Notifikasi expiry pack — banner orange "Pricelist 2025.Q4 sudah outdated, update ke 2026.Q1" dengan link checkout di marketplace.
- Multi-pack coexistence: customer yang punya 2 industri (mis. kontraktor yang kerjakan panel + sipil) bisa install kedua pack di Pro tier, pilih saat create estimasi baru.
- Improve onboarding: industry-aware wizard saat first run — "Apa industri Anda? [Panel Listrik / Konstruksi Sipil / MEP / Custom]" → load pack starter yang relevan.
- **Deliverable:** v1.4.0 dengan library update flow lengkap, marketplace pack purchase end-to-end working.

**Week 12 — Stabilization + Operations Setup**

- Operational dashboards: monitor activation rate, churn, support tickets per tier, pack download stats, library expiry alerts (untuk customer Custom yang butuh refresh).
- SLA definition: Custom edition support response 1 hari kerja, Generic Pro 3 hari kerja, Generic Basic email-only 1 minggu.
- Release channel separation: `tts-stable` (slow, manual approval untuk customer TTS), `generic-edge` (fast, weekly drop untuk Generic users yang opt-in beta).
- Knowledge base + admin documentation untuk future-developer onboarding.
- Customer success: kontak 5 Generic Basic/Pro user pertama untuk interview NPS + feedback — feed ke roadmap quarter berikutnya.

**Phase 3 Milestone — Demo: "Marketplace Platform"**

Pemilik bisa pitch ke investor atau partner: "Kami punya 50 paid users dari 2 industri, marketplace dengan X pack listed, dan setiap publisher kontribusi pack baru menambah value tanpa engineering effort dari sisi kami. Engine code commodity, value-nya di curated library yang user-generated."

---

## 3. Risk Register

| # | Risk | Likelihood | Impact | Mitigation |
|---|------|-----------|--------|------------|
| 1 | **Refactoring break customer TTS existing** — string/asset extraction salah, machine key derivation berubah, SQLCipher DB jadi unreadable | Tinggi | Kritis (revenue loss + reputasi) | Golden master regression: hash output PDF/Word/Excel dari 5 estimasi sample sebelum & sesudah refactor, harus match. Dark launch ke 1-2 user TTS dulu 1 minggu sebelum mass rollout. Backup DB customer otomatis sebelum migration. Kebijakan: `IBrandConfig.LegacyMachineKeyAppTag` dan pepper string WAJIB identik dengan v1.2.9 untuk Custom edition. |
| 2 | **DB migration v1.2 → v1.3 (LibraryPacks schema) corrupt customer data** | Sedang | Kritis | Migration script idempotent + transactional. Dry-run mode yang report rencana perubahan tanpa write. Auto-backup DB ke `%AppData%\PanelCalculator\backups\pre-v1.3-{timestamp}.db` sebelum migrate. Rollback script tested. Migration di-trigger manual via dialog "Update database schema?" — tidak auto pada launch pertama v1.3. |
| 3 | **License server downtime → customer locked out** | Sedang | Tinggi | Grace period 14 hari Custom / 7 hari Generic. Offline activation fallback: customer bisa kirim hardware fingerprint via email, dapat balasan signed license file untuk paste manual. Multi-region deployment (Cloudflare Workers auto-failover). Status page publik. |
| 4 | **Customer TTS tidak mau upgrade ke v1.3** (resistant to change) | Sedang | Sedang | Phase 1 invisible: v1.3.0 untuk TTS UX-identical dengan v1.2.9. Komunikasi: "Update v1.3 — backend improvement, no UI change". Auto-update tetap optional (manual click). Kalau ada yang refuse, support tetap v1.2.x lewat hotfix branch sampai Q4 2026. |
| 5 | **License key piracy / pirate generator** | Tinggi (Generic), Rendah (Custom) | Sedang (Generic), Tinggi (Custom) | Custom: hardware fingerprint binding ketat + obfuscation existing (Obfuscar). Generic: anti-piracy ringan (pirated user adalah lost cause untuk Rp 149rb/bulan tier; effort piracy > effort bayar). Subscription tier dengan online weekly heartbeat — pirate harus crack tiap minggu. Watermark output di Free tier supaya tidak praktis untuk komersial. |
| 6 | **Generic Edition tidak ada demand** (market kosong / tidak ada PMF) | Sedang | Tinggi (waste 4 weeks Phase 2) | De-risk Week 5: sebelum bangun license server, posting landing page "coming soon" + email capture, target 100 signup minat dalam 2 minggu. Kalau <30 signup, pivot: jangan launch self-service, fokus Custom enterprise sales. Phase 2 work tetap reusable untuk customer Custom kedua/ketiga karena policy/manifest abstraction valuable terlepas dari ada Generic SaaS atau tidak. |
| 7 | **Library curation effort untuk vertikal baru terlalu mahal** | Tinggi | Sedang | Phase 3 vertikal kedua: minta partner konsultan QS/estimator yang sudah punya pricelist, mereka jadi publisher di marketplace dengan revenue share — bukan kerja sendiri. MVP-nya: 200-500 SKU saja, bukan 8.000 seperti TTS. User Generic bisa import sendiri CSV mereka kalau pack di marketplace belum lengkap. |
| 8 | **Marketplace 3rd-party pack ada konten buruk / pricing salah** | Sedang | Sedang | Manual review queue di MVP — admin (pemilik) approve setiap pack sebelum listed. Rating + review system. Publisher signature + audit trail. Disclaimer di app: "Library data accuracy is publisher's responsibility". |
| 9 | **Ed25519 publisher private key bocor** | Rendah | Kritis (rogue pack signed bisa di-load) | HSM atau secure key vault (1Password/AWS KMS) untuk signing key. Key rotation procedure documented. App support multi-key (current + previous N) untuk rotation tanpa breaking pack lama. |
| 10 | **Single developer bus factor** | Tinggi | Kritis | Documentation prioritas tinggi di Phase 3 Week 12. CLAUDE.md tetap up-to-date setiap phase. Critical: license server source code + signing keys + deployment runbook tersimpan di location yang owner punya akses tanpa developer. Consider hire kontraktor untuk maintenance kalau pemilik tidak technical. |

---

## 4. First 2 Weeks Task List

### Week 1 — Branding Abstraction Foundation

**Day 1 (Senin) — Project Scaffolding**
1. Buat branch `feature/edition-refactor` dari `master`.
2. Tambah project baru `RabKit.Branding` di solution: define `IBrandConfig` interface (semua property sesuai dokumen Edition Design §1 Layer B), `IIndustryProfile` interface (Sections, SectionThemes, CategoryHeuristic, SectionDisplayMap, EstimationNumberPattern, PlaceholderPerihal).
3. Tambah project baru `Panel.Branding` yang reference `RabKit.Branding`: class `TTSBrandConfig : IBrandConfig` dengan semua nilai PT TTS existing (literal copy dari hardcode lokasi yang tercatat di audit), class `PanelIndustryProfile : IIndustryProfile`.
4. Verifikasi build solution sukses, tidak ada test break.

**Day 2 — Static Accessor Setup**
5. Tambah `RabKit.Branding.BrandContext` static class dengan `BrandContext.Current` property (set di Program.Main).
6. Di `Program.Main` paling awal (sebelum `ApplicationConfiguration.Initialize`): `BrandContext.Current = new TTSBrandConfig()` dan `IndustryContext.Current = new PanelIndustryProfile()`. Hardcode dulu — manifest loading datang di Week 4.
7. Verify boot app, semua flow PT TTS tetap jalan (login, lihat katalog, save estimasi, export PDF).

**Day 3 — String Replacement Wave 1: Forms**
8. Replace di `MainForm.cs` line 104 (title), 153 (banner), 601 (placeholder), 1429 (EstNumber prefix) — pakai `BrandContext.Current.AppName`, `BrandContext.Current.CompanyDisplayName`, `IndustryContext.Current.PlaceholderPerihal`, `IndustryContext.Current.EstimationNumberPattern`.
9. Replace di `LoginForm.cs` line 30, `ShellForm.cs` line 52.
10. Replace di `ActivationForm.cs` lines 14, 25, 82, 117, 135, 217, 259, 280 (PT TTS copy + WhatsApp number).
11. Smoke test: boot app, semua text tampil identik dengan v1.2.9.

**Day 4 — String Replacement Wave 2: Export Services**
12. `PdfLetterExport.cs`: replace lines 100-102 (default signer), 186-188 (signer fallback), 360, 368 (Bandung-specific phrase), 401, 431 (company name), 591-615 (section display map → `IndustryContext.Current.SectionDisplayMap`).
13. `WordLetterExport.cs`: replace lines 86, 88, 92, 150-162 (defaults + signer), 181, 246 (letterhead resource), 406-460 (signer + section map).
14. `ExcelLetterExport.cs`: replace lines 77, 82, 345, 349, 372 (company + city + copy).
15. Embedded resource loading di 3 export services: ganti `GetManifestResourceStream("PanelCalculator.WinForms.Assets.Letterhead.letterhead.jpg")` jadi `BrandContext.Current.GetLetterheadBytes()`. `Panel.Branding` project pegang assets — declare `EmbeddedResource` di `.csproj` Panel.Branding, expose via method.
16. Verify regression: export 1 estimasi sample, bandingkan PDF byte-by-byte (atau visual diff <1%) dengan v1.2.9 output.

**Day 5 — String Replacement Wave 3: Services & Program**
17. `UpdateService.cs`: ganti `Owner`, `Repo`, `AssetName`, `ManifestAssetName`, `AppVersion`, AppData dir literal jadi `BrandContext.Current.GitHubOwner` dst.
18. `MachineKeyProvider.cs`: ganti pepper string lines 174-183 dan constant line 56 jadi `BrandContext.Current.LegacyMachineKeyAppTag` dan `BrandContext.Current.LegacyMachineKeyPepper`. **KRITIS**: nilai yang di-return dari `Panel.Branding.TTSBrandConfig` HARUS persis sama dengan v1.2.9 — verify dengan unit test yang assert output `MachineKeyProvider.GetMachineKey()` di mesin developer match expected value yang di-generate dari v1.2.9.
19. `LicenseService.cs` line 34: `PublicKeyBase64` const jadi static property yang baca `BrandContext.Current.LicenseEd25519PublicKey`.
20. `Program.cs` seeds lines 246-249: defaults dari `BrandContext.Current`.
21. Full regression test: install v1.3.0 build over v1.2.9 di mesin test yang punya DB existing. Verify login berhasil (machine key match → SQLCipher decrypt OK), estimasi lama load OK, export PDF identik.

### Week 2 — Sections, Heuristic, Polish

**Day 6 (Senin) — Section System Extraction**
22. Audit di `MainForm.cs` lines 37-41 (section array literal), 479-480 (lain-lain section list), 1980-2021 (section color palette switch) — extract ke `Panel.Branding.PanelIndustryProfile.Sections` (list of SectionDefinition: name, displayLabel, theme color bg/fg).
23. Refactor `MainForm` switch statement jadi lookup `IndustryContext.Current.Sections.FirstOrDefault(s => s.Name == sectionName)?.Theme`.
24. Verify UI rendering: combo box section menampilkan sama, warna row per section sama.

**Day 7 — Category Heuristic + Section Display Map Cleanup**
25. `SettingsForm.cs` lines 925-944 (FamilyToCategory): extract ke `Panel.Branding.PanelIndustryProfile.MapFamilyToCategory(string family)`. Wire via `IndustryContext.Current.MapFamilyToCategory(...)`.
26. `PdfQuotationExport.cs` lines 27-46, 146-147, 313-318 (section color palette panel-domain): refactor pakai `IndustryContext.Current.GetSectionTheme(name)`.
27. Verify CSV import dengan vendor pricelist (Schneider sample) — kategori auto-detect masih sama hasilnya.

**Day 8 — Golden Master Test Suite**
28. Buat `PanelCalculator.Tests.GoldenMaster` project. Tambahkan fixture: 5 estimasi sample (tersimpan sebagai JSON serialized dari DB v1.2.9 production data PT TTS — anonymized):
    - Single panel sederhana (10 item, no diskon, no tax)
    - Single panel dengan PPN 11% + PPh 2.5%
    - Multi-panel 3 unit dengan ongkir
    - Estimasi dengan section custom "Trailer + Karoseri"
    - Estimasi dengan margin 3-tier
29. Untuk setiap fixture, generate PDF/Word/Excel di v1.2.9, simpan SHA-256 hash + visual screenshot referensi.
30. Test code: load fixture, generate output di v1.3.0 build, compare hash (binary identical) atau pixel diff <1% untuk yang ada timestamp.
31. Wire ke CI lokal: `dotnet test PanelCalculator.Tests.GoldenMaster` harus lulus sebelum tag release.

**Day 9 — Edition Manifest Skeleton**
32. Tambah `RabKit.EditionManifest` project dengan class `EditionManifest`, `SignedManifestLoader` (Ed25519 verify pakai existing crypto infrastructure).
33. Buat developer keypair untuk signing manifest: simpan private key di 1Password atau secure vault pemilik. Public key embed di `BuildConstants.ManifestPublicKey`.
34. Sign manifest `edition-tts.manifest.json` dengan claims sesuai dokumen Edition Design §1 Layer B (editionId=custom-tts-panel-v1, editionTier=Custom, dst).
35. `Program.Main`: load manifest dari install dir, instantiate `EditionContext`, gunakan untuk pilih `IBrandConfig` (sementara: kalau `editionId.StartsWith("custom-tts")` → `TTSBrandConfig`, else → throw). Cross-check belum perlu di Week 2 — license schema masih lama.

**Day 10 — Dark Launch Prep**
36. Bump version: `UpdateService.AppVersion = "1.3.0-alpha"`, `PanelCalculator.iss` AppVersion `1.3.0-alpha`, `CLAUDE.md` changelog entry.
37. Build single-file EXE via `Tools/build-release-singlefile.ps1`, run regression test suite, verify pass.
38. Build installer via Inno Setup, install di clean VM Windows 11, test full flow: first-time install, login admin, create estimasi, save, export PDF, restart app, load estimasi, run update check (harus tampil "Sudah versi terbaru").
39. Install di mesin test yang punya DB v1.2.9 existing (copy production DB dari pemilik untuk testing): verify upgrade in-place, semua data utuh, semua estimasi historikal load OK.
40. Kalau semua hijau: kirim installer v1.3.0-alpha ke 1 user TTS (admin atau pemilik sendiri) untuk soak test 5 hari sebelum mass rollout. Sementara itu, mulai planning Phase 2 dengan menulis API spec license server (Week 3 work).

**Definition of Done (akhir Week 2):**
- Tidak ada string literal `"Tritunggal"`, `"Kuntjoro"`, `"Bandung"`, `"Panel"`, `"TTS"` di `RabKit.*` projects (boleh ada di `Panel.Branding.*`).
- `grep -r "PT. Tritunggal" PanelCalculator.WinForms/ PanelCalculator.Core/ PanelCalculator.Data/` returns 0 results (semua sudah di `Panel.Branding`).
- Golden master test suite hijau.
- Build v1.3.0-alpha tersedia, terinstall di 1 mesin TTS untuk soak test.
- Documentation update: CLAUDE.md mention dual-project structure baru.

---

## 5. Open Questions Buat Decision Maker

Pemilik perlu jawab 8 pertanyaan berikut sebelum atau selama Week 1 supaya Phase 2-3 tidak bottleneck.

**Q1 — Posisi PT TTS dalam strategi: customer atau partner?**

Apakah PT TTS akan tetap jadi customer biasa (bayar maintenance per tahun untuk Custom edition), atau jadi co-publisher di marketplace (mereka dapat revenue share kalau ada perusahaan lain beli `tts-panel-listrik` pack)? Decision ini affect license agreement dengan PT TTS dan apakah kita perlu kontrak baru.

**Q2 — Brand name produk Generic?**

Phase 2 launch butuh nama produk untuk landing page, installer, domain. Opsi: "RAB Kit", "RABCalc", "Estimasi Pro", "Kalkulator Penawaran", nama lain? Affect: domain purchase, logo design, copy marketing. Decision di-perlukan paling lambat akhir Week 3.

**Q3 — Pricing tier Generic — apakah masuk akal untuk market Indonesia?**

Dokumen Edition Design propose: Free trial 30 hari, Basic Rp 149rb/bulan, Pro Rp 399rb/bulan, Business Rp 899rb/bulan, Lifetime Pro Rp 9.9jt. Apakah pemilik comfortable dengan price point ini? Punya benchmark dari kompetitor (Excel template Rp 50-500rb sekali bayar di Tokopedia, software RAB lain)? Affect: business case, go/no-go Phase 2.

**Q4 — Vertikal kedua di Phase 3: konstruksi sipil atau MEP?**

Pemilik punya akses lead atau koneksi di industri mana? Library curation effort lebih ringan kalau ada partner internal (mis. teman QS engineer untuk sipil, atau teman MEP contractor). Tanpa partner, pemilik harus riset sendiri 500+ harga material — effort ~2 minggu full-time. Decision di-perlukan paling lambat Week 8.

**Q5 — License server hosting & operations: pemilik manage sendiri atau outsource?**

License server adalah kritis (downtime = customer locked out). Opsi: (a) deploy ke Cloudflare Workers (~Rp 50rb/bulan, hampir zero-ops), (b) VPS Indonesia (Niagahoster/IDCloudHost, ~Rp 100rb/bulan, ada uptime risk), (c) outsource ke developer/agency dengan SLA. Affect: monthly cost + bus factor mitigation.

**Q6 — Payment gateway: Xendit, Midtrans, atau Stripe?**

Untuk market Indonesia, Xendit dan Midtrans paling umum (support virtual account, e-wallet, kartu kredit). Stripe untuk customer luar negeri kalau ada. Affect: integration effort + transaction fee 2.5-3.5%. Bisa dual-gateway nanti kalau perlu. Decision di Week 6.

**Q7 — Apakah Free tier benar-benar gratis selamanya atau time-limited?**

Trade-off: free-forever = lebih banyak signup tapi conversion lebih sulit + biaya server lebih besar; trial 30 hari = pressure ke conversion. Saran: trial 30 hari → expire jadi read-only (lihat history tapi tidak bisa save baru) + bisa di-extend free Selamanya tapi watermark + max 5 estimasi/bulan. Apakah pemilik setuju strategi ini?

**Q8 — Investasi tooling: marketplace web (Week 9-10) build sendiri atau pakai Gumroad/Lemon Squeezy?**

Build sendiri marketplace memakan 2 minggu engineering + ongoing maintenance. Alternatif: pakai Gumroad atau Lemon Squeezy yang handle checkout/license delivery/marketplace listing — hanya butuh integration custom untuk pack signing + binding ke license. Build sendiri lebih flexible (untuk pack rating, multi-version, dst) tapi lebih mahal. Decision di Week 8 sebelum start Phase 3.

---

**Catatan implementasi cross-cutting yang sudah baked in:**

- Backward compat 100% untuk customer TTS adalah constraint, bukan goal — kalau ada konflik antara "elegance" dan "TTS tidak break", selalu pilih TTS tidak break.
- Single codebase, multi-edition via manifest. Tidak ada git branch terpisah per edition.
- Setiap phase punya release artifact yang bisa di-rollback (installer disimpan, DB backup otomatis, Obfuscar tidak block decompile dari developer machine untuk debugging).
- Selalu bump patch version (kebijakan existing v1.2.6+) setiap distribusi. v1.3.0 untuk dark launch refactor, v1.3.x untuk patches Phase 2-3.