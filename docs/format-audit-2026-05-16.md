# Format Audit — Panel Calculator
**Tanggal:** 2026-05-16
**Scope:** PDF Penawaran (Formal + Modern) + CSV Import + CSV Export
**Mode:** AUDIT ONLY — tidak ada perubahan kode.

File sumber yang diaudit:
- `PanelCalculator.WinForms/Services/PdfLetterExport.cs` (Formal, 2 halaman)
- `PanelCalculator.WinForms/Services/PdfQuotationExport.cs` (Modern, 1 halaman)
- `PanelCalculator.WinForms/Forms/MainForm.cs` (call site export + export produk CSV)
- `PanelCalculator.WinForms/Forms/EstimationHistoryForm.cs` (CSV export estimasi tersimpan)
- `PanelCalculator.WinForms/Forms/SettingsForm.cs` (Import CSV/Excel)
- `PanelCalculator.Data/DataSeeding/ProductSeeder.cs` (engine upsert CSV)
- `Tools/*.py` (referensi schema kolom)

---

## Bagian A — PDF Penawaran

### A.1 Kondisi saat ini

**Formal (`PdfLetterExport.cs`)** — sudah ada:
- Letterhead image background (PNG/JPG di samping EXE) via `BackgroundImageHandler` (line 129–153).
- Dua kolom header: ref block (Nomor/Perihal/Lampiran) + Kepada/Up. (line 172–205).
- Salutation "Dengan hormat" (line 209–212).
- Tabel ringkasan **per-section** (bukan per-item): No / Nama Barang / Harga Satuan (line 222–237).
- Kondisi penawaran 4 poin (line 247–257).
- Closing + signature block dengan nama + jabatan (line 273–284).
- Halaman 2 "RINCIAN MATERIAL" per-section dengan tabel: No / Material / Merek / Tipe / Satuan / Jumlah (line 290–334).

**Formal — yang BELUM ada:**
- **Subtotal / Margin / DPP / PPN / PPh / Ongkir / GRAND TOTAL row** — TIDAK ditampilkan sama sekali di Page1. Hanya tabel section-subtotal saja. `subtotal`, `marginAmount`, `taxAmt`, `pphAmt`, `total` di-pass sebagai parameter tapi **tidak pernah dirender** (line 99–101 & 165).
- **Terbilang (angka grand total dalam huruf)** — tidak ada.
- **Harga satuan / Qty per item** di Page2 — kolom "Jumlah" hanya quantity, tidak ada Harga Satuan dan Total Line. Sales tidak bisa lihat breakdown harga.
- **Page X of Y / footer** — tidak ada page numbering.
- **NPWP** — tergantung pada letterhead image (kalau tidak ada image, tidak ada NPWP).
- **Nomor surat real** — di call site (`MainForm.cs:1560`) pakai `txtNomorSurat.Text` atau fallback `DRAFT-yyyyMMdd-HHmm`. Tidak ada format standar TTS.
- **Garis pembatas kop / footer** — semua via background image. Kalau image tidak ada, halaman jadi polos.

**Modern (`PdfQuotationExport.cs`)** — sudah ada:
- Header dua kolom: CompanyName/Address/Phone | "PENAWARAN HARGA" + nomor (line 60–72).
- Garis pembatas biru (line 75–76).
- Blok client + tanggal (line 79–100).
- Tabel items berwarna per-section dengan kolom: No / Kode / Nama Produk / Qty / Satuan / Harga Satuan / Adj / Total (line 103–162). Section header dengan subtotal di kanan.
- Summary block: Subtotal / Margin atau Diskon / Ongkir / PPN / PPh — dengan warna merah untuk diskon/PPh (line 165–198).
- Total row biru solid bold besar (line 200–212).
- Footer 1 baris (line 217–220).

**Modern — yang BELUM ada:**
- **Terbilang** — tidak ada.
- **Page X of Y** — tidak ada.
- **DPP terpisah** — DPP (subtotal+margin+ongkir sebelum PPN) tidak dirender, langsung lompat ke PPN.
- **Tanda tangan** — tidak ada signature block sama sekali.
- **Syarat & Ketentuan** — cuma 1 kalimat di footer, tidak ada poin pembayaran/DP/lead time/garansi.
- **NPWP company** — tidak ada field setting untuk ini.
- **Section "Box / Incoming / Outgoing / Trailer / Karoseri / Jasa"** — hanya 3 section yang dirender (`Material Utama / Pendukung / Lainnya` di line 116). Section lain yang ada di skema (lihat `PdfLetterExport.cs:215–216`) **HILANG total** dari format Modern. **BUG.**

