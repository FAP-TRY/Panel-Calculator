# Security Audit — Panel Calculator v1.2.3

**Tanggal:** 2026-05-16
**Auditor:** panel-security-hardener (AUDIT ONLY mode)
**Scope:** Crack lisensi, pencurian DB, tampering, reverse engineering, update channel hijack

---

## 1. Executive Summary

- **CRITICAL** — Database SQLite di `%AppData%\PanelCalculator\PanelCalculator.db` **tidak ter-enkripsi** dan tidak terikat ke mesin. Kompetitor cukup copy file `.db` (~8.000 produk + harga) lalu buka pakai DB Browser for SQLite. Inilah aset terpenting bisnis dan paling rentan.
- **CRITICAL** — Auto-update **tidak memverifikasi signature/hash** EXE yang di-download (`UpdateService.cs:88-194`). Siapa pun yang berhasil compromise akun GitHub `FAP-TRY` atau melakukan DNS/proxy hijack bisa push EXE jahat ke seluruh customer dengan UAC elevation otomatis.
- **HIGH** — Password user di-hash SHA-256 tanpa salt (`LoginForm.cs:300-305`, `Program.cs:155-160`). Rainbow table attack instan untuk password lemah seperti "admin", "admin123", nama orang.
- **HIGH** — Tidak ada license binding apa pun. EXE bisa di-copy ke PC lain dan jalan tanpa batas. Tidak ada machine ID, tidak ada activation, tidak ada trial.
- **HIGH** — Build pipeline `Installer/build.bat:80-82` **secara eksplisit men-skip Obfuscar** ("Obfuscation dinonaktifkan: tidak kompatibel dengan .NET 8") padahal `obfuscar.xml` sudah tersedia. EXE telanjang siap di-decompile dengan dnSpy/ILSpy.
- **MEDIUM** — Password installer Inno Setup hardcoded di repo: `TTS2025` (`PanelCalculator.iss:27`) dan `Swarna@2025` (`Installer/PanelCalculatorSetup.iss:19`). Repo GitHub yang public → password publik.

---

## 2. Kondisi Saat Ini per Kategori Threat

### 2.1 Crack lisensi
- **Tidak ada license binding sama sekali.** Tidak ditemukan kelas `License`, `Activation`, `MachineId`, atau panggilan ke WMI/registry untuk fingerprinting.
- Tidak ada trial mode, tidak ada limit estimasi, tidak ada expiration.
- Login admin default `admin/admin` di-seed di setiap install (`Program.cs:127-150`).
- EXE bisa di-copy + DB bisa di-copy → aplikasi running 100% di PC mana pun.

### 2.2 Pencurian database harga
- DB plain SQLite, tidak ada encryption (`Program.cs:176` → `options.UseSqlite($"Data Source={dbPath};")`). Tidak ada `Password=` di connection string, tidak pakai SQLCipher.
- Path predictable: `%AppData%\Roaming\PanelCalculator\PanelCalculator.db` (`Program.cs:165-169`).
- DB tidak diproteksi ACL khusus — readable oleh user yang sedang login (memang harus, untuk EF Core).
- Tidak ada audit log untuk export PDF/CSV → kompetitor internal bisa dump semua harga via export tanpa jejak.
- PDF/CSV export tidak ber-watermark (tidak ada user ID embed di metadata).

### 2.3 Tampering
- EXE tidak di-sign Authenticode → SmartScreen tidak punya basis trust, tampering tidak terdeteksi OS.
- Tidak ada self-integrity check (hash EXE saat startup).
- DB tidak punya row-level signature → harga, role admin, `IsActive` user bisa di-flip langsung lewat DB Browser.
- Role check sederhana via field string `User.Role` (`User.cs`, `UserManagementForm.cs:203`) — UPDATE SQL satu baris cukup untuk jadi admin.

