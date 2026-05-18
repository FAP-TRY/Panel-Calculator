# CLAUDE.md — Panel Calculator (Kalkulator Panel Tritunggal Swarna)

Dokumen ini berisi konteks penting untuk Claude AI saat bekerja di proyek ini.

---

## Ringkasan Proyek

Aplikasi desktop Windows untuk kalkulasi harga panel listrik PT Tritunggal Swarna.
Menggantikan proses manual menggunakan pricelist PDF.

- **Teknologi:** C# .NET 8, WinForms, SQLite (EF Core), iText7
- **Arsitektur:** Layered — Core / Data / WinForms
- **Database:** `%AppData%\PanelCalculator\PanelCalculator.db` (per user, tidak di repo)
- **Versi saat ini:** v1.2.5

---

## Struktur Solusi

```
PanelCalculator.sln
├── PanelCalculator.Core/          — Models, Interfaces, Services
├── PanelCalculator.Data/          — EF Core DbContext, Repositories, Seeder
├── PanelCalculator.WinForms/      — UI Forms, Theme, Services
│   ├── Forms/
│   │   ├── ShellForm.cs           — App shell (top bar, logo, auto-update)
│   │   ├── LoginForm.cs           — Login screen dengan logo embedded
│   │   ├── MainForm.cs            — Kalkulator utama (katalog + estimasi)
│   │   ├── EstimationHistoryForm.cs — Riwayat + Export PDF/CSV
│   │   ├── SettingsForm.cs        — Import produk, manajemen user, cek update
│   │   ├── ReportsForm.cs         — Laporan & analitik
│   │   ├── DashboardForm.cs       — Dashboard
│   │   └── ProductEditDialog.cs   — Edit produk manual
│   ├── Services/
│   │   ├── UpdateService.cs       — Auto-update via GitHub Releases
│   │   └── PdfLetterExport.cs     — Export PDF surat penawaran formal (iText7)
│   ├── Theme/AppTheme.cs          — Warna, font, style komponen
│   └── Assets/logo.png            — Logo TTS (embedded resource)
├── Tools/                         — Script Python parser PDF pricelist
│   ├── schneider_pdf_parser.py
│   ├── himel_pdf_parser.py
│   ├── extract_pdf.py             — HOWIG NH Fuse, LBS, Isolator, Meter
│   └── extract_ct_catalog.py     — HOWIG CT, Metering, PB, Fuse
├── Installer/                     — File installer Inno Setup (tidak di-commit)
└── PanelCalculator.iss            — Script Inno Setup
```

---

## Database

- **Path:** `C:\Users\<user>\AppData\Roaming\PanelCalculator\PanelCalculator.db`
- **TIDAK dihapus saat uninstall** (by design)
- Tabel utama: `Products`, `Estimations`, `EstimationDetails`, `Users`, `Settings`

### Jumlah Produk (per Mei 2026)

| Vendor | Jumlah |
|--------|--------|
| Schneider Electric | ~6.400 |
| Himel | ~1.047 |
| HOWIG | ~373 |
| **Total** | **~8.021** |

### Import Produk Baru

Gunakan script Python di folder `Tools/`:
```bash
python Tools/himel_pdf_parser.py
python Tools/extract_pdf.py           # HOWIG NH Fuse/LBS/Meter
python Tools/extract_ct_catalog.py    # HOWIG CT/Metering/PB/Fuse
```

Lalu import CSV via Settings → Import CSV/Excel, atau langsung ke SQLite:
```python
import sqlite3, csv
# upsert ke Products table
# kolom: Category, ReferenceCode, ProductName, Specifications,
#        Price, StockStatus, Vendor, PriceYear, LastUpdated
```

---

## Auto-Update (GitHub Releases)

- **Repo:** `https://github.com/FAP-TRY/Panel-Calculator` (PUBLIC)
- **Asset name di setiap release:** `PanelCalculator.exe` (single-file EXE, ~179MB)
- **Flow:** app cek `releases/latest` → bandingkan versi → tampilkan tombol update
- **Mekanisme update:** PowerShell script dengan `-Verb runas` (UAC elevation) untuk
  replace EXE di Program Files
- **Bug yang sudah diperbaiki:**
  - v1.2.0: Accept header binary download + PowerShell elevation
  - v1.2.1: Double-save UNIQUE constraint + retry loop
  - v1.2.2: `Application.DoEvents()` menyebabkan autoclose saat download
  - v1.2.3: DPI scaling — semua form pakai `AutoScaleMode.Dpi` + `PerMonitorV2`
  - v1.2.4: Koreksi harga katalog Himel & FORT ke harga list asli (tanpa diskon)
  - v1.2.5: Security hardening (DB encrypt, update SHA-256 verify, Obfuscar, BCrypt, license binding) + PDF/CSV format polish

### Cara Rilis Versi Baru

