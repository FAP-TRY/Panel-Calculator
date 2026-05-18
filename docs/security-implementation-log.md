# Security Implementation Log — Panel Calculator

Log perubahan keamanan, dibuat seiring implementasi rekomendasi audit
(`docs/security-audit-2026-05-16.md`).

---

## Item #1 — DB Encryption (SQLCipher, machine-bound key)

**Tanggal:** 2026-05-16
**Status:** Implementasi selesai — menunggu `dotnet build` + smoke test.
**Branch:** `claude/sharp-jemison-2e465a`

### Tujuan
Setiap database SQLite di komputer customer di-enkripsi dengan kunci yang
diturunkan dari identitas hardware mesin tersebut. Konsekuensi: file `.db`
yang di-copy ke komputer lain TIDAK BISA dibuka — baik oleh DB Browser
maupun oleh aplikasi yang sama yang di-install di mesin berbeda.

### File yang diubah / ditambah

| File | Jenis | Ringkasan |
|------|-------|-----------|
| `PanelCalculator.Data/PanelCalculator.Data.csproj` | EDIT | Ganti `Microsoft.EntityFrameworkCore.Sqlite` → `Microsoft.EntityFrameworkCore.Sqlite.Core` + `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlcipher` v2.1.8 + `System.Management` v8.0.0. Pakai `.Core` variants supaya bisa pilih provider SQLCipher; default bundle akan tarik plain SQLite. |
| `PanelCalculator.Data/Security/MachineKeyProvider.cs` | NEW | Derive 64-char hex key dari Win32_BaseBoard.SerialNumber + Win32_Processor.ProcessorId + HKLM `MachineGuid` + pepper (XOR-obfuscated). SHA-256. Hasil di-cache. |
| `PanelCalculator.Data/Security/DbMigrator.cs` | NEW | `MigrateIfNeeded(dbPath, key, logDir)`: deteksi plain DB → backup → ATTACH encrypted + `sqlcipher_export` → swap file → verify `PRAGMA integrity_check`. Auto-prune backup > 7 hari. Log ke `migration-yyyy-MM-dd.log`. |
| `PanelCalculator.WinForms/Program.cs` | EDIT | (1) Panggil `SQLitePCL.Batteries_V2.Init()` di awal `Main`. (2) Jalankan `DbMigrator.MigrateIfNeeded(...)` SEBELUM DI container dibangun. Kalau gagal: tampilkan MessageBox dan exit graceful. (3) Connection string EF Core sekarang sertakan `Password=<machineKey>` via `SqliteConnectionStringBuilder`. (4) Ekstrak `GetDbPath()` helper supaya migrator + DI pakai path yang sama. |
| `PanelCalculator.Tests/Security/MachineKeyProviderTests.cs` | NEW | 3 test: determinisme, format hex 64-char, caching. |
| `PanelCalculator.Tests/Security/DbMigratorTests.cs` | NEW | 3 test: missing DB → NoDatabase; plain DB → encrypted + data utuh + tidak bisa dibuka tanpa key + backup ter-create; sudah encrypted → noop. |

### Cara migrasi bekerja (alur)

```
App start
   │
   ├─ SQLitePCL.Batteries_V2.Init()             ← SQLCipher provider loaded
   │
   ├─ DbMigrator.MigrateIfNeeded(...)
   │     │
   │     ├─ File tidak ada? → NoDatabase (DB akan di-create encrypted oleh EnsureCreated)
   │     │
   │     ├─ Buka tanpa key + SELECT sqlite_master
   │     │     ├─ Sukses → DB masih plain → lanjut backup + encrypt
   │     │     └─ Gagal → coba lagi dengan key
   │     │           ├─ Sukses → AlreadyEncrypted (noop)
   │     │           └─ Gagal → throw "DB tidak bisa dibuka" → MessageBox → exit
   │     │
   │     ├─ File.Copy → PanelCalculator.db.bak-YYYYMMDD-HHmmss
   │     ├─ ATTACH DATABASE temp AS encrypted KEY '<hex>'
   │     ├─ SELECT sqlcipher_export('encrypted')
   │     ├─ DETACH
   │     ├─ File.Delete(plain) + File.Move(temp → plain)
   │     ├─ Verify PRAGMA integrity_check
   │     └─ Prune backup > 7 hari
   │
   ├─ DI container dibangun
   ├─ EnsureCreated() (noop untuk DB existing)
   ├─ MigrateDatabase()  (column additions, sudah ada sebelumnya)
   ├─ SeedDefaultAdmin()
   └─ Login → Shell
```

### Cara test manual

Prasyarat: punya akses ke mesin dev (Windows + dotnet 8 SDK).

#### 1. Build & unit tests

```powershell
# Dari root repo:
dotnet restore
dotnet build PanelCalculator.sln --configuration Release
dotnet test PanelCalculator.Tests/PanelCalculator.Tests.csproj
```

Yang diharapkan:
- `dotnet build` sukses tanpa warning baru.
- 6 test passed (3 MachineKeyProvider + 3 DbMigrator).

#### 2. Smoke test migrasi terhadap DB lama

```powershell
# Backup DB produksi yang sekarang
Copy-Item "$env:APPDATA\PanelCalculator\PanelCalculator.db" `
          "$env:APPDATA\PanelCalculator\PanelCalculator.db.PRE-ENCRYPT-BACKUP"

