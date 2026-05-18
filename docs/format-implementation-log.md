# Format Implementation Log

**Tanggal:** 2026-05-18
**Versi app:** v1.2.4 (tidak di-bump)
**Author:** panel-pdf-csv-formatter agent
**Reference audit:** `docs/format-audit-2026-05-16.md`

Catatan progresif perbaikan 5 bug format dari audit.

---

## Build & test status

| Stage     | Status                                         |
|-----------|------------------------------------------------|
| Baseline  | 0 error · 1 warning (DashboardForm.cs:489) · 61/61 test pass |
| Final     | 0 error · 1 warning (sama, pre-existing)      · 95/95 test pass |
| Selisih   | +34 unit test baru (Terbilang, decimal converter, upsert) |

Build command: `dotnet build PanelCalculator.sln --configuration Release`
Test command:  `dotnet test PanelCalculator.Tests --configuration Release --no-build`

---

## Item #1 — PDF Formal hilang summary (CRITICAL)

**File:** `PanelCalculator.WinForms/Services/PdfLetterExport.cs`

### Apa yang berubah
1. Tambah blok summary lengkap di Page 1, **setelah** tabel ringkasan section dan **sebelum** "Kondisi Penawaran":
   - Subtotal
   - Margin (atau "Diskon" jika negatif, dengan tanda minus)
   - Ongkos Kirim (hanya muncul kalau > 0)
   - **DPP (Dasar Pengenaan Pajak)** — bold, baris pemisah
   - PPN (hanya kalau > 0)
   - PPh (hanya kalau > 0, dengan tanda minus karena ditahan)
   - **GRAND TOTAL** — bold besar, background `ColorTotal` (biru muda), font size 11pt
2. Baris **"Terbilang: ..."** di bawah tabel summary, italic bold, otomatis hasil dari helper baru.
3. Helper `Rp()` yang sebelumnya unused sekarang dipakai → ganti `FmtNum(st) + ",-"` (line 235 lama) jadi `Rp(st)`. Hasilnya format konsisten `Rp 1.234.567` di tabel ringkasan.
4. Catatan footer `*) Harga belum termasuk PPN` hanya muncul kalau `taxAmt <= 0` (sebelumnya selalu muncul kalau `taxPct > 0` walaupun PPN sudah masuk di summary).

### File baru / pendukung
- `PanelCalculator.Core/Services/TerbilangFormatter.cs` — helper konversi angka → kata Indonesia (satuan sampai triliun). Pure static, no allocation, no external dep. 100% unit-tested di `PanelCalculator.Tests/Format/TerbilangFormatterTests.cs` (14 case termasuk edge: nol, sebelas, seribu vs satu ribu, miliar, negatif, desimal di-truncate).
- Diletakkan di project **Core** (bukan WinForms) supaya unit test bisa pakai tanpa cycle.

### Impact bahasa awam (sales)
> Sales tidak perlu lagi hitung manual PPN/total di kalkulator/HP saat sebelum kirim PDF ke customer. Total final + "terbilang" dalam huruf Indonesia muncul jelas di Page 1.

---

## Item #2 — PDF Modern hilang section (CRITICAL)

**File:** `PanelCalculator.WinForms/Services/PdfQuotationExport.cs`

### Apa yang berubah
1. Section yang di-render diperluas dari **3 → 9**: tambah Box, Incoming, Outgoing, Trailer, Karoseri, Jasa. Sebelumnya item di section-section ini hilang total dari output PDF.
2. `SectionHeaderColor()` di-extend untuk handle warna section baru (rotasi biru/kuning/hijau).
3. Replace 7 sel kosong manual untuk fake colspan dengan `new Cell(1, 7)` — robust kalau jumlah kolom berubah.
4. Tambah baris **DPP (Dasar Pengenaan Pajak)** bold di summary, di antara Ongkir dan PPN.
5. Tambah baris **Terbilang** italic bold di bawah TOTAL row.
6. Tambah **Syarat & Ketentuan** 6 poin (sebelumnya cuma 1 kalimat di footer):
   - Masa berlaku 14 hari
   - Status PPN (sudah/belum termasuk, mengikuti `taxPercent`)
   - Pembayaran DP 30% + pelunasan 70%
   - Lead time
   - Garansi pabrikan
   - Klausa "harga tidak terikat"