---

### A.2 Checklist standar vs aktual

| Item | Standar | Formal | Modern |
|------|---------|--------|--------|
| Kop surat (logo + nama PT + NPWP + telp) | Wajib | Via image bg ✓ (kalau ada) | Sebagian (no NPWP) |
| Garis pembatas kop | Wajib | Via image | ✓ |
| Nomor surat | Wajib | ✓ | ✓ |
| Tanggal + kota | Wajib | ✓ (di signature) | ✓ (hanya tanggal, kota tidak ada) |
| Kepada Yth + alamat | Wajib | ✓ | ✓ |
| Salam pembuka | Wajib | ✓ | ✗ |
| Tabel item per-item lengkap | Wajib | ✗ (cuma section subtotal di p1, no price di p2) | ✓ |
| Right-align angka | Wajib | ✓ | ✓ |
| Format Rupiah `Rp 1.234.567` | Wajib | ✓ (tapi suffix `,-` di p1) | ✓ |
| Subtotal | Wajib | ✗ | ✓ |
| Margin/Diskon | Wajib | ✗ | ✓ |
| DPP | Wajib | ✗ | ✗ |
| PPN 11% | Wajib | ✗ (cuma di catatan teks) | ✓ |
| PPh | Wajib | ✗ | ✓ |
| Ongkir | Wajib | ✗ | ✓ |
| GRAND TOTAL bold besar | Wajib | ✗ | ✓ |
| Terbilang | Wajib | ✗ | ✗ |
| S&K (4–5 poin) | Wajib | ✓ (4 poin) | ✗ (1 kalimat) |
| Tanda tangan + jabatan | Wajib | ✓ | ✗ |
| Cap (placeholder) | Wajib | ✗ | ✗ |
| Footer page X of Y | Wajib | ✗ | ✗ |
| Kontak sales di footer | Disarankan | ✗ | ✗ |

### A.3 Issue per kategori

**Alignment & layout (Formal):**
- `PdfLetterExport.cs:222` — `pw = {8, 62, 30}` percent. Kolom "Nama Barang" 62% — OK, tapi tidak ada kolom harga satuan vs total, hanya 1 angka yang ambigu apakah sudah dikalikan qty.
- `PdfLetterExport.cs:235` — Harga ditulis `FmtNum(st) + ",-"`. Suffix `,-` Indonesian-style legit tapi tidak konsisten dengan `Rp ` prefix yang ada di helper `Rp()` (line 382). Helper `Rp()` malah TIDAK PERNAH DIPAKAI.
- `PdfLetterExport.cs:313` — Page2 column widths `{6, 37, 16, 23, 9, 9}` — kolom "Tipe" 23% (untuk ReferenceCode) terlalu lebar; "Satuan" dan "Jumlah" 9% masing-masing kemungkinan kepotong saat ref code panjang.
- `PdfLetterExport.cs:325` — `bg = itemNo % 2 == 0 ? ColorTableAlt : ColorWhite`. Baris GANJIL pakai putih, GENAP pakai biru muda. Konvensi biasanya kebalik (ganjil zebra). Bukan bug, hanya catatan estetika.

**Alignment & layout (Modern):**
- `PdfQuotationExport.cs:103` — 8 kolom dengan width `{5,11,32,7,8,12,7,18}` total 100. Tapi kolom "Adj" 7% (line 149) cuma menampilkan `+5.0%` atau `—`, untuk produk tanpa adj jadi sia-sia ruang. Pertimbangkan menggabung dengan harga.
- `PdfQuotationExport.cs:131–141` — Section header pakai 7 sel kosong manual untuk fake colspan. Bug: kalau jumlah kolom berubah, harus update angka 7 ini. Lebih baik `cell.SetColspan(7)`.

**Format Rupiah:**
- Formal: campur `"Rp "` (helper unused) dan `",-"` suffix. **Inkonsisten.**
- Modern: konsisten `"Rp 1.234.567"` via `FormatRupiah()` (line 247). Bagus.

**Font:**
- Kedua format pakai `StandardFonts.HELVETICA` (built-in PDF font). Tidak mendukung karakter khusus Indonesia (sebenarnya ASCII OK, tapi simbol mata uang `€`, `™`, dll akan rusak). Tidak ada embed font.