# Run app yang baru (Debug atau Release)
dotnet run --project PanelCalculator.WinForms --configuration Release
```

Check setelah aplikasi exit / login:
- File `PanelCalculator.db.bak-<timestamp>` muncul di `%AppData%\PanelCalculator\`.
- File `logs\migration-<tanggal>.log` muncul, isinya alur migrasi sukses.
- Login & buka katalog produk — semua harga masih tampil.
- Buka `PanelCalculator.db` baru dengan **DB Browser for SQLite** → harus
  gagal ("file is not a database") = sukses, sudah ter-enkripsi.
- Buka file `.bak-<timestamp>` dengan DB Browser → harus berhasil dibaca
  (backup masih plain — sengaja, supaya bisa rollback manual kalau ada
  bencana). Backup ini akan auto-dihapus 7 hari ke depan.

#### 3. Test "DB di-copy ke PC lain"

```powershell
# Copy DB ter-enkripsi ke PC lain (atau VM lain)
# Coba jalankan aplikasi di sana.
```

Yang diharapkan:
- Aplikasi DETEKSI: DB tidak bisa dibuka tanpa key, juga tidak bisa
  dibuka dengan key mesin baru (karena fingerprint hardware beda).
- MessageBox: "Database tidak bisa dibuka sebagai plain text maupun
  dengan kunci mesin saat ini. Kemungkinan: database dibawa dari komputer
  lain, atau identitas hardware berubah. Hubungi support."
- Aplikasi exit, tidak menulis apa-apa.

### Risiko & yang harus dipantau setelah deploy

1. **Hardware change scenario.** Kalau motherboard / CPU diganti (servis,
   upgrade), `MachineKeyProvider.GetKey()` akan menghasilkan key baru →
   DB tidak bisa dibuka → MessageBox error. Mitigasi:
   - Sosialisasi ke customer: SEBELUM ganti hardware → lakukan ekspor
     CSV / backup manual.
   - Roadmap: tambah menu "Export DB ter-encrypted dengan password
     master" untuk recovery scenario.

2. **Test virtual machine.** VM seperti Hyper-V / VMware bisa expose
   serial number generic ("To Be Filled By O.E.M.") — sudah di-filter
   di `SafeWmi`. Tapi 2 VM identik mungkin punya MachineGuid sama jika
   di-clone tanpa sysprep. Belum dimitigasi karena bukan use case
   produksi.

3. **First-run migration time.** Untuk DB 8.000 produk (~10-20 MB),
   `sqlcipher_export` typically jalan < 2 detik. Tapi di mesin lama
   dengan HDD bisa lebih lama. Tidak ada progress UI sekarang — kalau
   user complain "aplikasi hang saat first run", tambah splash screen
   dengan "Migrasi database, mohon tunggu...".

4. **Package upgrade impact.** Ganti dari `Microsoft.EntityFrameworkCore.Sqlite`
   (high-level) ke `.Core` variant + manual provider bundle. Restore harus
   bersih. Kalau ada konflik dependency lain, kemungkinan dari `ClosedXML`
   atau `itext7` — sejauh ini tidak terlihat masalah.

5. **Backup file location.** Backup `.bak-*` ada di folder yang sama
   dengan DB (`%AppData%\PanelCalculator`). Selama 7 hari ada file plain
   yang bisa di-copy → window of opportunity untuk pencurian. Mitigasi
   nanti: enkripsi backup dengan DPAPI (`ProtectedData.Protect`).

### Yang BELUM dikerjakan (out of scope untuk item #1 ini)

- **Per-install GUID + DPAPI** untuk key derivation (rekomendasi audit
  L4.1 menyebut HKDF dari machineId + per-install GUID DPAPI-encrypted).
  Saat ini hanya pakai machineId murni. Implikasi: kalau attacker bisa
  enumerasi WMI di mesin user (mis. via malware), dia bisa derive key
  yang sama. Per-install GUID akan menambah lapisan yang harus dicuri
  juga dari registry/DPAPI store user.
- **Menu Export / Import DB ter-encrypted dengan password master** untuk
  recovery scenario (audit L4.1 catatan "Konsekuensi: kalau user pindah
  PC tanpa export → DB tidak terbaca").
- **Encrypted backup** (audit L4.4) — backup `.bak-*` saat ini masih
  plain SQLite. By design untuk recovery, tapi ideally enkripsi dengan
  DPAPI/user scope.
- **Proper string encryption untuk pepper.** Sekarang hanya XOR 1-byte
  mask. Akan otomatis ter-handle saat L3.2 (Obfuscar HideStrings)
  diaktifkan.
- **Code-sign cert verifikasi key integrity.** Bukan blocker tapi
  rekomendasi audit menyebut ini untuk lapisan extra di self-integrity
  check (L3.4).

### Pertanyaan untuk reviewer

1. Apakah pepper "TTS-PanelCalc-pepper-2026-v1" boleh di-hardcode begini?
   Atau mau di-rotate ke value lain sebelum release? (Sekarang sudah
   di-XOR tapi pepper masih recoverable kalau decompile)
2. Saat migrasi sukses, perlu kah tampilkan MessageBox info ke user
   ("Database berhasil di-enkripsi") atau silent saja (sekarang silent)?
3. Backup `.bak-*` retention 7 hari — terlalu pendek? Terlalu panjang?

---

## Item #2 — Verifikasi Hash Update (SHA-256 manifest)

**Tanggal:** 2026-05-16
**Status:** Implementasi selesai — build sukses, 36/36 test pass (30 baru + 6 existing).
**Branch:** `claude/sharp-jemison-2e465a`
**Referensi audit:** §2.5 "Update channel hijack" → severity CRITICAL.

### Tujuan
Sebelum perubahan ini, auto-updater HANYA mengecek bahwa file yang
di-download bukan halaman HTML dan ukurannya > 10 MB. Siapa pun yang
berhasil meng-compromise akun GitHub `FAP-TRY` (atau MITM tertentu) bisa
mendorong EXE jahat ke semua customer, yang lalu dijalankan dengan UAC
elevation — RCE level admin.

Sekarang, sebelum updater PowerShell di-spawn, client:
1. Menolak URL yang bukan dari `github.com` / `objects.githubusercontent.com`.
2. Wajib mendownload manifest `PanelCalculator.exe.sha256` dari release
   yang sama.
3. Recompute SHA-256 dari EXE yang baru di-download.
4. Bandingkan dengan isi manifest. Jika tidak cocok → hapus file +
   abort + tampilkan pesan jelas ke user.
5. Jika manifest tidak ada (404 / tidak di-upload oleh maintainer) →
   fail-secure: tolak update.

### File yang diubah / ditambah

| File | Jenis | Ringkasan |
|------|-------|-----------|
| `PanelCalculator.Data/Security/UpdateVerifier.cs` | NEW | Helper murni (no WinForms dep) — `ComputeSha256`, `ParseManifest`, `HashesMatch`, `IsAllowedDownloadHost`, `VerifyOrThrow`. Custom `UpdateVerificationException`. Diletakkan di `PanelCalculator.Data` supaya bisa di-unit-test tanpa harus reference WinForms dari test project. |
| `PanelCalculator.WinForms/Services/UpdateService.cs` | EDIT | (1) `ReleaseInfo` record dapat field baru `ManifestUrl`. (2) `CheckAsync` mengidentifikasi asset `PanelCalculator.exe.sha256` di samping `PanelCalculator.exe`. (3) `DownloadAndApplyAsync` sekarang: validasi host download → download EXE → validasi host setelah redirect → download manifest dengan validasi yang sama → call `UpdateVerifier.VerifyOrThrow` → baru spawn updater PowerShell. (4) Semua step di-log ke `%AppData%\PanelCalculator\logs\update-YYYY-MM-DD.log`. (5) Helper `MakeManifestClient` (text/plain, 30 detik timeout). (6) Helper `TryDelete` untuk cleanup file gagal verify. |
| `Tools/generate_release_manifest.ps1` | NEW | Script PowerShell — input `publish/PanelCalculator.exe`, output `publish/PanelCalculator.exe.sha256` format sha256sum (2 spasi). Print hash ke console. |
| `CLAUDE.md` | EDIT | Section "Cara Rilis Versi Baru" — tambah step #5 (jalankan script generator) dan tambah file `.sha256` ke daftar asset di `gh release create`. Warning box "PENTING" tentang kewajiban manifest sejak v1.2.4. |
| `PanelCalculator.Tests/Security/UpdateVerifierTests.cs` | NEW | 30 test (10 fact + 20 inline data dari 4 theory): hash known vectors, file vs bytes consistency, manifest parsing (5 valid format + 5 invalid), case-insensitive match, host allowlist (subdomain rules, http vs https, typosquat), `VerifyOrThrow` round-trip (match/non-match/invalid manifest). |

### Format manifest yang didukung

Parser sengaja toleran — bisa parse line apa saja yang mengandung token
64 karakter heksadesimal. Contoh yang valid semua:

```
ba78…15ad
ba78…15ad  PanelCalculator.exe          # sha256sum, 2 spasi (default script)
ba78…15ad *PanelCalculator.exe          # sha256sum -b binary mode
SHA256: ba78…15ad
# Komentar di baris atas
ba78…15ad
```

### Host allow-list

Hanya dua root domain yang diterima:
- `github.com` (dan subdomain seperti `api.github.com`)
- `objects.githubusercontent.com` (exact)

Subdomain RANDOM di bawah `githubusercontent.com` ditolak (mis.
`random.githubusercontent.com`). Cek dilakukan dua kali: pada URL
sebelum download, dan pada `resp.RequestMessage.RequestUri` setelah
redirect (jaga-jaga kalau attacker bisa inject redirect ke host lain
via header).

### Alur baru `DownloadAndApplyAsync`

```
ReleaseInfo (dari CheckAsync)
   │
   ├─ Validasi DownloadUrl & ManifestUrl host  →  abort kalau bukan GitHub
   │
   ├─ Download EXE ke %TEMP%\PanelCalculator_update.exe
   │     ├─ Validasi final redirect host
   │     ├─ Tolak HTML content-type
   │     └─ Tolak file < 10 MB
   │
   ├─ Download manifest ke %TEMP%\PanelCalculator_update.exe.sha256
   │     ├─ Validasi final redirect host
   │     └─ Kalau gagal (404, network) → HAPUS EXE + throw UpdateVerificationException
   │
   ├─ UpdateVerifier.VerifyOrThrow(exe, manifest)
   │     ├─ Parse expected digest
   │     ├─ Compute actual digest
   │     ├─ HashesMatch?
   │     │     ├─ Ya → log OK
   │     │     └─ Tidak → HAPUS EXE + HAPUS manifest + throw
   │
   ├─ Hapus manifest sementara
   ├─ Tulis updater PS1
   └─ Spawn powershell.exe -Verb runas → Application.Exit()