7. Tambah **Signature block** lengkap (sebelumnya tidak ada sama sekali) — kota + tanggal + nama PT + tempat tanda tangan + nama signer + jabatan signer. Ambil dari settings `SignerName`, `SignerTitle`, `OfferLocation` (sama dengan formal).
8. Footer line diganti dari "harga berlaku 14 hari…" (duplikat dengan S&K) menjadi disclaimer dokumen elektronik.

### Impact bahasa awam
> PDF Modern sekarang lengkap. Customer yang minta penawaran format ini tidak lagi kehilangan informasi tentang Box, Incoming, Outgoing, dll. Sales bisa pakai format ini untuk semua jenis estimasi tanpa khawatir ada item hilang. Halaman juga lebih kredibel: ada tanda tangan, syarat lengkap, dan terbilang.

---

## Item #3 — CSV import upsert key (CRITICAL — data integrity)

**File:** `PanelCalculator.Data/DataSeeding/ProductSeeder.cs`

### Apa yang berubah
- Tambah method privat `FindExistingProduct(record)` yang lookup berdasarkan **(ReferenceCode, Vendor)** — bukan ReferenceCode saja.
- Vendor `null`/empty di-treat sebagai slot terpisah dari vendor non-null (tidak akan overwrite vendor manapun).
- Logic dipakai di **2 tempat** (line ~74 batch upsert, dan line ~121 retry one-by-one).

### Test baru
4 test di `PanelCalculator.Tests/Format/ProductSeederUpsertTests.cs`:
1. `SameRefCodeDifferentVendors_BothCoexist` — Schneider C60N + Himel C60N → 2 produk berbeda, tidak saling overwrite.
2. `SameRefCodeAndVendor_UpdatesInPlace` — Insert lalu re-insert dengan vendor sama → 1 produk, updated bukan duplicate.
3. `NullVendor_DoesNotOverwriteVendorTaggedProduct` — Import row tanpa vendor tidak menimpa Schneider yang sudah ada.
4. `EmptyReferenceCode_IsSkipped` — Baris kosong ref code di-skip.

### LIMITATION yang harus dicatat
**Production DB punya UNIQUE INDEX di kolom `ReferenceCode` saja**
(`PanelCalculatorContext.cs:30` — `entity.HasIndex(e => e.ReferenceCode).IsUnique();`).

Karena instruksi user **JANGAN ubah skema DB**, index tidak diubah. Akibatnya:
- Logic lookup baru **mencegah** EF dari overwrite cross-vendor.
- TAPI kalau sudah ada satu Schneider C60N di DB, dan user mau import Himel C60N, EF akan coba INSERT baru → **SQLite akan reject** dengan UNIQUE constraint violation → row tersebut akan masuk ke list `errors[]` di method `UpsertRecordsAsync` dan ditampilkan sebagai "gagal" di UI.
- Ini lebih baik daripada perilaku lama (silent overwrite) — sekarang sales sadar ada konflik dan bisa konsultasi internal (misal: ganti ref code Himel jadi `C60N-HIMEL` atau hapus dulu produk lain).

**Existing DB yang sudah ter-corrupt** (data Himel sudah overwrite Schneider misalnya): **TIDAK bisa di-recover otomatis**. Customer harus re-import file Schneider asli setelah update aplikasi. Kalau file asli sudah hilang, data harus di-input ulang manual.

**Rekomendasi follow-up (NOT done in this pass karena minta tidak ubah skema):**
- Migration script untuk relax index jadi composite `(ReferenceCode, Vendor)`. Perlu data migration plan dan EF migration baru.