**Spacing & page break:**
- Formal: `AreaBreak(NEXT_PAGE)` hard-coded (line 104) memaksa page 2 walaupun page 1 cukup pendek. Wasteful kalau item < 5.
- Formal: kalau jumlah section > 5 di page 2, tabel section terakhir mungkin pecah jelek karena `SetMarginBottom(18)` per section tapi tidak ada `KeepTogether`.
- Modern: tabel items panjang akan pecah ke page 2 tanpa header berulang (tidak pakai `SetSkipFirstHeader(false)` atau `SetSkipLastFooter`). Sales bingung baca page 2.

**Page break (kedua format):**
- Tidak ada PageEvent untuk render footer "page X of Y" — perlu 2-pass rendering atau `PdfDocument` + `OnEvent(END_PAGE)`.

### A.4 Rekomendasi konkret (file:line)

| # | Rekomendasi | File:Line |
|---|-------------|-----------|
| A1 | Tambah summary block Subtotal/Margin/DPP/PPN/PPh/Ongkir/Total di Page1 Formal **sebelum** "Kondisi Penawaran" | `PdfLetterExport.cs:245` (insert sebelum baris ini) |
| A2 | Tambah baris "Terbilang: ……… rupiah" setelah grand total. Buat helper `Terbilang(decimal)` di class baru `Services/TerbilangFormatter.cs` | `PdfLetterExport.cs:265` |
| A3 | Halaman 2 (Rincian) — tambah kolom Harga Satuan + Total Line, recalc column widths `{5,28,12,18,7,7,11,12}` | `PdfLetterExport.cs:313–331` |
| A4 | Hapus `,-` suffix, gunakan helper `Rp()` (line 382) di p1 | `PdfLetterExport.cs:235` |
| A5 | Tambah PageEvent untuk footer "Halaman {n} dari {total}" + telp sales | `PdfLetterExport.cs:73` (after `pdf` create) |
| A6 | Modern: tambah signature block + S&K poin (DP, lead time, garansi, masa berlaku) | `PdfQuotationExport.cs:212` (after total table) |
| A7 | Modern: render SEMUA section dari skema (Box, Incoming, Outgoing, Trailer, Karoseri, Jasa) — bukan cuma 3 | `PdfQuotationExport.cs:116` |
| A8 | Modern: pakai `cell.SetColspan(7)` ganti 7 sel kosong manual | `PdfQuotationExport.cs:131–141` |
| A9 | Tambah `taxPercent` lihat juga `pphPercent` untuk DPP row di Modern | `PdfQuotationExport.cs:194` |
| A10 | Pertimbangkan `KeepTogether(true)` per section di Page2 formal | `PdfLetterExport.cs:314` |
| A11 | Tambah setting `CompanyNpwp`, render di header Modern + sebagai fallback teks di Formal kalau letterhead image tidak ditemukan | `PdfQuotationExport.cs:55–67` + `PdfLetterExport.cs:124` |

---

## Bagian B — CSV Import

Lokasi: `SettingsForm.cs:406` (button click) → `SettingsForm.cs:435` (`RunImport`) →
- Untuk Excel: parsing manual via ClosedXML (line 574–772) lalu `ProductSeeder.SeedFromRecordsAsync`.
- Untuk CSV: langsung `ProductSeeder.SeedFromCsvAsync` (`ProductSeeder.cs:17`).

### B.1 Format yang diterima saat ini

**CSV (`ProductSeeder.cs:17–35`):**
- Pakai CsvHelper, header dinormalisasi: lowercase + buang underscore (line 27).
- Property record `ProductCsvRecord` punya: `Category`, `ReferenceCode`, `ProductName`, `Specifications`, `Price`, `PriceYear`, `StockStatus`, `Vendor` (line 177–187).
- Header yang valid: `category`/`Category`, `reference_code`/`referencecode`/`ReferenceCode`, dst — case insensitive, underscore optional.
- Encoding: UTF-8 dengan BOM detection (line 30). Bagus.
- **Tidak menerima header Bahasa Indonesia di mode CSV** (hanya bahasa Inggris property name). Excel parser punya alias yang lebih luas (`SettingsForm.cs:605–611`) tapi CSV TIDAK punya alias mapping.
- **Harga**: tipe `decimal`, tapi CsvHelper default culture `InvariantCulture` (line 22) — jadi:
  - `1234567` OK
  - `1234.56` OK (titik = desimal)
  - `1.234.567` GAGAL (parse "1.234567" → tetap 1.234567, salah!)
  - `Rp 1.234.567` GAGAL parse → exception
  - `1,234,567.00` GAGAL (koma bukan thousand sep di InvariantCulture)