```

### Catatan backwards compatibility

- **Client v1.2.3 (yang sudah ter-deploy)**: tidak peduli ada manifest
  atau tidak — masih jalan seperti biasa, tidak break. Mereka akan tetap
  menerima notifikasi update dan men-download EXE tanpa verify.
- **Client v1.2.4+**: WAJIB ada manifest di release. Tanpa manifest →
  tolak install update dengan pesan "Rilis ini tidak menyertakan file
  verifikasi keamanan… Hubungi support."
- **Release v1.2.4 sendiri** (rilis pertama yang mengandung kode ini):
  manifest WAJIB di-upload supaya client v1.2.4 di mesin user yang baru
  install dari installer bisa update ke v1.2.5+. Untuk customer v1.2.3
  yang akan update ke v1.2.4 — TIDAK ADA verifikasi karena client lama
  belum punya logic ini. Setelah satu kali update sukses ke v1.2.4,
  semua update berikutnya akan ter-verify.

### Logging

Semua step (success + failure) di-log ke:
```
%AppData%\PanelCalculator\logs\update-yyyy-MM-dd.log
```
Format:
```
[14:23:01] Begin update apply. tag=v1.2.5 target=1.2.5
[14:23:01] EXE downloaded OK. size=187,432,544 bytes, path=C:\Users\X\AppData\Local\Temp\PanelCalculator_update.exe
[14:23:14] Manifest downloaded OK. bytes=80, path=…\PanelCalculator_update.exe.sha256
[14:23:14] OK: SHA-256 hash matches manifest. Proceeding with apply.
[14:23:14] Updater PS1 written to '…\panel_calc_update.ps1'. Launching elevated.
```
Atau jika gagal:
```
[14:23:14] ABORT: SHA-256 verification FAILED. Hash SHA-256 file update yang didownload TIDAK SAMA dengan manifest resmi. …
```
Folder log adalah folder yang sama dengan `migration-*.log` dari Item #1.

### Cara test manual end-to-end

#### Test happy path (release punya manifest valid)
1. Bump version `AppVersion` di `UpdateService.cs` (mis. ke 1.2.5).
2. `dotnet publish` → `publish/PanelCalculator.exe`.
3. Jalankan `powershell .\Tools\generate_release_manifest.ps1`.
4. Konfirmasi file `publish/PanelCalculator.exe.sha256` muncul, isinya
   `<hex>  PanelCalculator.exe`.
5. Buat release di test repo (atau tag draft), upload BOTH file.
6. Pasang versi lama (1.2.3) di Windows VM, jalankan, klik tombol update.
7. Expected: progress bar jalan, log di `%AppData%\PanelCalculator\logs\`
   menunjukkan verify OK, aplikasi keluar + restart dengan versi baru.

#### Test pesimis: manifest dihapus dari release
1. Buat release tanpa upload `.sha256`.
2. Trigger update dari client.
3. Expected: MessageBox "Rilis ini tidak menyertakan file verifikasi
   keamanan (PanelCalculator.exe.sha256). Update dibatalkan. Hubungi
   support."
4. Log menunjukkan `ABORT: release has no SHA-256 manifest asset`.

#### Test pesimis: manifest valid tapi EXE diganti (simulasi tampering)
1. Generate manifest dari EXE A.
2. Upload manifest A + EXE B (yang beda, ukuran > 10 MB).
3. Trigger update.
4. Expected: download sukses, lalu verify gagal → MessageBox berisi
   "Hash SHA-256 file update yang didownload TIDAK SAMA dengan manifest
   resmi." File EXE temp dihapus. Log menunjukkan kedua hash (expected
   vs actual).

### Risiko & yang dipantau setelah deploy

1. **Maintainer lupa upload `.sha256`.** Mitigasi: warning box di
   CLAUDE.md, dan command `gh release create` sudah explicit menyertakan
   file `.sha256`. Tambahan: pertimbangkan GitHub Action yang auto-fail
   release publish kalau asset .sha256 tidak ada.
2. **Manifest yang bocor.** Manifest hanya berisi hex digest — tidak
   menambah serangan baru. Kalau attacker bisa replace BOTH file
   (EXE jahat + manifest jahat untuk EXE jahat itu) → mereka harus
   compromise GitHub release write access, yang artinya skema ini
   memang TIDAK proteksi dari compromise akun maintainer. Itu butuh
   code-signing cert (L1.3) atau Ed25519 signature dengan public key
   embedded di EXE (L1.2 opsi B di audit). Pendekatan saat ini
   melindungi dari: CDN compromise (objects.githubusercontent.com),
   DNS hijack proxy, MITM TLS strip, dan typo URL.
3. **Tidak ada certificate pinning untuk api.github.com.** Belum di-cover —
   `HttpClient` pakai default OS trust store. Untuk paranoid level,
   pertimbangkan public key pinning untuk GitHub root CA.
4. **Disk space `%TEMP%`.** Manifest hanya ~100 byte, tapi EXE temp
   ~179 MB selama proses verify. Sudah dibersihkan via `TryDelete` di
   path gagal — di path sukses akan dihapus oleh PowerShell updater
   sesudah `Copy-Item`.
5. **Latency.** Download manifest tambah 1 round-trip HTTPS (manifest
   ~100 byte, tipikal < 1 detik). Hash compute 179 MB di disk SSD modern
   ~500 ms; di HDD lama bisa 2-3 detik. UI menampilkan "Memverifikasi
   keamanan file..." selama proses.

### Yang BELUM dikerjakan (out of scope untuk item #2)

- **Authenticode code-sign** EXE (audit L1.3) — butuh sertifikat berbayar
  ($200-400/tahun). Lapisan terkuat untuk anti-tampering pasca-install.
- **Ed25519 signature manifest** (audit L1.2 opsi B) — saat ini manifest
  cuma hash polos. Kalau attacker bisa upload kedua file ke GitHub
  release, dia bisa bypass. Mitigasi: embed Ed25519 public key di EXE
  (after obfuscation L1.1), sign manifest dengan private key offline.
- **Certificate pinning** untuk `api.github.com` dan
  `objects.githubusercontent.com`. Saat ini bergantung pada CA trust
  store OS.
- **GitHub Action / CI guard** yang menolak push tag tanpa asset `.sha256`.
- **Anti-rollback**: client saat ini cuma cek `remoteVer > localVer`.
  Tidak ada blacklist versi yang dikenal compromised.

### Pertanyaan untuk reviewer

1. Setuju path log `%AppData%\PanelCalculator\logs\update-YYYY-MM-DD.log`
   (folder sama dengan migration log dari Item #1)? Atau pisah ke
   subfolder `logs\update\`?
2. Saat verify gagal, MessageBox-nya sekarang lewat catch generik di
   `ShellForm` / `SettingsForm` yang menampilkan `ex.Message`. Mau saya
   bedakan icon (`Error` vs `Warning`) dan title (`"Update Gagal Verifikasi"`
   vs `"Download Gagal"`) berdasarkan tipe exception?
3. Host allow-list saat ini hardcoded di kode (`UpdateVerifier.AllowedDownloadHosts`).
   Tidak perlu config — tapi kalau suatu hari GitHub pindah CDN, kita
   harus release versi baru. Itu OK?

---

## Item #3 — Aktifkan Obfuscar di build pipeline

**Tanggal:** 2026-05-16
**Status:** Implementasi selesai — `dotnet build Release` masih clean (Obfuscar
hanya berjalan di `Installer/build.bat`, bukan di MSBuild). Verifikasi end-to-end
output EXE perlu dijalankan dari mesin dev (butuh Inno Setup terinstall).
**Branch:** `claude/sharp-jemison-2e465a`
**Referensi audit:** §2.4 + §4 L1.1 / L3.1 → severity HIGH.

### Tujuan

Sebelum perubahan ini, `Installer/build.bat` (multi-file installer flow)
**dengan sengaja** men-skip Obfuscar dan langsung `xcopy app-raw → app-final`,
dengan komentar "Obfuscation dinonaktifkan: tidak kompatibel dengan .NET 8".
Hasilnya: EXE produksi di customer punya IL telanjang yang bisa di-decompile
satu klik dengan dnSpy / ILSpy, sehingga:

- Pepper di `MachineKeyProvider.cs` (item #1) terlihat sebagai string literal
  dengan XOR mask 1-byte yang trivial untuk di-reverse.
- Algoritma derivasi key SQLCipher terbuka — attacker bisa replikasi key
  di mesin korban.
- URL endpoint update (`api.github.com/repos/FAP-TRY/...`) terlihat plain →
  memudahkan targeted attack di update channel.
- Logic kalkulasi margin / harga, struktur DI container, dan semua nama
  method/field private terbuka penuh.

Catatan: incompatibility yang dimaksud di komentar lama merujuk ke Obfuscar
versi 2.2.30 ke bawah yang masih targetkan netcore3.1 runtime. Versi
`Obfuscar.GlobalTool` 2.2.39+ targetkan net6.0 dan jalan mulus terhadap
assembly .NET 8.

### File yang diubah / ditambah

| File | Jenis | Ringkasan |
|------|-------|-----------|
| `Installer/build.bat` | EDIT | Step baru `[3/5]`: cek/install `Obfuscar.GlobalTool` global, jalankan terhadap `obfuscar.xml`. Step `[4/5]`: overlay 3 DLL obfuscated (`PanelCalculator.{WinForms,Core,Data}.dll`) di atas `app-final` yang berisi runtime DLL plain. Step counter di-rename dari `[1/4]…[4/4]` menjadi `[1/5]…[5/5]`. Probe Obfuscar di PATH + fallback ke `%USERPROFILE%\.dotnet\tools\obfuscar.exe` (PATH bisa belum refresh di session yang sama dengan install). |
| `Installer/obfuscar.xml` | EDIT | (1) Komentar header di-update untuk explain kenapa re-enabled + safety constraints. (2) `Var MarkedOnly=false` ditambah eksplisit (default Obfuscar, tapi jadi documentation). (3) `<SkipType>` `Program` di module WinForms (defense-in-depth — class sudah punya `[Obfuscation(Exclude=true)]`). (4) `<SkipNamespace>` `PanelCalculator.Core.Models` — semua EF entity. (5) `<SkipType>` `PanelCalculatorContext` di module Data — DbContext + entity config. (6) `<SkipMethod>` `MachineKeyProvider.GetKey`, `DbMigrator.MigrateIfNeeded`, `UpdateVerifier.*` — entry-point yang dipanggil dari Program.cs by name. |
| `CLAUDE.md` | EDIT | Section baru "Build Installer (Inno Setup multi-file) — `Installer\build.bat`" tepat setelah box PENTING update verification — menjelaskan pipeline 5-step + estimasi waktu +30 detik. |

### Settings Obfuscar yang dipakai (recap)

| Switch | Nilai | Alasan |
|--------|-------|--------|
| `RenameMethods` | `true` | Main protection layer — semua nama method private/internal jadi `a()`, `b()`, dst. |
| `RenameFields` | `true` | Field name terhapus dari IL. |
| `RenameProperties` | `false` | EF Core map kolom DB by property name (reflection). Wajib off. |
| `RenameTypes` | `false` | EF Core pakai `Type.FullName` sebagai nama tabel; DI container resolve Form by Type. Wajib off. |
| `HideStrings` | `true` | **Pepper di MachineKeyProvider** + URL update + path DB + pesan error → semua di-encrypt di IL, decode runtime via helper di-inject. |
| `SuppressILdasm` | `true` | Marker bahwa assembly sudah di-obfuscate (psikologis untuk attacker). |
| `KeepPublicApi` | `false` | DLL kita tidak di-konsumsi external — boleh obfuscate semua public. |
| `UseUnicodeNames` | `false` | Stack trace masih ASCII → mudah dibaca kalau customer kirim screenshot error. |

### Skip rules per assembly

```
PanelCalculator.WinForms.dll
  └─ SkipType "Program"          → entry point, [STAThread] + Main signature