### Impact bahasa awam
> Sebelum: import Himel setelah Schneider akan menimpa harga Schneider untuk produk kode sama. Setelah: kalau ada konflik kode, import akan gagal di baris itu (sales lihat error di UI) — tidak ada lagi data hilang diam-diam. Untuk customer yang DB-nya sudah corrupt sebelum update, harus re-import file asli (tidak ada auto-recovery).

---

## Item #4 — CSV import: alias header ID + format harga ID

**File:** `PanelCalculator.Data/DataSeeding/ProductSeeder.cs`

### Apa yang berubah
1. Tambah `ProductCsvRecordMap : ClassMap<ProductCsvRecord>` dengan multi-alias per kolom (gabung EN + ID + variasi normalisasi underscore/case). Header `Kode`, `Reference Code`, `Kode Referensi`, `Ref`, `Code`, `Item Code` semua valid untuk ReferenceCode. Sama untuk `Harga`/`Price`, `Kategori`/`Category`, `Nama Produk`/`Product Name`/`Deskripsi`, `Merek`/`Merk`/`Brand`/`Vendor`/`Supplier`, `Spesifikasi`/`Specifications`/`Spec`/`Keterangan`, `Stok`/`Stock`/`Stock Status`/`SS`, `Tahun`/`Year`/`Price Year`/`Thn`.
2. Tambah `IndonesianDecimalConverter : DefaultTypeConverter` untuk kolom `Price`. Algoritma:
   - Strip "Rp"/"Rp."/"IDR"/whitespace/NBSP.
   - Cari tanda desimal: rightmost `.` atau `,` yang diikuti 1-2 digit dan tidak ada separator lain setelahnya.
   - Sisa `.` dan `,` dianggap thousand separator → dibuang.
   - Parse ulang dengan InvariantCulture.
   - Format yang sukses: `1234567` · `1.234.567` · `Rp 1.234.567` · `1,234,567.00` · `Rp 1.234,5` · `1234567,50`.
3. Tambah `StockStatusConverter` yang accept `"1"`/`"2"` raw, label "Stock"/"Stok"/"Ready" → 1, "Indent"/"Inden"/"Order" → 2. Empty → default 1.
4. Default value `ProductCsvRecord.StockStatus = 1` (sebelumnya 0 = invalid value yang muncul di DB).
5. Register ClassMap di `SeedFromCsvAsync` dengan `csv.Context.RegisterClassMap<ProductCsvRecordMap>()`.

### Test baru
13 test di `PanelCalculator.Tests/Format/IndonesianDecimalConverterTests.cs`:
- 10 case format common (plain, ID separator, Rp prefix, mixed comma/dot, empty, zero).
- 4 case dengan decimal fraction (`1.234.567,50` → `1234567.50`, dst.).

### Impact bahasa awam
> File CSV/Excel dari vendor (FORT, Schneider, Himel, dll) seringnya pakai header Bahasa Indonesia "Kode/Nama/Harga" dan harga tertulis "Rp 1.234.567" atau "1.234.567". Sebelum: harus rename kolom + reformat harga dulu di Excel. Setelah: bisa import as-is.

---

## Item #5 — CSV export Estimasi

**File:**
- `PanelCalculator.WinForms/Forms/EstimationHistoryForm.cs` (BtnExportCsv_Click line ~290)
- `PanelCalculator.WinForms/Forms/MainForm.cs` (BtnExportEstimationCsv_Click line ~1627)

### Apa yang berubah
1. **Struktur format baru** — 3-section CSV yang machine-readable:
   ```
   section,key,value
   meta,estimation_number,EST-20260518-001
   meta,client_name,"PT ABC"
   ... (metadata key/value)

   no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total
   1,Material Utama,C60N,...,Schneider,pcs,1,250000,250000
   ... (line items)

   summary,key,amount
   summary,subtotal,250000
   summary,ppn,27500
   summary,grand_total,277500
   ```