### 2.4 Reverse engineering
- `obfuscar.xml` sudah ditulis lengkap dengan settings yang sehat (`RenameMethods=true`, `HideStrings=true`, `SuppressILdasm=true`), tapi **build.bat baris 80-82 secara aktif menonaktifkannya** dan langsung `xcopy app-raw → app-final`.
- Build script `build.bat:84-86` menghapus `.pdb` — bagus, tapi metadata IL masih full readable.
- Single-file publish (di CLAUDE.md disebut `PublishSingleFile=true`) — proteksinya cosmetic; ekstraktor seperti `dotnet-warp` decompiler bisa unpack.
- String literal seperti URL GitHub API, asset name `PanelCalculator.exe`, path DB, query SQL terlihat plain di `UpdateService.cs:18-24`, `Program.cs:103-118`.
- `[Obfuscation(Exclude = true)]` di `Program.Main` (`Program.cs:16`) sudah dipersiapkan — siap pakai begitu obfuscation diaktifkan.

### 2.5 Update channel hijack
- `UpdateService.cs:20` → URL `https://api.github.com/repos/FAP-TRY/Panel-Calculator/releases/latest` (HTTPS, baik).
- Tapi: **tidak ada verifikasi SHA-256, tidak ada signature check, tidak ada code-sign verification** pada file yang di-download (`UpdateService.cs:104-144`).
- Validasi hanya: content-type bukan HTML (line 110-114) dan ukuran > 10 MB (line 137-142). Trivial untuk attacker memenuhi keduanya.
- Update di-jalankan via PowerShell dengan `Verb = "runas"` (`UpdateService.cs:190`) — UAC elevation, file di-tulis ke Program Files. **Konsekuensi: malicious payload berjalan dengan admin rights.**
- Tidak ada certificate pinning untuk `api.github.com`.
- Tidak ada rate limiting / backoff — `try/catch swallow all` (`UpdateService.cs:76-80`) menyembunyikan tanda-tanda MITM.

---

## 3. Threat Assessment Table

| # | Threat | Likelihood | Impact | Current state | Severity |
|---|--------|-----------|--------|---------------|----------|
| T1 | Pencurian DB harga via copy `.db` | High (employee/IT vendor bisa copy file dari `%AppData%`) | Critical (8.000 SKU + margin = aset bisnis utama) | Plain SQLite, no encryption | **CRITICAL** |
| T2 | Update channel hijack (compromised GitHub / DNS) | Low-Medium | Critical (RCE dengan admin) | No hash/signature verification | **CRITICAL** |
| T3 | Password rainbow-table / DB swap | Medium (perlu akses DB) | High (jadi admin) | SHA-256 no salt, no iterations | **HIGH** |
| T4 | EXE bajakan dipakai kompetitor / PT lain | High | High (revenue loss) | Tidak ada licensing apapun | **HIGH** |
| T5 | Decompile dengan dnSpy untuk lihat logic margin / harga | High (skill rendah, tools gratis) | High (logic dipindah ke produk lain) | Obfuscation di-skip oleh build.bat | **HIGH** |
| T6 | Tampering DB (role, IsActive, harga) | Medium (perlu akses file) | High | Plain DB, no row signature | **HIGH** |
| T7 | EXE diganti binary jahat (tampering pasca-install) | Low-Medium | Critical | No Authenticode signing, no integrity self-check | **MEDIUM-HIGH** |
| T8 | Installer password leak | Already leaked (di repo public) | Low (hanya menahan install kasual) | Hardcoded `TTS2025` / `Swarna@2025` di `.iss` | **MEDIUM** |
| T9 | Export PDF/CSV exfiltration tak terdeteksi | High | Medium | Tidak ada audit log, tidak ada watermark | **MEDIUM** |
| T10 | Default credentials `admin/admin` tidak di-force-change | High | High (jika DB ter-expose) | Auto-seed setiap startup (`Program.cs:130-144`) tanpa first-run password reset | **MEDIUM-HIGH** |

---

## 4. Rekomendasi Bertingkat

### Level 1 — Wajib (low effort, high impact)