PanelCalculator.Core.dll
  └─ SkipNamespace "Models"      → semua entity EF (Product, Estimation, dst.)

PanelCalculator.Data.dll
  ├─ SkipType    "PanelCalculatorContext"  → OnModelCreating reflection
  ├─ SkipMethod  MachineKeyProvider.GetKey
  ├─ SkipMethod  DbMigrator.MigrateIfNeeded
  └─ SkipMethod  UpdateVerifier.* (semua public method)
```

Alasan skip method: walaupun `RenameTypes=false` + `KeepPublicApi=false`
gabungannya akan tetap me-rename method public, kita pakai `<SkipMethod>`
sebagai *belt and braces* — Program.cs memanggil `MachineKeyProvider.GetKey()`,
`DbMigrator.MigrateIfNeeded(...)`, dan `UpdateService.cs` memanggil
`UpdateVerifier.VerifyOrThrow(...)`. Kalau di-rename, MethodNotFoundException
saat runtime.

### Alur build baru (5 step)

```
build.bat
  │
  ├─ [1/5] Clean: hapus app-raw, app-obf, app-final, Output
  │
  ├─ [2/5] dotnet publish → Installer\app-raw\
  │         (self-contained, multi-file, ReadyToRun, ~300 file)
  │
  ├─ [3/5] Obfuscar
  │         ├─ Cek `obfuscar` di PATH
  │         ├─ Kalau tidak ada → `dotnet tool install --global Obfuscar.GlobalTool`
  │         ├─ Fallback ke %USERPROFILE%\.dotnet\tools\obfuscar.exe
  │         └─ Jalankan: obfuscar obfuscar.xml
  │               input:  app-raw\PanelCalculator.{WinForms,Core,Data}.dll
  │               output: app-obf\PanelCalculator.{WinForms,Core,Data}.dll
  │
  ├─ [4/5] Assemble app-final
  │         ├─ xcopy app-raw → app-final (semua file termasuk runtime DLL)
  │         ├─ copy /y app-obf\*.dll → app-final\ (overlay 3 DLL aplikasi)
  │         └─ del app-final\*.pdb (strip debug symbol)
  │
  └─ [5/5] ISCC PanelCalculatorSetup.iss → Output\PanelCalculatorSetup_v*.exe
```

Penting: yang di-obfuscate HANYA 3 DLL aplikasi. ~300 file runtime .NET
(`System.*.dll`, `Microsoft.*.dll`, `coreclr.dll`, `e_sqlcipher.dll`, dll.)
dan `iText.*`, `ClosedXML.*`, `EF Core` reference DLL tetap apa adanya —
Obfuscar baca mereka sebagai reference untuk type resolution, tidak menulis.

### Dampak ke layer keamanan lain

1. **Pepper `MachineKeyProvider` (item #1).** Sebelumnya pepper di-obfuscate
   manual dengan XOR 1-byte (cleartext "TTS-PanelCalc-pepper-2026-v1"). Setelah
   Obfuscar `HideStrings=true` aktif, byte array `obf` dan literal lain di
   method ini di-wrap dengan encrypted-string helper. XOR manual tetap
   dipertahankan sebagai defense-in-depth — kalau attacker bisa bypass
   string decryptor Obfuscar, dia masih lihat byte XOR-masked, bukan plain
   "TTS-PanelCalc-pepper-...".

2. **URL update (item #2).** `UpdateService.cs` punya `https://api.github.com/...`
   hardcoded. Sekarang masuk ke encrypted string table. **Tapi** `UpdateVerifier.AllowedDownloadHosts`
   array (di Data.dll) juga di-encrypt — host allow-list tetap berfungsi karena
   decryption terjadi sebelum compare.

3. **Resource loading.** `MainForm`, `LoginForm`, `ShellForm` panggil
   `typeof(X).Assembly.GetManifestResourceStream("PanelCalculator.WinForms.Assets.logo.png")`.
   String resource name akan di-encrypt oleh `HideStrings=true`, tapi
   decode di runtime. Resource manifest table tidak di-rename oleh Obfuscar.
   Verifikasi manual: jalankan EXE → cek logo TTS muncul di Login/Shell/Main.

### Yang TIDAK di-obfuscate (dan kenapa)

- **Type names (semua assembly).** `RenameTypes=false`. EF Core 8 default
  pakai `Type.FullName` sebagai nama tabel kalau tidak ada `[Table]`.
- **Property names (semua entity).** `RenameProperties=false` + explicit
  `SkipNamespace Models`. EF Core column mapping berbasis property name.
- **`PanelCalculatorContext`.** `OnModelCreating` panggil
  `modelBuilder.Entity<Product>(e => e.HasKey(...))` — `HasKey` expression
  ter-translate ke property name via reflection.
- **3 entry-point method security (MachineKeyProvider.GetKey,
  DbMigrator.MigrateIfNeeded, UpdateVerifier.*).** Dipanggil cross-assembly
  by name dari Program.cs / UpdateService.cs.
- **PanelCalculator.WinForms.Program.** Entry point + apphost wiring.

### Cara test manual (untuk reviewer / maintainer)

Karena Obfuscar hanya jalan di `build.bat` (bukan di MSBuild normal),
verifikasi `dotnet build` / `dotnet test` saja TIDAK cukup. Step lengkap:

#### 1. Verifikasi MSBuild normal masih clean

```powershell
dotnet build PanelCalculator.sln --configuration Release
dotnet test PanelCalculator.Tests/PanelCalculator.Tests.csproj
```

Expected: 0 warning, 36 test pass. Tidak ada perubahan dari kondisi item #2,
karena Obfuscar tidak terpanggil di sini.

#### 2. Verifikasi pipeline build.bat

```powershell
cd Installer
.\build.bat
```

Expected output (5 step):
```
[1/5] Membersihkan hasil build sebelumnya...
[OK] Bersih.
[2/5] Mempublish aplikasi (self-contained, multi-file)...
[OK] Publish selesai.
[3/5] Mengaktifkan Obfuscar (rename method/field + encrypt string)...
[OK] Obfuscar     : C:\Users\<you>\.dotnet\tools\obfuscar.exe
       (atau "obfuscar" kalau di PATH)
[OK] Obfuscation selesai.
[4/5] Menyiapkan app-final (overlay obfuscated DLLs)...
[OK] app-final siap (DLL aplikasi sudah ter-obfuscate).
[5/5] Mengompilasi installer dengan Inno Setup...
[OK] Selesai.
```