```bash
# 1. Bump versi di dua tempat:
#    - PanelCalculator.WinForms/Services/UpdateService.cs → AppVersion
#    - PanelCalculator.iss → AppVersion + OutputBaseFilename

# 2. Build single-file EXE (obfuscated) + SHA-256 manifest
#    Script ini menjalankan: dotnet publish (multi-file) -> Obfuscar (3 DLL aplikasi)
#    -> overlay obfuscated DLL ke bin -> dotnet publish (single-file, --no-build)
#    -> copy ke publish\ -> generate .sha256 manifest.
#    Auto-install Obfuscar.GlobalTool kalau belum ada. Total waktu ~2-3 menit.
powershell -ExecutionPolicy Bypass -File ".\Tools\build-release-singlefile.ps1"
# Output:
#   publish\PanelCalculator.exe         (~179 MB, obfuscated)
#   publish\PanelCalculator.exe.sha256  (manifest untuk auto-updater)

# 3. Compile installer (PowerShell)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "PanelCalculator.iss"

# 4. Commit, tag, push, buat release
#    Pastikan file .sha256 ikut di-upload sebagai asset ketiga.
git add . && git commit -m "Bump version to vX.Y.Z"
git push origin master
git tag vX.Y.Z && git push origin vX.Y.Z
gh release create vX.Y.Z "./publish/PanelCalculator.exe" \
  "./publish/PanelCalculator.exe.sha256" \
  "./Installer/KalkulatorPanel-TTS-vX.Y.Z-Setup.exe" \
  --title "vX.Y.Z - Deskripsi"
```

> **PENTING — single-file EXE auto-update ter-obfuscate (sejak v1.2.4)**
> Step #2 di atas WAJIB pakai script `build-release-singlefile.ps1`, bukan
> `dotnet publish` polos. Script ini menjalankan Obfuscar pada
> `PanelCalculator.{WinForms,Core,Data}.dll` SEBELUM single-file bundler
> memasukkannya ke EXE. Tanpa script ini, IL 3 DLL aplikasi akan telanjang
> di dalam EXE 179 MB → bisa di-decompile satu klik dengan ILSpy/dnSpy
> (membocorkan SQLCipher key derivation, update endpoint, pricing logic).
> Untuk override (mis. debug build), pakai flag `-SkipObfuscation` — tapi
> JANGAN release ke customer dengan flag itu aktif.

> **PENTING — verifikasi keamanan update (sejak v1.2.4)**
> Asset `PanelCalculator.exe.sha256` WAJIB diupload bersama setiap release.
> Client baru (v1.2.4+) akan menolak install update bila manifest tidak ada
> atau hash tidak cocok. Lihat `PanelCalculator.Data/Security/UpdateVerifier.cs`
> dan `docs/security-implementation-log.md` (Item #2) untuk detail.

### Build Installer (Inno Setup multi-file) — `Installer\build.bat`

Alternatif dari step #3 di atas: jalankan `Installer\build.bat` untuk paket
multi-file (folder `app-final\` di-bundle ke installer Inno Setup).
Pipeline-nya sekarang **otomatis menjalankan Obfuscar** terhadap 3 DLL
aplikasi:

1. `dotnet publish` ke `Installer\app-raw\` (self-contained, multi-file)
2. Auto-install `Obfuscar.GlobalTool` kalau belum ada (`dotnet tool install`)
3. Obfuscar baca `Installer\obfuscar.xml` → output `Installer\app-obf\`
4. Overlay 3 DLL obfuscated ke `Installer\app-final\` (runtime DLL .NET tetap utuh)
5. Compile installer dengan Inno Setup

Tambahan waktu build: ~30 detik (sekali jalan Obfuscar untuk ~10 MB IL).
Konfigurasi Obfuscar safe untuk WinForms + EF Core: method/field di-rename,
string literal di-encrypt (`HideStrings=true`), tapi nama type & property
TIDAK di-rename (supaya EF mapping dan DI tetap jalan). Lihat
`docs/security-implementation-log.md` (Item #3) untuk detail design.

---

## Installer

- **Tool:** Inno Setup 6 — `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
- **Script:** `PanelCalculator.iss`
- **Password install:** `TTS2025`
- **Install dir:** `{autopf}\TritunggalSwarna\KalkulatorPanel`
- **Output:** `Installer\KalkulatorPanel-TTS-vX.Y.Z-Setup.exe`
- File installer TIDAK di-commit ke git (ukuran besar, binary)

---

## Login Default

| Username | Password | Role |
|----------|----------|------|
| admin | admin | Admin |

---

## Fitur Utama

- **Katalog Produk** — filter kategori/merk, search, klik 2x untuk tambah ke estimasi
- **Kalkulator Estimasi** — multi-item, margin bertingkat (3 tier), PPN/PPh, ongkir
- **Simpan Estimasi** — nomor otomatis `EST-YYYYMMDD-###` dengan retry jika duplikat
- **Riwayat** — load ulang estimasi lama, filter, export PDF/CSV
- **Export PDF** — 2 format: Surat Formal (iText7) dan Modern
- **Export CSV** — di form Riwayat, di samping tombol Export PDF
- **Auto-Update** — via GitHub Releases, tombol muncul di top bar
- **Import Produk** — CSV/Excel via Settings, atau sync ulang dari file terakhir
- **Manajemen User** — tambah/nonaktifkan/reset password user

---

## Hal Penting / Jangan Dilakukan

- **Database TIDAK dihapus saat uninstall** — by design, data tetap aman
- **Installer file besar (~53MB)** — jangan di-commit ke git
- **CSV output di Tools/ ada di .gitignore** — sudah benar, tidak perlu di-commit
- **Kolom `Price` di DB bertipe TEXT** — bukan decimal, sudah by design (EF Core mapping)
- **Logo di Assets/logo.png** — embedded sebagai `EmbeddedResource` di .csproj
- **Jangan gunakan `Application.DoEvents()`** di async handler — menyebabkan autoclose bug

---

## Pending / Belum Diimplementasi

- **WhatsApp Approval** — fitur kirim approval ke WA pimpinan (user bilang "pending")