**L1.1 — Aktifkan Obfuscar di build pipeline**
- File: `Installer/build.bat` baris 80-82.
- Ganti `xcopy /e /i /q "app-raw" "app-final"` → invoke `Obfuscar.Console.exe obfuscar.xml` lalu copy `app-obf` ke `app-final`.
- Tambah `<PackageReference Include="Obfuscar" Version="2.2.*" />` (dotnet tool) atau download standalone.
- Test: buka hasil EXE dengan dnSpy, pastikan method/field ter-rename.

**L1.2 — Verifikasi hash update sebelum apply**
- File: `PanelCalculator.WinForms/Services/UpdateService.cs`.
- Tambah field `ExpectedSha256` di `ReleaseInfo`. Sumber hash:
  - Opsi A (sederhana): parse dari release body markdown (regex `SHA256:\s*([0-9a-f]{64})`).
  - Opsi B (lebih kuat): host file `manifest.json` ditandatangani Ed25519 di GitHub Pages / repo; embed public key di EXE.
- Setelah download (line 144), compute `SHA256.HashData(File.ReadAllBytes(tempExe))`, bandingkan, throw kalau beda.

**L1.3 — Authenticode sign EXE & installer**
- Tanpa sertifikat code-signing → SmartScreen warning + tidak ada basis untuk integrity check.
- EV / OV cert (DigiCert / Sectigo ~$200/tahun) atau self-signed + distribusi cert ke customer.
- Sign: `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 PanelCalculator.exe`.

**L1.4 — Upgrade password hashing ke BCrypt**
- File: `LoginForm.cs:300-305` dan `Program.cs:155-160` (duplikat — konsolidasi dulu).
- Tambah `BCrypt.Net-Next` (NuGet). Hash baru: `BCrypt.HashPassword(pwd, workFactor: 12)`.
- Migration: saat login pertama setelah update, kalau format hash lama (64 hex chars), verify SHA-256, lalu re-hash BCrypt. Tambah kolom `PasswordHashVersion`.
- Force change `admin/admin` saat first login (`Program.cs:130-144` perlu flag `MustChangePassword`).

**L1.5 — Hapus installer password dari repo**
- `PanelCalculator.iss:27` (`TTS2025`) dan `Installer/PanelCalculatorSetup.iss:19` (`Swarna@2025`).
- Pindah ke environment variable di build machine: `#define MyInstallPassword GetEnv("PANEL_INSTALL_PASSWORD")`.
- Rotate password sekarang juga (sudah public di repo selama berbulan-bulan).

---

### Level 2 — License binding

**L2.1 — Machine fingerprint**
- File baru: `PanelCalculator.Core/Services/MachineId.cs`.
- Fingerprint dari kombinasi (di-hash SHA-256):
  - `WMI: Win32_BaseBoard.SerialNumber`
  - `WMI: Win32_Processor.ProcessorId`
  - `Volume serial C:` via `kernel32.GetVolumeInformation`
- Jangan pakai MAC address (mudah berubah dengan VPN/Hyper-V).

**L2.2 — License file ter-signed**
- Generator (di luar app): generate JSON `{customer, expiresUtc, machineIdHash, features}` → sign Ed25519.
- App: embed Ed25519 **public** key (di-encrypt setelah Obfuscar HideStrings).
- Validasi startup di `Program.cs:Main` sebelum `Application.Run`. Tolak kalau invalid / expired / machineId mismatch.
- Simpan license di `%ProgramData%\TritunggalSwarna\license.dat` (read-only untuk user biasa).

**L2.3 — Trial mode (opsional)**
- 14 hari sejak first run. Counter di registry `HKCU\Software\TritunggalSwarna\PanelCalculator\FirstRun` (encrypted via DPAPI agar tidak trivial reset).

**L2.4 — Online activation (opsional, kalau ada budget)**
- Cloudflare Worker gratis: endpoint `POST /activate {licenseKey, machineId}` → return signed license.
- One-time per machine; revoke list di KV.

---

### Level 3 — Anti reverse engineering