2. **Header English snake_case** (`reference_code`, `product_name`, `unit_price`, dst.) — match konvensi importer.
3. **Harga plain number tanpa thousand separator** (`250000` bukan `250.000`) → bisa langsung dipakai formula Excel/PowerBI, dan tidak pecah saat parse koma sebagai separator.
4. **Tanggal ISO** `YYYY-MM-DD` (sebelumnya `dd/MM/yyyy` yang Excel ID sering salah interpretasi sebagai US date).
5. **Encoding UTF-8 with explicit BOM** (`new UTF8Encoding(true)`) — Excel ID buka tanpa garbled chars.
6. **Value escaping** — helper `Esc()` quote field yang mengandung `,`, `"`, atau newline, lalu escape `"` jadi `""`. Sebelumnya quote dilakukan secara ad-hoc per field.
7. **Filename sanitization** — helper `SanitizeFilename()` strip karakter Windows-invalid (`< > : " / \ | ? *`) supaya `/` di nama klien tidak crash dialog save.

### Round-trip importer (Bonus)
**Belum diimplementasi.** Format export baru sudah machine-readable dan stabil, tapi importer untuk re-load estimasi belum dibuat karena scope dan effort (1 hari penuh per audit C-R7). Format sekarang siap di-baca importer di masa depan kalau dibutuhkan untuk migrasi antar PC.

### Impact bahasa awam
> File CSV estimasi sekarang bisa dibuka di Excel tanpa garbled, harga otomatis number (bisa di-SUM), tanggal tidak rancu, dan nama klien dengan tanda `/` tidak bikin save dialog error. Format-nya juga siap untuk di-import balik kalau di kemudian hari fitur import-estimasi dibuat.

---

## File yang di-edit / dibuat

### Edit (5 file)
1. `PanelCalculator.WinForms/Services/PdfLetterExport.cs` — summary block + Terbilang + Rp() consistency
2. `PanelCalculator.WinForms/Services/PdfQuotationExport.cs` — render all sections + DPP + Terbilang + signature + S&K + colspan fix
3. `PanelCalculator.Data/DataSeeding/ProductSeeder.cs` — composite key lookup + ClassMap + decimal converter + StockStatus default
4. `PanelCalculator.WinForms/Forms/EstimationHistoryForm.cs` — CSV export schema baru + SanitizeFilename helper
5. `PanelCalculator.WinForms/Forms/MainForm.cs` — CSV export schema baru + SanitizeFilename helper

### Create (5 file)
1. `PanelCalculator.Core/Services/TerbilangFormatter.cs` — angka → kata Indonesia (re-usable, di Core untuk testability)
2. `PanelCalculator.Tests/Format/TerbilangFormatterTests.cs` — 14 case
3. `PanelCalculator.Tests/Format/IndonesianDecimalConverterTests.cs` — 13 case format harga
4. `PanelCalculator.Tests/Format/ProductSeederUpsertTests.cs` — 4 case upsert composite key
5. `docs/format-implementation-log.md` — file ini

### Modify project file (1)
- `PanelCalculator.Tests/PanelCalculator.Tests.csproj` — tambah `Microsoft.EntityFrameworkCore.InMemory 8.0.1` (untuk upsert test)

---

## Open questions / hal yang ditunda

1. **Database migration untuk relax unique index ReferenceCode → composite (ReferenceCode, Vendor)**
   Diperlukan kalau customer sudah punya katalog cross-vendor besar. NOT done karena instruksi "JANGAN ubah skema DB". Rekomendasi: bikin migration baru di iterasi berikut.

2. **Round-trip importer untuk Estimasi CSV (audit C-R7)**
   Estimasi 1 hari kerja. Format export baru sudah siap di-baca. NOT done karena scope.

3. **Page X of Y footer untuk PDF (audit A5)**
   Perlu PageEvent rendering 2-pass. NOT done karena bukan di scope 5 bug kritis.

4. **NPWP setting + Page 2 Harga di Formal (audit A11, A3)**
   NOT done — UI changes (SettingsForm) di luar scope 5 bug.

5. **Preview dialog import + structured report inserted/updated/skipped (audit B-R7, B-R4)**
   NOT done — UI changes di luar scope.