- **Strategi**: upsert by `ReferenceCode` saja, **bukan** (ReferenceCode + Vendor). Artinya kalau ada produk dengan kode sama dari 2 vendor berbeda → vendor kedua overwrite vendor pertama. **BUG nyata** kalau katalog campur Schneider+Himel pakai kode generik.
- Skip baris dengan `ReferenceCode` kosong (`ProductSeeder.cs:54`). Bagus.

**Excel (`SettingsForm.cs:574–772`):**
- Lebih toleran. Alias kolom luas (line 605–611), auto-detect section header dengan merged cells, auto-derive category dari family name (`FamilyToCategory`).
- Harga di Excel: jika `XLDataType.Number` ambil langsung; jika string, strip "Rp", spasi, titik, koma (line 668–670) — agresif tapi mengasumsikan format ID.
- Vendor: bisa override via dialog kalau tidak ada kolom (line 448–457). Bagus.

### B.2 Issue

| # | Issue | Severity |
|---|-------|----------|
| B1 | CSV importer **tidak punya alias Bahasa Indonesia** (kategori, kode, nama, harga, vendor) — beda dengan Excel importer yang punya alias luas | High |
| B2 | CSV harga tidak handle format Indonesia (`1.234.567`, `Rp 1.234.567`) — akan exception atau salah parse | High |
| B3 | Upsert key hanya `ReferenceCode` (`ProductSeeder.cs:74-75`) bukan (ReferenceCode + Vendor) — kode sama dari vendor berbeda saling overwrite | High |
| B4 | Error reporting: max 10 baris pertama saja yang ditampilkan (`ProductSeeder.cs:171`). User tidak tahu detail lain | Medium |
| B5 | Tidak ada **dry-run / preview** sebelum upsert — user langsung lihat hasil | Medium |
| B6 | Tidak ada laporan inserted vs updated vs skipped — cuma 1 angka total | Medium |
| B7 | Excel parser men-strip titik dari harga, tapi kalau harga punya **decimal** (e.g. 1.234.567,50) akan kehilangan 50 sen | Low |
| B8 | `MissingFieldFound = null` + `HeaderValidated = null` (`ProductSeeder.cs:24-25`) artinya kolom tipo silent-skip — user tidak tahu kalau header salah | Medium |
| B9 | `StockStatus` default 0 di CSV record (`ProductSeeder.cs:185`), tapi DB expect 1 atau 2. Kalau CSV tidak punya kolom ini, semua produk masuk dengan StockStatus=0 (invalid) | Medium |
| B10 | Tidak ada validasi `PriceYear` di CSV (di Excel ada — line 693). Bisa terisi 0 atau garbage | Low |

### B.3 Rekomendasi CSV Import

1. **B-R1** — Buat `ClassMap<ProductCsvRecord>` di `ProductSeeder.cs` dengan multiple `Name()` aliases per property (Indonesian + English): "kategori"|"category", "kode"|"reference_code"|"ref", "nama"|"product_name"|"nama_produk", "harga"|"price", "vendor"|"merek"|"merk", dll.
2. **B-R2** — Tambah `TypeConverter` custom untuk `decimal Price` yang strip `Rp`, spasi, dot/comma sesuai konteks ID. Reuse logic dari `SettingsForm.cs:668-670`.
3. **B-R3** — Ubah upsert lookup di `ProductSeeder.cs:74-75` & 121-122 menjadi `p.ReferenceCode == r.ReferenceCode && p.Vendor == r.Vendor` (dengan handling null Vendor).
4. **B-R4** — Tambah laporan terstruktur: `{inserted: N, updated: M, skipped: K, errors: [...]}` — tampilkan di MessageBox + tombol "Lihat detail" untuk modal scrollable.
5. **B-R5** — Default `StockStatus = 1` di `ProductCsvRecord` initializer (`ProductSeeder.cs:185`).
6. **B-R6** — Set `MissingFieldFound = args => log.Add($"Missing column at row {args.Index}")` ganti silent null, accumulate ke summary.
7. **B-R7** — Tambah preview dialog (DataGridView read-only 20 baris pertama) sebelum commit untuk Excel + CSV.

---

## Bagian C — CSV Export

Ada **3 jalur** CSV export:

1. **Export Produk** (`MainForm.cs:1065 ExportToCsv`) — schema snake_case English, match importer.
2. **Export Estimasi dari MainForm** (`MainForm.cs:1627`) — header info + items + summary, Indonesian.
3. **Export Estimasi dari History** (`EstimationHistoryForm.cs:290`) — sama dengan #2 plus kolom Status.