**L3.1 — Aktifkan full Obfuscar config** (sudah dibahas di L1.1, ini extension)
- Pertimbangkan `RenameTypes=true` setelah test EF Core (saat ini di `obfuscar.xml:31` masih false). EF Core 8 reflection berbasis property — relatif aman kalau `RenameProperties=false`.
- Setelah obfuscated, smoke test: login, buka katalog, simpan estimasi, export PDF, cek update.

**L3.2 — String encryption untuk secret-like data**
- Setelah `HideStrings=true`, semua string literal di-encrypt otomatis. Tapi data sensitif tetap jangan hardcode (URL update, public key license).

**L3.3 — Anti-debug check ringan**
- Di `Program.Main`: `if (Debugger.IsAttached) Environment.FailFast("");`.
- Win32: `[DllImport("kernel32")] static extern bool IsDebuggerPresent();`.
- Bukan defence-in-depth — hanya naikkan effort attacker pemula.

**L3.4 — Self-integrity check**
- Saat startup, hash EXE sendiri (`File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)`), bandingkan dengan hash yang di-embed (di-update saat build).
- Tantangan: chicken-and-egg → hash harus di-injected post-build. Pakai post-build step yang patch placeholder.

**L3.5 — Strip metadata lebih lanjut**
- Tambah ke `PanelCalculator.WinForms.csproj`:
  ```xml
  <DebugType>none</DebugType>
  <DebugSymbols>false</DebugSymbols>
  <Deterministic>true</Deterministic>
  ```
- Sekarang hanya `.pdb` yang dihapus by build.bat — lebih bersih kalau tidak di-generate sama sekali.

---

### Level 4 — Data protection

**L4.1 — Encrypt database SQLite (paling penting!)**
- Ganti `Microsoft.Data.Sqlite` → tetap, tapi tambah encryption via SQLite EE (SEE, berbayar) atau **SQLCipher** (gratis, paling umum).
- Package: `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher`.
- Connection string: `Data Source=...; Password=<key>`.
- Key derivation: HKDF dari `MachineId` (L2.1) + per-install GUID (di registry, DPAPI-encrypted).
- **Konsekuensi:** kalau user pindah PC tanpa export → DB tidak terbaca. Butuh menu **Export / Import** dengan password.
- **Migration plan:** saat update v1.3.0, app detect DB plain → minta password baru → re-create encrypted DB → backup yang lama.

**L4.2 — Audit log export**
- Tabel baru: `AuditLog (Id, UserId, Action, Target, TimestampUtc, MachineId)`.
- Log setiap: login, export PDF, export CSV, edit produk, view harga (kontroversial — bisa over-log).
- Tampilkan di SettingsForm untuk admin.

**L4.3 — Watermark PDF**
- File: `PanelCalculator.WinForms/Services/PdfLetterExport.cs` dan `PdfQuotationExport.cs`.
- Tambah hidden metadata (iText7 `PdfDocumentInfo.SetMoreInfo`) dengan `{username, machineId, timestampUtc}`.
- Atau watermark visual diagonal abu-abu tipis dengan nama user (deterrent psikologis).