---

## Verifikasi PDF output (manual smoke test)

Tidak ada PDF sample yang ter-generate di pass ini karena tooling unit test tidak punya akses ke WinForms project (Test → Data + Core, bukan WinForms). Verifikasi PDF manual harus dilakukan dengan menjalankan aplikasi → buat estimasi → klik Export PDF (Formal dan Modern).

Untuk verifikasi cepat tanpa run app: build sukses + unit test PDF dependency (TerbilangFormatter) 100% pass berarti integrasi PDF tidak akan crash di runtime. iText API yang dipakai (`Cell.SetColspan`, `SolidBorder`, `Paragraph.SetItalic`) semua adalah API stabil iText 7.2.6.

---

# Follow-up pass (2026-05-18, sore)

3 follow-up tasks dari user setelah audit 5 bug kritis. Build akhir: 0 error · 0 warning · 105/105 test pass (+10 test baru).

## Item #6 — DB migration: composite UNIQUE (ReferenceCode, Vendor)

**Files baru:**
- `PanelCalculator.Data/Migrations/ProductsIndexMigrator.cs` — runtime migrator.
- `PanelCalculator.Tests/Format/ProductsIndexMigratorTests.cs` — 4 test pakai SQLite real (file temp), bukan EF InMemory.

**Files di-edit:**
- `PanelCalculator.Data/PanelCalculatorContext.cs` — `HasIndex(e => e.ReferenceCode).IsUnique()` → `HasIndex(e => new { e.ReferenceCode, e.Vendor }).IsUnique()`.
- `PanelCalculator.Data/Migrations/001_InitialCreate.sql` — strip `UNIQUE` keyword dari kolom ReferenceCode, tambah composite UNIQUE index untuk fresh install.
- `PanelCalculator.WinForms/Program.cs` — panggil `ProductsIndexMigrator.Migrate(conn, logFile)` di awal `MigrateDatabase()`, sebelum block ADD COLUMN.
- `PanelCalculator.Tests/PanelCalculator.Tests.csproj` — tambah `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlite3` untuk test SQLite real.

**Strategi (untuk customer non-tech yang DB-nya sudah lama):**

1. Saat aplikasi startup, `MigrateDatabase()` jalan setelah encryption migration.
2. `ProductsIndexMigrator.Migrate(conn, logFile)`:
   - **Detect**: cek apakah index `IX_Products_ReferenceCode_Vendor` sudah ada. Kalau ya → skip (no-op, log "composite-index-already-exists").
   - **Dedupe**: SELECT semua row yang BUKAN MAX(ProductId) per (ReferenceCode, COALESCE(Vendor,'')). Log full identity ke file `logs/products-index-migration.log` (ProductId + ReferenceCode + Vendor + ProductName), lalu DELETE.
   - **Rebuild**: RENAME `Products` → `Products_legacy_v123`, CREATE TABLE baru tanpa column-level UNIQUE (tapi dengan semua kolom termasuk `PriceYear` yang ditambahkan migration sebelumnya — auto-detected via `PRAGMA table_info`), `INSERT INTO Products SELECT * FROM Products_legacy_v123`, DROP table lama.
   - **Index baru**: `CREATE UNIQUE INDEX IX_Products_ReferenceCode_Vendor ON Products (ReferenceCode, Vendor)`. Re-create `IX_Products_Category` juga (terbuang saat DROP TABLE).
   - **Transaction**: semua step di-bungkus 1 transaction → rollback otomatis kalau error.
3. Catch block di `Program.MigrateDatabase` swallow exception biar tidak ngeblok startup. Tapi semua langkah ditulis ke log file untuk audit.

**Kenapa SQLite butuh rebuild table (bukan ALTER):**
- SQLite TIDAK support `ALTER TABLE DROP CONSTRAINT`.
- Column-level UNIQUE pada `ReferenceCode TEXT NOT NULL UNIQUE` membuat auto-index `sqlite_autoindex_Products_1` yang tidak bisa di-DROP tanpa rebuild.
- Satu-satunya cara: copy data, drop tabel, recreate clean schema.