### C.1 Format yang sekarang dihasilkan

**Export Produk (`MainForm.cs:1068`):**
```
category,reference_code,product_name,specifications,price,price_year,stock_status,vendor
```
- Separator: `,`
- Encoding: UTF-8 (TANPA BOM — `File.WriteAllText` + `Encoding.UTF8` tanpa eksplisit BOM, akan otomatis BOM karena `Encoding.UTF8` default emits BOM — sebenarnya OK)
- Quote: hanya kolom string (`Q()` helper line 1071)
- Harga: plain number `p.Price.ToString("0")` — bagus
- **Round-trip: OK** karena schema match importer property.

**Export Estimasi (kedua tempat):**
```
Nomor Estimasi,EST-...
Klien,"…"
…<blank line>
No,Seksi,Kode Referensi,Nama Produk,Merek/Vendor,Satuan,Qty,Harga Satuan (Rp),Total (Rp)
1,"Material Utama","NSX100","NSX100F TM100D 3P","Schneider",pcs,1,5234567,5234567
…<blank line>
,,,,,,,Subtotal,12345678
,,,,,,,TOTAL,15000000
```

### C.2 Issue

| # | Issue | Severity |
|---|-------|----------|
| C1 | **Round-trip estimasi: TIDAK mungkin reimport** — schema estimasi tidak ada importer-nya. Hanya export-only | High (kalau diharapkan round-trip) |
| C2 | Export Estimasi pakai **header Bahasa Indonesia mixed case** — tidak match konvensi importer (snake_case English) | Medium |
| C3 | Mixed structure: header info + data + summary di file yang sama. Tidak bisa di-parse generic oleh Excel/PowerBI sebagai tabel | Medium |
| C4 | Tanggal format `dd/MM/yyyy` (`EstimationHistoryForm.cs:316`) — bukan ISO `YYYY-MM-DD`. Excel ID kadang salah interpretasi sebagai US date | Medium |
| C5 | Encoding `Encoding.UTF8` — secara default sudah emit BOM, jadi sebenarnya OK. **Tapi cek manual diperlukan** karena dokumentasi MS Docs `Encoding.UTF8` static instance memang dengan BOM (`new UTF8Encoding(true)`) | Verified OK |
| C6 | Separator `,` — masalah kalau buka di Excel ID dengan regional setting koma=desimal. Lebih aman pakai `;` di export estimasi (tapi konflik dengan export produk yang harus match importer) | Low |
| C7 | Item harga di estimasi pakai `ToString("F0", IdCulture)` (line 340-341) → menghasilkan `5.234.567` (Indonesian thousand sep). **Tapi** field tidak di-quote → CSV parser pecah karena titik bukan separator, tapi koma di angka decimal `5,5` akan pecah. Untuk safety, quote semua nilai numerik dengan thousand sep | High |
| C8 | Tidak ada kolom `total_item_count`, `created_by`, `notes` di summary | Low |
| C9 | Filename pakai nama klien tanpa sanitize (`MainForm.cs:1645`) — kalau klien punya `/` atau `:` di nama → file error di Windows | Medium |
| C10 | Kolom "Total (Rp)" di header — sebut "Rp" tapi nilainya tanpa Rp, mismatch hint | Low |

### C.3 Round-trip test (analisis statis)

| Skenario | Hasil |
|----------|-------|
| Export Produk → reimport ke fresh DB | ✓ akan bersih (schema match) |
| Export Produk → edit harga `100000` → reimport | ✓ OK |
| Export Produk → buka di Excel ID, save → reimport | ✗ kemungkinan Excel akan mengubah `1234567` jadi format scientific `1.23E+06` atau insert thousand sep |
| Export Estimasi → reimport | ✗ tidak ada importer untuk estimasi |

### C.4 Rekomendasi CSV Export