#### 3. Smoke test EXE

```powershell
# Setelah build.bat selesai:
.\Installer\app-final\PanelCalculator.WinForms.exe
```

Expected:
- Login screen muncul dengan logo TTS.
- `admin` / `admin` masuk.
- Katalog produk tampil dengan harga.
- Buat 1 estimasi, save, export PDF — semua jalan.
- DB tetap encrypted (cek dengan DB Browser — gagal buka tanpa key).

Kalau ada `MissingMethodException` atau `TypeLoadException` saat startup —
brarti rule skip di `obfuscar.xml` kurang. Tambah `<SkipType>` atau
`<SkipMethod>` sesuai stack trace.

#### 4. Verifikasi IL ter-obfuscate

Pakai ILSpy / dnSpy / `ildasm`:

```powershell
& "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe" `
  ".\Installer\app-final\PanelCalculator.WinForms.dll"
```

Atau ILSpy GUI. Cek:
- Method private di `LoginForm`, `MainForm`, dst. punya nama 1-2 char (`a`, `b`, `c`).
- String literal yang tadinya `"PanelCalculator.WinForms.Assets.logo.png"`
  tidak muncul polos di string table — terlihat sebagai byte array +
  panggilan ke helper decrypt.
- Class `Program` masih bernama `Program` dengan method `Main`.
- Class `Product`, `Estimation`, `User`, dst. masih punya nama property
  asli (`ProductId`, `Price`, `Username`).

### Risiko & yang dipantau setelah deploy

1. **Migration breakage di rilis pertama dengan obfuscation aktif.**
   Walaupun kita sudah skip EF entity + DbContext, ada kemungkinan edge
   case yang ketinggalan (mis. EF Core internal yang panggil method by
   string). Mitigasi: full regression test di staging sebelum rilis,
   bukan langsung ke customer.

2. **Crash log dari customer jadi sulit dibaca.** Method `a()` di stack
   trace tidak bisa langsung di-debug. Mitigasi: simpan file mapping
   `Obfuscar.xml.map` (auto-generated di `app-obf\Mapping.txt`) per
   release, beri tag versi. Saat customer report crash, terjemahkan
   stack trace manual.

3. **AV false positive.** Obfuscated .NET assembly kadang trigger
   heuristic SmartScreen / Windows Defender. Mitigasi: Authenticode
   sign EXE (audit L1.3, belum dikerjakan) — sekali signed, AV trust
   meningkat drastis. Sementara: kalau ada false positive, sumbit
   sample ke Microsoft Submission Portal.

4. **Auto-install behaviour di build server.** Step `[3/5]` panggil
   `dotnet tool install --global Obfuscar.GlobalTool`. Di mesin dev
   pribadi: aman. Di CI/CD: mungkin perlu pre-install di Dockerfile
   atau workflow YAML supaya idempotent.

5. **`app-obf` directory tidak di-`.gitignore`.** Auto-generated saat
   build, sama seperti `app-raw` / `app-final` / `Output`. Sudah di-clean
   di step `[1/5]` setiap run. Cek apakah folder ini di-ignore di repo —
   kalau belum, tambah.

### Yang BELUM dikerjakan (out of scope untuk item #3)

- **ConfuserEx 2.** Alternatif lebih agresif (control flow obfuscation,
  anti-debug, anti-tamper). Trade-off: lebih sering false-positive AV,
  startup lebih lambat ~200ms. Pertimbangkan jika audit periode berikut
  masih merekomendasikan reverse-engineering protection lebih kuat.
- **`crossgen2` ulang setelah obfuscation.** R2R image untuk 3 DLL kita
  hilang karena Obfuscar tidak preserve native pre-compilation. Startup
  aplikasi sedikit lebih lambat (~50-100ms). Bisa diatasi dengan run
  `crossgen2` setelah Obfuscar, sebelum copy ke app-final. Belum saya
  kerjakan karena efeknya marginal.
- **Mapping file archive.** `Obfuscar` generate `Mapping.txt` di
  `app-obf\` — simpan per release untuk symbolicate stack trace
  customer. Belum ada workflow upload mapping ke release artifact
  (mis. `gh release upload`). Bisa ditambah di build.bat step [3/5].
- ~~**Skipping di `PanelCalculator.iss` (single-file flow).** File
  `PanelCalculator.iss` di root masih pakai single-file publish yang
  dijalankan manual via langkah di CLAUDE.md — tidak lewat build.bat,
  jadi Obfuscar TIDAK kena. Kalau mau, single-file flow juga harus
  punya Obfuscar step. Saat ini multi-file flow (`build.bat` +
  `PanelCalculatorSetup.iss`) yang di-cover.~~ → **DITUTUP via
  follow-up Item #3b (lihat di bawah).**
- **Verifikasi end-to-end di mesin dev.** Saya tidak bisa run
  `build.bat` dari sandbox ini (butuh Inno Setup interaktif). Maintainer
  perlu run sekali manual sebelum rilis pertama dengan obfuscation aktif.

### Pertanyaan untuk reviewer

1. Mau aktifkan juga Obfuscar untuk single-file flow di `PanelCalculator.iss`
   (yang ada di CLAUDE.md "Cara Rilis Versi Baru" step #2)? Atau cukup
   multi-file flow (`build.bat`) saja?
2. Mapping file `Obfuscar` (`app-obf\Mapping.txt`) — mau diarsipkan ke
   GitHub Release sebagai asset privat? Tanpa ini, stack trace dari
   customer crash sulit di-debug.
3. Kalau pertama kali build di mesin tanpa internet → `dotnet tool install`
   gagal → build error. Mau saya tambah cara "offline" (download .nupkg
   sekali, pasang dari local cache)? Atau cukup assume build server selalu
   online?
4. `crossgen2` ulang setelah Obfuscar untuk regain R2R startup speed —
   priority rendah atau perlu sekarang?

---

## Item #3b — Obfuscar untuk single-file release EXE (gap closure dari Item #3)

**Tanggal:** 2026-05-16
**Status:** Implementasi selesai — `dotnet build Release` clean (0 warning),
36/36 test pass. End-to-end script verification perlu dijalankan dari mesin dev
(butuh download/install Obfuscar.GlobalTool sekali; offline-blocked di sandbox CI saat ini).
**Branch:** `claude/sharp-jemison-2e465a`
**Referensi:** Item #3 "Pertanyaan untuk reviewer" #1 + audit §2.4 / L1.1.

### Gap yang ditutup

Item #3 hanya mengaktifkan Obfuscar di flow **multi-file installer** (`Installer\build.bat`
→ Inno Setup `PanelCalculatorSetup.iss`). Tapi customer **tidak download installer
multi-file via auto-update** — mereka download single-file EXE (`PanelCalculator.exe`,
~179 MB) langsung dari GitHub Releases asset, sesuai flow `UpdateService.cs`.

EXE itu sebelumnya di-build via `dotnet publish ... -p:PublishSingleFile=true`
manual (CLAUDE.md "Cara Rilis Versi Baru" step #2) — **tanpa Obfuscar**. Artinya
3 DLL aplikasi (`PanelCalculator.WinForms/Core/Data`) yang ter-bundle di dalam
single-file EXE itu IL-nya telanjang dan bisa di-decompile dengan ILSpy/dnSpy
satu klik. Item #3 keseluruhan dampak proteksinya ter-bypass untuk customer
yang upgrade via auto-update (≈99% kasus).

### File yang diubah / ditambah

| File | Jenis | Ringkasan |
|------|-------|-----------|
| `Tools/build-release-singlefile.ps1` | NEW | Wrapper 5-step: (1) clean staging, (2) `dotnet publish -p:PublishSingleFile=false` ke `release-build-staging\app-raw\`, (3) auto-install + run Obfuscar terhadap 3 DLL dengan config `Installer\obfuscar-singlefile.xml`, (4) overlay 3 DLL ter-obfuscate ke `PanelCalculator.WinForms\bin\Release\net8.0-windows\win-x64\` lalu `dotnet publish -p:PublishSingleFile=true --no-build` (bundler picks up overlaid DLL), (5) copy EXE final ke `publish\PanelCalculator.exe` + chain ke `Tools\generate_release_manifest.ps1`. Flag `-SkipObfuscation` tersedia sebagai escape hatch untuk debug. |
| `Installer/obfuscar-singlefile.xml` | NEW | Konfigurasi Obfuscar untuk flow ini. Identik dengan `Installer/obfuscar.xml` (skip rules sama) kecuali I/O path: input `release-build-staging\app-raw\`, output `release-build-staging\app-obf\`. Komentar header menjelaskan kewajiban sync dengan `obfuscar.xml` kalau salah satu di-edit. |
| `.gitignore` | EDIT | Tambah `release-build-staging/` supaya folder staging tidak ke-commit. |
| `CLAUDE.md` | EDIT | "Cara Rilis Versi Baru" step #2 di-rewrite: ganti perintah `dotnet publish` polos + `cp` + `generate_release_manifest.ps1` (tiga step terpisah) menjadi satu panggilan ke `Tools\build-release-singlefile.ps1`. Step renumbered (sebelumnya 1-6, sekarang 1-4). Box PENTING baru menjelaskan kenapa script ini wajib + warning untuk flag `-SkipObfuscation`. |

### Cara kerja step 4 (titik tricky)

`dotnet publish -p:PublishSingleFile=true` bekerja sebagai berikut di .NET 8:

```
ComputeFilesToPublish  →  baca file list dari bin\<Config>\<TFM>\<RID>\
        ↓