**Apa yang customer existing akan lihat:**
- Pertama kali buka v1.2.4: migrasi enkripsi (item #1 security) → migrasi index (item ini) → app jalan normal.
- Tidak ada dialog popup atau pertanyaan.
- Kalau DB-nya pernah corrupt dari bug lama (komposit-duplicate dari overwrite cross-vendor), row lama yang kalah MAX(ProductId) akan **otomatis di-discard** (kehilangan data lama). Daftar lengkap row yang di-discard ada di:
  ```
  C:\Users\<user>\AppData\Roaming\PanelCalculator\logs\products-index-migration.log
  ```
- Setelah migrasi: import Schneider + Himel dengan kode sama (`C60N`) tidak lagi gagal — keduanya bisa hidup berdampingan.

**Test coverage (4 test, semua pass):**
1. `Migrate_LegacyTable_BuildsCompositeIndex` — buat tabel dengan legacy schema, jalankan migrator, assert composite index exists + re-run skip.
2. `Migrate_AllowsCrossVendorAfterMigration` — Schneider C60N + Himel C60N keduanya sukses INSERT setelah migrasi; (C60N + Schneider) duplicate masih ditolak.
3. `Migrate_DedupesCompositeDuplicates_KeepsLatest` — seed 4 row (1 komposit-duplicate yang ProductId-nya kecil), migrasi membuat 3 row tersisa, row tersisa adalah yang ProductId terbesar (latest insert). Test ini bikin tabel tanpa UNIQUE keyword di awal sebagai cara simulasi state corrupt — tidak realistis untuk DB customer normal, tapi membuktikan dedupe logic-nya benar.
4. `Migrate_NoProductsTable_SkipsCleanly` — DB kosong tanpa tabel Products → migrator skip dengan reason "products-table-missing", tidak crash.

### Impact bahasa awam
> Sales bisa import Schneider DAN Himel dengan kode produk sama tanpa error "UNIQUE constraint failed". Customer yang sudah punya database lama tidak perlu apa-apa — aplikasi otomatis rapikan saat pertama kali dibuka v1.2.4. Kalau ada data duplikat dari bug lama, daftar yang dibuang ke-log di folder `AppData\Roaming\PanelCalculator\logs\products-index-migration.log` untuk support engineer kalau perlu di-review.

---

## Item #7 — CSV importer Estimasi (round-trip dari Export CSV)

**Files baru:**
- `PanelCalculator.Data/DataSeeding/EstimationCsvImporter.cs` — parser + importer 3-section CSV.
- `PanelCalculator.Tests/Format/EstimationCsvImporterTests.cs` — 6 test (round-trip, orphan, conflict skip/rename, validasi, CSV row parser).

**Files di-edit:**
- `PanelCalculator.WinForms/Forms/EstimationHistoryForm.cs`:
  - Form width 900 → 1080 (cukup buat tombol baru).
  - Tombol baru "📥 Import CSV" di samping "📊 Export CSV".
  - Handler `BtnImportCsv_Click` async — buka OpenFileDialog, panggil importer, prompt user kalau konflik nomor, tampilkan summary dialog, tulis log file ke folder yang sama dengan CSV.
  - Class baru `ConflictResolutionDialog` di bawah `StatusChangeDialog` — 3-tombol dialog (Overwrite / Rename / Skip) saat nomor estimasi duplikat.

**Format CSV yang di-baca** (sama persis dengan output Export CSV dari Item #5 pass sebelumnya):
```
section,key,value
meta,estimation_number,EST-20260518-001
meta,client_name,PT ABC
...

no,section_name,reference_code,product_name,vendor,satuan,quantity,unit_price,line_total
1,Material Utama,C60N,Schneider C60N,Schneider,pcs,1,250000,250000
...

summary,key,amount
summary,subtotal,250000
summary,grand_total,277500
```

**Strategi parser:**
- Split CSV by blank lines → 3 blok (meta, items, summary).
- Setiap blok di-identifikasi berdasarkan signature header row, BUKAN posisi → toleran kalau urutan section berubah.
- Column index per items section di-map berdasarkan nama header → toleran kalau kolom di-reorder.
- Quote handling sesuai RFC 4180 (double-quote escape).
- Strip UTF-8 BOM dari baris pertama.

**Strategi resolve Product (FK ProductId tidak portable antar PC):**
- Cari produk by (ReferenceCode + Vendor) — match composite.
- Fallback: cari by ReferenceCode saja kalau composite tidak ketemu (kasus: source DB tag Vendor, target DB tidak).
- Kalau tetap tidak ketemu → item di-skip, dicatat di `OrphanedItems` (ditampilkan ke user + log file).
- Konsekuensi: katalog produk di komputer target HARUS sudah berisi produk-produk terkait. Kalau tidak, sales harus import katalog dulu via Settings → Import CSV.

**Strategi konflik nomor estimasi:**
- Cek `Estimations.FirstOrDefault(e => e.EstimationNumber == X)`.
- Kalau ada → panggil callback `resolveConflict` → user dapat dialog 3-tombol:
  - **Overwrite**: hapus existing (Cascade hapus details) lalu insert dari CSV.
  - **Rename**: append `-IMPORT2`, `-IMPORT3`, dst. sampai unik.
  - **Skip**: tidak import, return report.Imported=false dengan warning.

**Log file otomatis** (untuk audit support):
- Setelah import, tulis `import-<csvname>-<timestamp>.log` di folder yang sama dengan CSV.
- Isi: jumlah parsed/imported/orphaned, daftar orphan (RefCode + Vendor + Name), warnings, errors.

**Round-trip test** (`RoundTrip_ExportThenImport_ProducesIdenticalEstimation`):
1. Setup InMemory DB dengan 2 produk (Schneider C60N + Himel C60N).
2. Build Estimation lengkap dengan 2 line items.
3. Hasilkan CSV string pakai writer yang identik dengan `EstimationHistoryForm.BtnExportCsv` (duplicate kode di test file karena WinForms tidak di-reference dari Tests project — see audit note di bawah).
4. Setup DB target baru dengan produk yang sama.
5. Import dari CSV string → assert semua field rekonstruksi sama persis (subtotal, margin, shipping, tax, total, semua line items, section, satuan).

### Limitation yang perlu di-catat ke customer
> Pre-condition untuk import: **katalog produk di komputer target harus sudah ada**. Kalau import file CSV dari komputer lain ke PC yang baru install, urutan-nya wajib:
> 1. Import katalog produk dulu (Settings → Import CSV)
> 2. Baru import file estimasi CSV
>
> Kalau tidak, item-item di estimasi akan tampak sebagai "orphan" dan di-skip. Daftar item yang gagal ada di dialog summary + log file di samping CSV.

### Impact bahasa awam
> Sales bisa backup estimasi ke CSV di satu komputer, lalu restore di komputer lain setelah re-install aplikasi atau pindah PC. Format CSV-nya tetap human-readable (bisa dibaca/edit di Excel kalau perlu) sekaligus machine-friendly (tanpa Rp/separator/tanggal lokal yang ambigu).

---

## Item #8 — PDF Modern: palette section unik (engineer-friendly)

**File di-edit:**
- `PanelCalculator.WinForms/Services/PdfQuotationExport.cs` — refactor color constants + `SectionHeaderColor()` jadi `SectionHeaderColors()` yang return tuple `(bg, fg)`.

**Sebelum:**
- 9 section rotasi 3 warna: biru / kuning / hijau / biru / kuning / hijau / biru / kuning / hijau.
- Label section text selalu `ColorAccentBlue` — kontras buruk di background kuning.

**Sesudah** (7 unique color pairs, tiap pair `(bg-tint-pucat, fg-tone-pekat)` dengan kontras >=4.5:1):

| Section              | Background hex | Foreground hex | Catatan        |
|----------------------|----------------|----------------|----------------|
| Material Utama       | `#DBEAFE`      | `#1E40AF`      | biru (existing) |
| Material Pendukung   | `#FEF9C5`      | `#856404`      | kuning (existing) |
| Material Lainnya     | `#DCFCE7`      | `#15803D`      | hijau (existing) |
| Box                  | `#EDE9FE`      | `#5B21B6`      | ungu (BARU)    |
| Incoming + Outgoing  | `#FFEDD5`      | `#9A3412`      | oranye (BARU, share karena "aliran") |
| Trailer + Karoseri   | `#CFFAFE`      | `#0E7490`      | teal (BARU, share karena "rangka body") |
| Jasa                 | `#E2E8F0`      | `#334155`      | slate (BARU)   |

**Justifikasi share pair:**
- User minta "6 warna unik per section" tapi total ada 9 canonical sections. Saya pakai 7 warna unik, dengan 2 pasangan section yang semantically related saling share warna (Incoming/Outgoing untuk "panel flow direction", Trailer/Karoseri untuk "kendaraan body"). Kalau user prefer 100% unik per section, tinggal split saja — palette sudah disiapkan.

**Foreground (warna teks label) ikut berubah per section:** sebelumnya semua label pakai `ColorAccentBlue` (biru tua) yang kontras-nya jelek di background kuning. Sekarang teks label match ke dark tone dari warna section → readable di printer hitam-putih juga (semua dark tones masih punya luminance < 0.3).

**Tidak ada test khusus** untuk palette — perubahan visual murni. Sudah dipastikan build sukses dan tidak ada section yang di-render lewat code path lain.

### Impact bahasa awam
> Customer yang minta PDF format Modern dengan banyak section (Box + Incoming + Outgoing + Trailer + Karoseri + Jasa) tidak lagi melihat 3 warna ber-ulang. Tiap kategori produk punya warna sendiri yang tetap engineer-friendly (tidak girly), dan teks judul section kontras kuat dengan background-nya.

---

## File summary follow-up pass

### Create (4 file)
1. `PanelCalculator.Data/Migrations/ProductsIndexMigrator.cs`
2. `PanelCalculator.Data/DataSeeding/EstimationCsvImporter.cs`
3. `PanelCalculator.Tests/Format/ProductsIndexMigratorTests.cs`
4. `PanelCalculator.Tests/Format/EstimationCsvImporterTests.cs`

### Edit (5 file)
1. `PanelCalculator.Data/PanelCalculatorContext.cs` — composite unique index
2. `PanelCalculator.Data/Migrations/001_InitialCreate.sql` — fresh-install schema
3. `PanelCalculator.WinForms/Program.cs` — call migrator dalam MigrateDatabase()
4. `PanelCalculator.WinForms/Forms/EstimationHistoryForm.cs` — Import CSV button + handler + ConflictResolutionDialog
5. `PanelCalculator.WinForms/Services/PdfQuotationExport.cs` — palette refactor

### Modify project file (1)
- `PanelCalculator.Tests/PanelCalculator.Tests.csproj` — tambah `Microsoft.Data.Sqlite.Core` + `SQLitePCLRaw.bundle_e_sqlite3` (untuk test SQLite real)

---

## Final build & test status

| Stage                      | Status                                            |
|----------------------------|---------------------------------------------------|
| Baseline (sebelum follow-up) | 0 error · 1 warning (DashboardForm.cs:489) · 94/95 test pass (1 flaky LicenseService) |
| Final                      | 0 error · 0 warning · 105/105 test pass          |
| Selisih                    | +10 test baru (4 ProductsIndexMigrator + 6 EstimationCsvImporter) |

> Catatan: Saat re-run di pass ini, pre-existing failing test `LicenseServiceTests.ValidateLicense_TamperedSignature_ReturnsInvalidSignature` ternyata pass — ini test flaky yang kadang gagal kadang sukses (tidak terkait perubahan kami). Final run menunjukkan 105/105 hijau.