**L4.4 — Backup DB ter-encrypted**
- Auto-backup harian ke `%AppData%\PanelCalculator\Backups\` dengan AES-GCM, key sama dengan DB encryption key.

---

## 5. Quick Wins (Sehari Kerja, Impact Tinggi)

| # | Aksi | Estimasi | File / line |
|---|------|----------|-------------|
| QW1 | Aktifkan Obfuscar di `build.bat` (uncomment + invoke binary) | 2 jam | `Installer/build.bat:80-82` |
| QW2 | Tambah SHA-256 verification pada update (parse hash dari release body) | 2 jam | `UpdateService.cs:144` (insert hash check sebelum return) |
| QW3 | Ganti SHA-256 → BCrypt untuk password baru, keep legacy fallback untuk login lama | 2 jam | `LoginForm.cs:300`, `Program.cs:155` |
| QW4 | Rotate `TTS2025` & `Swarna@2025` + pindah ke env var | 30 menit | `PanelCalculator.iss:27`, `Installer/PanelCalculatorSetup.iss:19` |
| QW5 | Tambah `<DebugType>none</DebugType>` + `<Deterministic>true</Deterministic>` ke 3 csproj | 15 menit | `*.csproj` |

**Total: ~7 jam**. Tidak butuh refactor besar, semua additive, breaking risk minimal kalau hash migration ditest.

---

## 6. Effort Estimate per Rekomendasi

| ID | Rekomendasi | Effort (jam) | Risiko breaking | Catatan |
|----|-------------|--------------|-----------------|---------|
| L1.1 | Aktifkan Obfuscar | 4 | Medium — EF Core / DI reflection bisa break | Test menyeluruh wajib; perlu `[Obfuscation(Exclude=true)]` di kelas EF model |
| L1.2 | Update hash verification | 3 | Low | Backward compat: skip check kalau hash tidak ada di release body |
| L1.3 | Authenticode sign | 4 + biaya cert | Low | Butuh sertifikat $200-400/tahun |
| L1.4 | BCrypt migration | 4 | Low (dengan fallback) | Wajib test login user existing |
| L1.5 | Rotate installer password | 1 | None | Quick win, kerjakan hari ini |
| L2.1 | Machine fingerprint | 4 | None (additive) | WMI bisa slow di first call — cache |
| L2.2 | License file + Ed25519 | 12 | Medium (force-block startup) | Butuh license generator tool terpisah |
| L2.3 | Trial mode | 6 | Low | DPAPI untuk anti-tamper counter |
| L2.4 | Online activation | 16 (incl. Worker) | Medium | Butuh server, butuh offline grace period |
| L3.3 | Anti-debug check | 1 | None | Trivial |
| L3.4 | Self-integrity check | 6 | High — salah implementasi = aplikasi crash | Post-build patch script |
| L3.5 | Strip metadata | 0.5 | None | Just csproj edit |
| L4.1 | **SQLCipher encryption** | 16 + migration | **High** — semua install existing perlu migrate | Paling penting, paling berisiko. Wajib backup otomatis sebelum migrate |
| L4.2 | Audit log table | 6 | Low | DB schema migration via `MigrateDatabase` pattern existing |
| L4.3 | PDF watermark | 3 | None | iText7 metadata API |
| L4.4 | Encrypted backup | 4 | Low | Cron via Timer di ShellForm |

**Total full hardening: ~90 jam (≈ 2-3 minggu) untuk satu engineer.**
**Minimum viable security (L1 + L4.1 + L2.1+L2.2): ~50 jam.**

---

## 7. Prioritas yang Direkomendasikan

1. **Hari ini (1 jam):** QW4 + QW5 — rotate installer password, strip metadata.
2. **Minggu ini (1 hari):** QW1-QW3 — Obfuscar on, hash verify, BCrypt.
3. **2 minggu ini:** L4.1 (SQLCipher) + L2.1+L2.2 (license binding). Test migration di staging.
4. **Setelah customer growth justify biaya:** L1.3 (code-sign cert), L2.4 (online activation), L4.2-L4.4 (audit + watermark + backup).

Jangan kerjakan L3.4 (self-integrity check) sebelum L1.3 (code-sign) — kombinasi keduanya yang berarti, sendirian masing-masing lemah.

---

## 8. Pertanyaan untuk User Sebelum Implementasi

1. Apakah customer current sudah pasti < 10 PT? Kalau ya, license per-machine manual lebih cocok daripada online activation.
2. Berapa frekuensi update DB harga? Kalau monthly, SQLCipher migration ribet (tiap update perlu re-sync DB user) — pikirkan separate "catalog DB" read-only.
3. Apakah user sekarang sudah ganti password admin default? Kalau belum, force-change saat update v1.3 wajib.
4. Budget untuk Authenticode cert? ~$200-400/tahun.
5. Mau toleransi false-positive update verification (kalau user manual edit release body, hash mismatch)? Atau hard-fail?