CopyFilesToPublishDirectory  →  copy ke bin\<Config>\<TFM>\<RID>\publish\
        ↓
GenerateSingleFileBundle  →  baca semua file di publish\, pack ke 1 EXE
```

Dengan flag `--no-build`, target `Build` (kompilasi) DI-SKIP — tapi tiga target
publish di atas TETAP jalan. Jadi triknya:

1. Step [2/5] menjalankan `dotnet publish` PENUH (multi-file). Side-effect penting:
   ini juga memicu Build, yang menulis 3 DLL aplikasi ke
   `PanelCalculator.WinForms\bin\Release\net8.0-windows\win-x64\` (build cache).
2. Step [3/5] Obfuscar baca dari `release-build-staging\app-raw\` (output multi-file)
   dan tulis hasil ter-obfuscate ke `release-build-staging\app-obf\`.
3. Step [4/5] **overlay** 3 DLL ter-obfuscate dari `app-obf\` ke
   `PanelCalculator.WinForms\bin\Release\net8.0-windows\win-x64\` (replace yang plain),
   lalu jalankan `dotnet publish -p:PublishSingleFile=true --no-build`.
4. Karena `--no-build`, Build target tidak overwrite overlay kita; ComputeFilesToPublish
   baca DLL yang sudah ter-obfuscate; CopyFilesToPublishDirectory copy ke `publish\`;
   bundler pack ke single-file EXE. Hasil: 3 DLL ter-bundle di dalam EXE adalah
   versi ter-obfuscate.

Step [1/5] secara eksplisit menghapus `bin\<...>\publish\` (sisa publish lama)
supaya tidak ada stale file yang ikut ter-bundle (bundler tidak re-evaluate
file yang sudah ada di sana dari run sebelumnya).

### Settings Obfuscar (recap)

Sama persis dengan `Installer/obfuscar.xml` — hanya I/O path yang beda. Skip rules:

```
PanelCalculator.WinForms.dll
  └─ SkipType "Program"          → entry point, [STAThread] + Main signature

PanelCalculator.Core.dll
  └─ SkipNamespace "Models"      → semua entity EF (Product, Estimation, dst.)

PanelCalculator.Data.dll
  ├─ SkipType    "PanelCalculatorContext"
  ├─ SkipMethod  MachineKeyProvider.GetKey
  ├─ SkipMethod  DbMigrator.MigrateIfNeeded
  └─ SkipMethod  UpdateVerifier.*