1. **C-R1** — Standardisasi semua CSV export ke **header English snake_case** + UTF-8 BOM eksplisit + tanggal ISO. Buat helper `CsvWriter.Write(path, headers, rows)` di `Services/CsvWriter.cs`.
2. **C-R2** — Untuk Estimasi: split jadi 2 file atau gunakan sheet Excel multi-tab. **Atau** terima format ini untuk human reading dan tambah opsi "Export Excel (.xlsx)" terpisah dengan 3 sheet: Info / Items / Summary — lebih cocok untuk ditelaah sales.
3. **C-R3** — Quote SEMUA nilai numerik yang punya thousand separator, atau ubah ke plain number tanpa separator: `d.UnitPrice.ToString("F0", CultureInfo.InvariantCulture)` ganti `IdCulture`. Lihat `EstimationHistoryForm.cs:340-341` & `MainForm.cs:1679-1680`.
4. **C-R4** — Sanitize filename: tambah helper `SanitizeFilename(string)` yang strip karakter Windows-invalid (`< > : " / \ | ? *`).
5. **C-R5** — Tambah kolom `created_at` ISO format di summary block estimasi.
6. **C-R6** — Tanggal export estimasi: ganti `dd/MM/yyyy` → `yyyy-MM-dd` (atau `dd MMMM yyyy` panjang tanpa ambigu).
7. **C-R7** — Bila ingin round-trip estimasi: buat importer di History yang baca CSV/Excel dan rebuild estimasi (snapshot-only, bukan edit). Cocok untuk migrasi antar PC.

---

## Quick Wins (1 hari kerja)

| # | Win | File:Line | Impact |
|---|-----|-----------|--------|
| QW1 | Tambah summary block (Subtotal/Margin/PPN/Total) + Terbilang di PDF Formal Page1 | `PdfLetterExport.cs:244` | High — sales saat ini harus hitung manual saat PDF dicetak |
| QW2 | Render semua section (Box/Trailer/Karoseri/Jasa) di PDF Modern, bukan cuma 3 | `PdfQuotationExport.cs:116` | High — data hilang dari output |
| QW3 | Fix upsert key di importer jadi (ReferenceCode, Vendor) | `ProductSeeder.cs:74-75, 121-122` | High — cegah overwrite cross-vendor |
| QW4 | Tambah alias Indonesian header + custom decimal converter di CSV importer | `ProductSeeder.cs:17-35` (tambah ClassMap) | High — user bisa import file vendor as-is |
| QW5 | Plain number tanpa thousand separator di Export Estimasi CSV (`InvariantCulture` ganti `IdCulture`) | `EstimationHistoryForm.cs:340-355`, `MainForm.cs:1679-1692` | Medium — cegah CSV pecah di Excel |

---

## Effort estimate per rekomendasi

| Rec | Effort | Risk breaking |
|-----|--------|---------------|
| A1 (summary block Formal) | 2h | Low — append paragraf baru |
| A2 (Terbilang) | 3h (termasuk unit test) | Low — class helper baru, tidak menyentuh kode lain |
| A3 (Page2 harga + total) | 2h | Low |
| A4 (consistent Rp prefix) | 30m | Low |
| A5 (page X of Y footer) | 2h | Medium — perlu PageEvent dengan DocumentRenderer |
| A6 (signature + S&K di Modern) | 1.5h | Low |
| A7 (render semua section Modern) | 30m | Low |
| A8 (Colspan) | 30m | Low |
| A9 (DPP row) | 30m | Low |
| A10 (KeepTogether p2) | 1h | Medium — bisa cause overflow page break weird |
| A11 (NPWP setting) | 1.5h (UI Settings + render) | Low |
| B-R1 (CSV header alias) | 2h | Low |
| B-R2 (decimal converter) | 1.5h | Low — kalau ditest dengan beberapa format |
| B-R3 (upsert key Vendor) | 1.5h | **Medium-High** — perlu data migration check, kalau dulu sudah ada kode dup cross-vendor mungkin akan tercipta record baru |
| B-R4 (laporan inserted/updated/skipped) | 2h | Low |
| B-R5 (StockStatus default 1) | 5m | Low |
| B-R6 (missing field log) | 30m | Low |
| B-R7 (preview dialog) | 4h | Low |
| C-R1 (CsvWriter helper) | 2h | Low |
| C-R2 (Excel multi-sheet export) | 4h | Low — fitur baru |
| C-R3 (plain number) | 30m | **Medium** — bisa break user yang sudah punya workflow buka file Excel dengan auto-format |
| C-R4 (sanitize filename) | 15m | Low |
| C-R5 (created_at column) | 15m | Low |
| C-R6 (date format ISO) | 15m | Medium — breaks user yang punya pipeline parsing `dd/MM/yyyy` |
| C-R7 (round-trip importer estimasi) | 1 day | Medium — fitur baru, perlu rebuild logic |

**Total effort untuk semua rekomendasi:** ~5 hari developer, dengan QW1–QW5 mencakup ~6 jam pertama.