```

Maintenance note: kalau salah satu config (`obfuscar.xml` atau `obfuscar-singlefile.xml`)
diedit, edit yang satu lagi juga — keduanya harus konsisten. Sekarang kedua file
ada header komentar yang reminder hal ini.

### Verifikasi yang sudah dijalankan (di sandbox)

1. `dotnet build PanelCalculator.sln --configuration Release` → **0 warning, 0 error**.
2. `dotnet test PanelCalculator.Tests --no-build` → **36/36 pass**.
3. `dotnet publish ... -p:PublishSingleFile=false --output release-build-staging\app-raw`
   → 3 DLL aplikasi muncul di `app-raw\` (verified via Glob).
4. `dotnet publish ... -p:PublishSingleFile=true --no-build` → single-file EXE
   muncul di `bin\<...>\publish\PanelCalculator.WinForms.exe`. Validates that
   the `--no-build` + `PublishSingleFile=true` chain works as expected.

### Verifikasi yang BELUM dijalankan (perlu di mesin dev)

5. Full end-to-end run script: `.\Tools\build-release-singlefile.ps1` —
   sandbox CI ini block `dotnet tool install` (network), jadi Obfuscar tidak
   bisa di-install otomatis. Maintainer perlu run sekali manual:
   ```powershell
   dotnet tool install --global Obfuscar.GlobalTool   # sekali saja
   .\Tools\build-release-singlefile.ps1
   ```
   Expected: output 5-step log, `publish\PanelCalculator.exe` muncul (~179 MB),
   `publish\PanelCalculator.exe.sha256` muncul, SHA-256 ter-print di console.

6. **Smoke test EXE final**: jalankan `.\publish\PanelCalculator.exe` di Windows VM
   bersih. Login `admin`/`admin` → buka katalog → buat estimasi → save → export
   PDF. Kalau ada `MissingMethodException` / `TypeLoadException`, brarti skip rule
   di `obfuscar-singlefile.xml` perlu ditambah (sama treatment dengan `obfuscar.xml`).

7. **Verifikasi IL ter-obfuscate**: extract single-file bundle pakai
   [`SingleFileExtractor`](https://github.com/Sebastian1989101/SingleFileExtractor)
   atau ILSpy yang sudah punya support single-file unbundling. Cek 3 DLL
   `PanelCalculator.*.dll` di dalamnya → method private harus `a()`, `b()`,
   string literal "TTS-PanelCalc-pepper-..." tidak muncul polos di string table.

### Dampak ke customer

**Tidak ada dampak visible.** EXE-nya tetap `PanelCalculator.exe` dengan ukuran
hampir sama (Obfuscar nambah ~50-100 KB ke 3 DLL aplikasi yang terbundle, tidak
terasa di 179 MB total). Startup, behavior, semua identik. Yang berubah hanya:
ekstrak EXE → decompile DLL aplikasi → sekarang ketemu IL ter-obfuscate, bukan
source-near IL seperti sebelumnya.

### Risiko & yang dipantau

1. **SDK target order berubah** di .NET 9/10 → trik `--no-build` mungkin tidak
   reliable. Mitigasi: tambah test di CI yang ekstrak single-file EXE dan
   `grep` string "TTS-PanelCalc-pepper" — kalau ketemu, build di-fail.
2. **Mesin tanpa internet** → `dotnet tool install` step [3/5] gagal. Same
   risk dengan `build.bat`. Mitigasi sama: pre-install Obfuscar di build server.
3. **Two Obfuscar configs drift**. `Installer/obfuscar.xml` dan
   `Installer/obfuscar-singlefile.xml` punya skip rules yang HARUS sama.
   Header komentar di kedua file remind hal ini, tapi enforcement masih manual.
   Pertimbangkan: extract skip rules ke file shared `obfuscar-skips.xml` yang
   di-include via XInclude (Obfuscar belum support `<xi:include>` AFAIK —
   need verifying).
4. **Mapping file `app-obf\Mapping.txt`** — sama issue dengan Item #3, belum
   diarsipkan per release. Stack trace customer crash di method ter-obfuscate
   sulit di-debug tanpa file ini.

### Yang BELUM dikerjakan

- Pre-install Obfuscar di CI workflow (kalau CI dipakai untuk release build).
- Symbolicator workflow (mapping file archive per release).
- `crossgen2` re-run setelah Obfuscar (R2R image hilang).
- Unit test yang verifikasi single-file EXE bener-bener ter-obfuscate (mis. via
  `SingleFileExtractor` + Mono.Cecil ASSERT method name `a` ada).

### Pertanyaan untuk reviewer

1. Saya bikin config Obfuscar baru (`obfuscar-singlefile.xml`) yang duplikasi
   skip rules dari `obfuscar.xml`. Lebih bagus: extract ke shared include
   file, atau biarkan dua file dengan reminder comment? (Pilihan kedua sekarang
   yang saya pakai — lebih simpel, risk drift kecil karena skip rules jarang
   berubah).
2. `release-build-staging/` di repo root — OK lokasinya? Atau pindah ke
   `Installer/release-build-staging/` supaya satu folder dengan flow multi-file?
3. Flag `-SkipObfuscation` di script — perlu juga ada di `build.bat`? Sekarang
   hanya single-file flow yang punya escape hatch.

---

## Item #4 — Migrate password hashing ke BCrypt (legacy SHA-256 fallback)

**Tanggal:** 2026-05-16
**Status:** Implementasi selesai — `dotnet build Release` clean (0 error, 1 warning
pre-existing di `DashboardForm.cs:489` `AddQueueDialog.CompanyName` — tidak terkait
item ini), `dotnet test` 46/46 pass (36 existing + 10 baru).
**Branch:** `claude/sharp-jemison-2e465a`
**Referensi audit:** §2.3 / L1.4 / QW3 → severity HIGH.

### Tujuan

Sebelum perubahan ini, password user di-hash dengan SHA-256 telanjang (tanpa salt,
tanpa work factor) dan logic-nya **duplikat di 4 file**:

- `PanelCalculator.WinForms/Forms/LoginForm.cs:300-305` (`HashPassword`)
- `PanelCalculator.WinForms/Program.cs:186-191` (`HashPassword`)
- `PanelCalculator.WinForms/Forms/SettingsForm.cs:990-994` (`HashPassword`)
- `PanelCalculator.WinForms/Forms/UserManagementForm.cs:232-236` (`HashPassword`)

Konsekuensi: rainbow-table attack instan untuk password lemah ("admin",
"admin123", nama orang). DB yang dicuri (sebelum item #1 = trivial via file
copy; setelah item #1 = butuh akses runtime mesin korban) langsung memberikan
plaintext password lewat lookup pre-computed.

Sekarang: satu kelas `PasswordHasher` yang dipakai semua call site. Hash baru =
BCrypt (work factor 12, salt random per-call). Login terhadap hash legacy
masih jalan, dan hash legacy di-upgrade ke BCrypt **silent** pada login
berikutnya — eventual migration tanpa intervensi customer.

### File yang diubah / ditambah

| File | Jenis | Ringkasan |
|------|-------|-----------|
| `PanelCalculator.Core/PanelCalculator.Core.csproj` | EDIT | Tambah `<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />` + `<InternalsVisibleTo Include="PanelCalculator.Tests" />` supaya unit test bisa akses helper `LegacySha256` (internal, hanya untuk simulasi data legacy di test). |
| `PanelCalculator.Core/Security/PasswordHasher.cs` | NEW | API public: `Hash(string) → "bcrypt$<hash>"` dan `Verify(string password, string storedHash, out bool needsUpgrade) → bool`. Detect format hash via prefix `"bcrypt$"`; tanpa prefix → fallback SHA-256 dengan `CryptographicOperations.FixedTimeEquals`. Catch `BCrypt.Net.SaltParseException` supaya hash rusak di DB → auth fail (bukan crash). `internal static LegacySha256(string)` exposed ke test via InternalsVisibleTo. |
| `PanelCalculator.WinForms/Forms/LoginForm.cs` | EDIT | (1) Drop import `System.Security.Cryptography` + `System.Text`. (2) Tambah `using PanelCalculator.Core.Security`. (3) `DoLogin()`: lookup user hanya by username+IsActive (bukan lagi by `PasswordHash == HashPassword(...)` di SQL). Verify password in-process via `PasswordHasher.Verify`. Kalau `needsUpgrade==true` → re-hash dengan BCrypt + persist di `SaveChanges` yang sudah ada. (4) Hapus method `HashPassword`. |
| `PanelCalculator.WinForms/Program.cs` | EDIT | (1) Drop imports `System.Security.Cryptography` + `System.Text`. (2) Tambah `using PanelCalculator.Core.Security`. (3) `SeedDefaultAdmin`: kalau user `admin` belum ada → buat dengan `PasswordHasher.Hash("admin")` (BCrypt). Kalau user `admin` sudah ada DAN password-nya cocok dengan legacy "admin123" → reset ke "admin" (BCrypt). Verify pakai `PasswordHasher.Verify` jadi cek "admin123" jalan untuk hash legacy maupun BCrypt. (4) Hapus method `HashPassword`. |
| `PanelCalculator.WinForms/Forms/SettingsForm.cs` | EDIT | Replace 3 panggilan `HashPassword(dlg.NewPassword)` → `PasswordHasher.Hash(dlg.NewPassword)` (add user, edit user, reset password). Hapus method local `HashPassword`. Bersihkan import. |
| `PanelCalculator.WinForms/Forms/UserManagementForm.cs` | EDIT | Idem: 3 panggilan + drop method local + bersihkan import. |
| `Installer/obfuscar.xml` | EDIT | Tambah `<SkipMethod type="PanelCalculator.Core.Security.PasswordHasher" name="*" />` di module Core. Tanpa ini, dengan `KeepPublicApi=false`, method `Hash`/`Verify` bisa di-rename dan call dari WinForms break dengan `MissingMethodException`. |
| `Installer/obfuscar-singlefile.xml` | EDIT | Idem — kedua config Obfuscar harus sync (lihat note Item #3b). |
| `PanelCalculator.Tests/Security/PasswordHasherTests.cs` | NEW | 10 test (lihat di bawah). |

### Format hash di kolom `Users.PasswordHash`

Schema DB **tidak berubah** — kolom tetap `TEXT`. Discriminator pakai prefix string:

```
bcrypt$$2a$12$RandomSaltHere...               ← hash baru (BCrypt, ~67 char total)
e3afed0047b08059d0fada10f400c1e5...           ← hash legacy SHA-256 (64 lower hex)
                                                 (tanpa prefix apa-apa)
```

`PasswordHasher.Verify` cek prefix:
- Starts with `"bcrypt$"` → strip prefix, panggil `BCrypt.Net.BCrypt.Verify`. Catch `SaltParseException` → return false.
- Tidak ada prefix → hash input pakai SHA-256, compare constant-time dengan stored hash. Kalau cocok → `needsUpgrade=true`.

### Alur upgrade login (silent migration)

```
User klik Login
   │
   ├─ LoginForm.DoLogin()
   │     ├─ context.Users.FirstOrDefault(u => username && IsActive)
   │     │     (TIDAK lagi filter by PasswordHash di SQL — verify in-process)
   │     │
   │     ├─ PasswordHasher.Verify(password, user.PasswordHash, out needsUpgrade)
   │     │     ├─ Format BCrypt? → BCrypt.Verify
   │     │     ├─ Format legacy? → SHA-256 hash + FixedTimeEquals
   │     │     └─ Match? → return true
   │     │
   │     ├─ Verify false → ShowError + return
   │     │
   │     ├─ Verify true + needsUpgrade
   │     │     └─ user.PasswordHash = PasswordHasher.Hash(password)
   │     │         (akan ikut ke-persist di SaveChanges di bawah)
   │     │
   │     ├─ user.LastLoginDate = UtcNow
   │     ├─ context.SaveChanges()
   │     └─ DialogResult.OK
```

Hasil di DB setelah login pertama post-upgrade: user yang dulu hash-nya SHA-256
sekarang punya hash BCrypt — silent, tanpa user diminta apa-apa.

### Settings BCrypt

- **Algorithm:** BCrypt (default revision `$2a$` di BCrypt.Net-Next 4.x).
- **Work factor (cost):** 12 — ~250 ms hash di CPU desktop modern. Standar
  industri 2024+ (OWASP recommendation `cost >= 10`, banyak vendor pakai 12).
- **Salt:** 16-byte random per hash (BCrypt internal — kita tidak perlu generate).
- **Library:** `BCrypt.Net-Next` v4.0.3 — fork community yang masih maintain
  (BCrypt.Net original sudah abandon). Latest stable.

### Test coverage

Test di `PanelCalculator.Tests/Security/PasswordHasherTests.cs` (10 test):

1. `Hash_ReturnsBcryptPrefixedString` — output starts with `"bcrypt$"` + total length sane.
2. `Hash_GeneratesDifferentSaltEachTime` — dua hash dari password yang sama tidak boleh sama (verify lemma BCrypt random salt). Plus assert keduanya tetap verify true.
3. `Verify_Bcrypt_Correct_ReturnsTrue_NoUpgradeNeeded` — round-trip BCrypt happy path.
4. `Verify_Bcrypt_Wrong_ReturnsFalse` — wrong password → false, no upgrade.
5. `Verify_LegacySha256_Correct_ReturnsTrueAndNeedsUpgrade` — fallback path: legacy hash matches → true + flag.
6. `Verify_LegacySha256_Wrong_ReturnsFalse` — wrong password against legacy hash → false.
7. `Verify_LegacySha256_KnownVector_MatchesProductionFormat` — hardcoded SHA-256 hex dari "admin" (`8c6976e5...8a918`) — anchor regression test supaya format hash legacy yang sudah ada di customer DB tetap di-recognize. Kalau test ini break, brarti ada implementasi yang tidak sengaja ubah encoding.
8. `Verify_EmptyStoredHash_ReturnsFalse` — defensive: kalau DB column kebetulan kosong → tidak crash, return false.
9. `Verify_MalformedBcryptHash_ReturnsFalse_DoesNotThrow` — `bcrypt$garbage` → catch exception → return false (jangan crash login form).
10. `RoundTrip_UpgradeFlow_NewHashVerifiesWithoutUpgrade` — simulasi 4-step alur upgrade real: legacy → verify+needsUpgrade → re-hash dengan BCrypt → verify lagi → tidak butuh upgrade lagi.

### Verifikasi build + test

```
dotnet build PanelCalculator.sln --configuration Release  → 0 error, 1 warning
                                                            (warning pre-existing
                                                            di DashboardForm.cs:489,
                                                            tidak terkait item ini)

dotnet test PanelCalculator.Tests/PanelCalculator.Tests.csproj
                                                          → 46/46 pass
                                                            (36 existing + 10 baru)
```

### Risiko & yang dipantau setelah deploy

1. **Login pertama post-upgrade lebih lambat ~250 ms.** Itu BCrypt re-hash
   yang jalan begitu legacy hash sukses di-verify. Sekali per user; setelah
   itu hanya BCrypt.Verify (~250 ms juga, tapi tidak diikuti re-hash).
   Tidak ada UI feedback "memproses" — kalau customer report "login lemot",
   pertimbangkan tambah small spinner di LoginForm.

2. **User yang tidak pernah login lagi → tidak ter-migrate.** Kalau ada
   akun lama yang sudah lama tidak dipakai, hash-nya akan stay SHA-256
   selamanya. Bukan masalah security (mereka tidak akan diserang via
   password yang tidak pernah dipakai), tapi bisa jadi tech debt. Mitigasi
   nanti: script "force re-hash on next login" via flag `MustChangePassword`
   yang sudah disebut di audit §10.

3. **Default admin password "admin"/"admin"** — tetap di-seed. Setelah
   upgrade, hash-nya BCrypt dari "admin" (bukan SHA-256). Itu lebih kuat
   secara algoritma tapi tetap mudah ditebak. Audit §10 / item rekomendasi
   "force change password on first login for default admin" → out of scope
   untuk item #4 ini; ditangani di item terpisah.

4. **Field signature `Hash` / `Verify` di Obfuscar.** Kalau suatu hari ada
   refactor yang ubah nama method ini, harus update `obfuscar.xml` +
   `obfuscar-singlefile.xml` (skip rule pakai wildcard `name="*"` jadi
   relatif tahan banting). Sudah di-document di header config files.

5. **BCrypt.Net-Next dependency.** Library aktif maintained, no known CVE
   per Mei 2026. Worst case: butuh ganti ke `Microsoft.AspNetCore.Identity`
   `PasswordHasher<TUser>` (juga BCrypt-like / PBKDF2). API kita
   (`Hash`/`Verify` + prefix) cukup abstract sehingga vendor swap trivial.

6. **DB migration.** **Tidak ada DB schema migration** — prefix-based
   discrimination tidak butuh `ALTER TABLE`. Customer upgrade dari v1.2.3
   ke v1.2.4+: tidak ada `MustChangePassword` flag, tidak ada kolom baru.
   Existing user database tetap valid.

### Yang BELUM dikerjakan (out of scope untuk item #4)

- **Force change default admin password on first login.** Audit §10 / T10
  menyebut "default credentials `admin/admin` tidak di-force-change". Item
  ini akan menambah kolom `MustChangePassword INTEGER DEFAULT 0` dan
  intercept login flow → dialog reset password. Ditunda ke item terpisah
  karena butuh ER change dan UI baru.
- **Password complexity policy.** Tidak ada validasi "minimal 8 char,
  campur huruf besar/kecil/angka". UserEditDialog dan PasswordResetDialog
  menerima string apa saja. Bisa ditambah di `PasswordHasher` sebagai
  `static (bool ok, string err) ValidateStrength(string)` — out of scope.
- **Rate limiting login attempt.** Tidak ada lockout setelah N percobaan
  gagal. Brute force dari aplikasi WinForms minim risiko (perlu akses
  fisik), tapi kalau DB ter-expose, attacker offline bisa terus brute
  force terhadap hash-nya. BCrypt cost 12 sudah jadi rate limit alami
  (~4 attempts/sec/core), tapi explicit account lockout idealnya
  dipertimbangkan.
- **Pepper untuk BCrypt.** BCrypt sendiri tidak support pepper native;
  bisa pre-hash password dengan HMAC-SHA256(pepper, password) sebelum
  BCrypt. Saat ini tidak dilakukan untuk simplicity + risiko: pepper
  hilang = semua password tidak bisa di-verify. Audit tidak request.

### Ringkasan bahasa awam Indonesia (untuk user / customer)

**Apa yang berubah dari sisi user?**

- **Login tetap sama:** username + password yang lama, **tetap berfungsi**.
  Tidak perlu reset password, tidak perlu ingat password baru.
- **Login pertama setelah update sedikit lebih lambat (~250 ms tambahan).**
  Itu **normal** — aplikasi sedang meng-upgrade penyimpanan password Anda
  ke standar yang lebih aman (BCrypt). Hanya terjadi sekali per user.
- **Login-login berikutnya tetap normal (~250 ms total per attempt).**
- **Password tidak pernah terlihat oleh aplikasi maupun tersimpan plain
  di mana pun.** Yang disimpan di database adalah "sidik jari" matematika
  password (hash BCrypt), yang **tidak bisa dibalik** menjadi password
  asli walaupun database dicuri.

**Apa keuntungannya?**

- Sebelumnya, jika database `.db` jatuh ke tangan kompetitor (sebelum
  item #1 = trivial dengan copy file; setelah item #1 = jauh lebih sulit
  karena terenkripsi), kompetitor bisa pakai "rainbow table" — tabel
  pre-computed berisi pasangan password populer → hash SHA-256 — untuk
  **tebak password lemah dalam hitungan detik** ("admin" → langsung
  ketahuan).
- Sekarang dengan BCrypt: tebakan password butuh sekitar **250 ms per
  attempt per core CPU attacker**. Brute force "admin" yang dulu instan
  sekarang masih cepat (1 detik), tapi password yang sedikit lebih panjang
  ("admin2026!") jadi tidak praktis untuk di-crack — butuh jutaan tahun.

**Apa yang TIDAK berubah?**

- Cara login, tampilan login, dialog ubah password — **identik**.
- Format file database — **identik** (tidak perlu migrasi terpisah).
- User yang sudah ada — **tidak perlu apa-apa**. Saat login berikutnya,
  password akan otomatis di-upgrade ke format baru tanpa user sadar.
- Backup / restore database — **tetap kompatibel**.

### Pertanyaan untuk reviewer

1. Work factor BCrypt = 12. Mau dinaikkan ke 13 (~500 ms) atau 14 (~1s)?
   Trade-off: lebih aman vs login terasa lambat. Standar 2024+ untuk
   aplikasi internal: 12. Untuk public-facing (banking, ecommerce): 13-14.
2. `LegacySha256` di-expose `internal` + InternalsVisibleTo ke Tests
   project — OK? Alternatif: hapus `internal` dan test pakai `BCrypt.Verify`
   round-trip saja (tapi kehilangan known-vector regression test untuk
   format hash legacy yang sudah ada di customer DB).
3. Sekarang `LoginForm.DoLogin` lookup user pakai `FirstOrDefault(u =>
   username && IsActive)` lalu verify in-process. Sebelumnya filter
   `PasswordHash == hash` jalan di SQL → SQL engine optimize. Untuk DB
   ~10 user (use case kita), perbedaannya nol. Kalau suatu hari user count
   naik signifikan, perlu re-evaluasi.
4. Apakah perlu sekalian implement `MustChangePassword` untuk default admin
   di item ini, atau biarkan ke item terpisah? (Saya tulis ke "out of
   scope" — tapi kalau mau jadi satu, ~30 menit kerja tambahan.)
