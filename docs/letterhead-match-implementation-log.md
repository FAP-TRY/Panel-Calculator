# Letterhead Match — Implementation Log

**Tanggal:** 2026-05-28
**Versi app:** v1.2.7 (tidak di-bump pada fase ini — sesuai instruksi user)
**Branch:** `claude/sharp-jemison-2e465a`

User story:
> User upload 2 file DOCX referensi (161 PT Gemilang, 191 PT Anugerah Jaya)
> sebagai standar format penawaran resmi PT TTS. Rewrite `PdfLetterExport.cs`
> + `WordLetterExport.cs` agar output PDF/Word **pixel-match** dengan
> template DOCX resmi yang sudah dipakai selama ini.

---

## Ringkasan apa yang berubah (untuk customer)

Sebelumnya, ekspor PDF dan Word menghasilkan dokumen "modern" dengan
table summary lengkap (Subtotal, Margin, DPP, PPN, PPh, GRAND TOTAL,
Terbilang dst.) yang **tidak match** dengan format yang biasa dipakai
PT TTS untuk customer. Customer harus copy-paste isi ke template Word
PT TTS secara manual sebelum kirim.

Sekarang, output PDF dan Word **identik dengan template DOCX resmi**:

1. **Latar belakang letterhead PT TTS** (logo + sertifikasi KAN/LMK +
   alamat footer) muncul di **setiap halaman** PDF, dan di **header**
   tiap halaman Word. Tidak perlu print di kertas kop khusus lagi —
   PDF/Word sudah include kop.
2. **Tanda tangan & stempel** Pak Kuntjoro langsung ter-cap di posisi
   yang benar (di atas nama "Kuntjoro Handoko" dan jabatan "Direktur").
   Customer tidak perlu print → tanda tangan basah → scan lagi.
3. **Layout match referensi**: Nomor / Perihal / Lampiran di kiri,
   "Kepada: [PT Customer], alamat" di kanan, tanpa "Up." kalau
   nama customer = nama PT. Bullet list "Kondisi Penawaran"
   memakai bullet `•` (bukan numbered). Tabel ringkas hanya 3 kolom
   (No. / Nama Barang / Harga Satuan Rp).
4. **Multi-panel**: Page 1 = surat penawaran ringkas (1 baris per panel
   di tabel), Page 2+ = "Rincian Material" per panel dengan section
   divider rows ("Box Panel :", "Incoming :", "Outgoing :", "Lainnya :")
   dan kolom No / Material / Merek / Tipe / Satuan / Jumlah.
5. **Default signer**: "Kuntjoro Handoko" + "Direktur" kalau setting
   Settings → SignerName / SignerTitle kosong. User bisa override
   via tab Settings.
6. **Bahasa**: angka rupiah pakai format Indonesia dengan akhiran
   `,-` (mis. `4.320.000,-`) sesuai referensi.

---

## File yang disentuh

| File                                                          | Status     | Catatan |
|---------------------------------------------------------------|------------|---------|
| `PanelCalculator.WinForms/Services/PdfLetterExport.cs`        | REWRITE    | Layout baru: letterhead bg + signature/stamp overlay |
| `PanelCalculator.WinForms/Services/WordLetterExport.cs`       | REWRITE    | Layout baru: letterhead di header section + sig/stamp inline |
| `PanelCalculator.WinForms/Assets/Letterhead/letterhead.jpg`   | NEW (di-extract sebelumnya) | Full-page kop surat |
| `PanelCalculator.WinForms/Assets/Letterhead/signature.png`    | NEW (di-extract sebelumnya) | Tanda tangan handwriting |
| `PanelCalculator.WinForms/Assets/Letterhead/stamp.png`        | NEW (di-extract sebelumnya) | Stempel PT TTS bulat |
| `PanelCalculator.WinForms/PanelCalculator.WinForms.csproj`    | (di-update sebelumnya) | EmbeddedResource 3 image |
| `PanelCalculator.Tests/Format/PdfLetterExportTests.cs`        | NEW        | 5 smoke test (4 normal + 1 EmitSamples opt-in) |
| `PanelCalculator.Tests/Format/WordLetterExportTests.cs`       | UPDATE     | Threshold 1KB → 50KB (letterhead must be embedded); cek signer default |
| `docs/letterhead-match-implementation-log.md`                 | NEW        | (file ini) |

**Call site existing JANGAN BREAK** — verified:
- `EstimationHistoryForm.cs` line 495, 623, 816, 931 (4 call site)
- `MainForm.cs` line 1558 (1 call site)

Semua method signature tetap kompatibel — argumen masih sama persis,
hanya internal implementation yang berubah. Build clean: 0 error,
1 warning (warning unrelated di `DashboardForm.cs` yang sudah ada
sebelumnya).

---

## Arsitektur perubahan

### 1. Embedded resource loading

3 image di-load dari assembly resource:
```csharp
typeof(PdfLetterExport).Assembly.GetManifestResourceStream(
    "PanelCalculator.WinForms.Assets.Letterhead.letterhead.jpg")
```
- Letterhead.jpg → diserahkan ke `BackgroundImageHandler` yang dipanggil
  on `PdfDocumentEvent.START_PAGE` → draw full-page background tiap halaman
- Signature.png + Stamp.png → di-overlay manual di canvas page terakhir
  setelah text signature block di-render

### 2. PDF: signature + stamp positioning

Trick: kita pakai `Document.GetRenderer().GetCurrentArea().GetBBox().GetTop()`
SEBELUM `doc.Add(sigTbl)` untuk dapat Y koordinat awal area.
Setelah render, kita hitung:

```
signerNameTopY = yBefore - (2 lines × 14pt) - SignatureGap(100pt)
```

Lalu signature image rectangle di-draw via `PdfCanvas.AddImageFittedIntoRectangle`
di posisi `(pageWidth*0.55, signerNameTopY+2, 130pt, 101pt)`,
stamp di-overlap di sebelah kanan signature dengan ukuran 85×85.

Fallback: kalau renderer state tidak available (mis. block flow ke page baru),
pakai constant `bottomMargin(43) + 28 + 4 ≈ 75pt` dari bawah page.

### 3. Word: letterhead via header section

`doc.AddHeaders()` lalu pakai `doc.Headers.Odd` (DocX 4.x — header default).
Letterhead.jpg di-insert sebagai `CreatePicture(height, width)` di header.
Word akan reuse header di setiap halaman secara otomatis.

Signature + stamp di-insert sebagai inline picture di paragraph yang
sama (side-by-side, tidak strict overlap karena DocX 4.x tidak expose
z-order). Customer bisa edit di Word untuk fine-tune kalau perlu.

### 4. Section mapping (sesuai spec)

Display group di Rincian Material (Page 2+):

| Section raw (Estimation.Detail.Section) | Display label |
|------------------------------------------|---------------|
| `Box`, `Box Panel`                       | `Box Panel :` |
| `Material Utama`, `Incoming`             | `Incoming :`  |
| `Material Pendukung`, `Outgoing`         | `Outgoing :`  |
| `Material Lainnya`, `Trailer`, `Karoseri`, `Jasa`, `Lainnya` | `Lainnya :` |

Order render: Box Panel → Incoming → Outgoing → Lainnya.
Items dalam tiap group dipertahankan urutan insertion-nya.

### 5. Layout konstanta

```
Page size      : A4 portrait (595 × 842 pt)
Margins        : top=85 / bot=43 / left=71 / right=43 pt
                 (≈ 3.0 / 1.5 / 2.5 / 1.5 cm)
Body font      : Helvetica 9-10pt
Header font    : Helvetica-Bold 10pt
Table header   : 9pt bold, bg=#E8EEF6
Table data     : 9pt, alternating bg=#F8FAFC
Bullet         : "• " indent ~12pt
Signature gap  : 100pt (untuk fit gambar sig+stamp)
Sig image      : 130×101 pt
Stamp image    : 85×85 pt
Sig X          : pageWidth × 0.55 (right column)
```

---

## Default signer (override-able)

| Setting key      | Default fallback     |
|------------------|----------------------|
| `SignerName`     | `Kuntjoro Handoko`   |
| `SignerTitle`    | `Direktur`           |
| `OfferLocation`  | `Bandung`            |

Existing default di `Program.MigrateDatabase()` tidak diubah:
- `SignerName` = `""` (kosong → fallback ke Kuntjoro Handoko)
- `SignerTitle` = `"Marketing"` ← **NOTE**: kalau user belum pernah
  buka Settings, fallback default sekarang **OVERRIDE** ke `Direktur`
  saat export. Kalau user MAU pakai "Marketing", tinggal isi explicit
  di Settings.

> **Pertanyaan untuk user:** Apakah behavior ini OK?
> Atau apakah lebih baik tetap pakai "Marketing" sebagai default
> dan hanya pakai "Direktur" kalau setting kosong/Null total?
> Saat ini logic: kalau setting value = "" (kosong), fallback ke
> "Kuntjoro Handoko" / "Direktur". Kalau setting value diisi
> (mis. "Marketing"), pakai yang diisi.

---

## Verifikasi

### Build
```
dotnet build PanelCalculator.sln -c Release
→ 0 error, 1 warning (DashboardForm.cs CompanyName — pre-existing)
```

### Tests
```
dotnet test PanelCalculator.Tests
→ 130/130 pass (baseline 125 + 5 baru)
```

Test baru di `PdfLetterExportTests.cs`:
1. `Generate_SinglePanel_ProducesValidPdfWithLetterhead` — file > 50KB
2. `Generate_SinglePanel_NoSettingsOverride_UsesDefaultSigner` — pakai
   default Kuntjoro Handoko / Direktur
3. `GenerateCombined_TwoPanels_ProducesMultiPagePdf` — page count ≥ 3
4. `MapSectionToDisplay_AllAliases_ReturnsCorrectGroup` — section mapping
5. `EmitSamples_ToRepoFolder_WhenEnvVarSet` — opt-in via `EMIT_SAMPLES=1`

Test updated di `WordLetterExportTests.cs`:
- Threshold 1KB → 50KB (letterhead embedded)
- Cek "Kuntjoro Handoko" / "Direktur" muncul di output
- Cek "Rincian Material" muncul di Lampiran field (multi-panel)

### Sample artefak
Di-generate via:
```
dotnet test -e EMIT_SAMPLES=1 --filter "FullyQualifiedName~PdfLetterExportTests.EmitSamples"
```
Output ada di `build/samples/letter-export/`:
- `sample-single-EV-Charger.pdf` (281 KB)
- `sample-single-EV-Charger.docx` (220 KB)
- `sample-combined-Panel-Distribusi.pdf` (606 KB, 3 pages)
- `sample-combined-Panel-Distribusi.docx` (221 KB)

Folder `build/` di-`.gitignore` (untuk verifikasi: cek `.gitignore`
root project) sehingga sample artefak tidak commit.

---

## Hal yang TIDAK dilakukan (sesuai instruksi)

- **TIDAK** bump version (user yang handle, current v1.2.7)
- **TIDAK** ubah skema DB
- **TIDAK** ubah method signature `Generate` / `GenerateCombined`
  → existing call site (EstimationHistoryForm, MainForm) tetap kompatibel
- **TIDAK** commit (user yang handle)
- **TIDAK** ubah `PdfQuotationExport.cs` (format Modern — untuk kasus
  customer yang mungkin pingin tabel summary lengkap dengan PPN/PPh/dst)

---

## Catatan teknis kalau perlu fine-tune

### Signature/stamp position drift
Kalau di production ada kasus signature+stamp posisi-nya off (geser
ke atas/bawah page), tuning ada di `AddSignatureBlock()`:
- `SignatureGap` constant → kalau gambar terpotong / overlap teks,
  naikkan (100 → 120/150).
- `overlaySigY = signerNameTopY + 2f` → tune offset +/-

### Letterhead spill
Kalau ada konten Page 1 yang terlalu panjang sehingga konten meluap ke
Page 2 SEBELUM Rincian Material, letterhead background tetap muncul
di Page 2 (karena event handler `START_PAGE`). Tidak masalah.

### Multi-panel page count
Combined PDF dengan N panel = N+1 halaman (1 cover + N rincian).
Kalau 1 panel punya banyak items sehingga rincian-nya overflow ke
halaman ke-2, iText otomatis split table dan letterhead tetap muncul.

### Word interop
Customer bisa buka .docx hasil di Word 2016+, edit fine-tune (mis. tambah
catatan, ganti urutan), lalu save. Letterhead di header tidak akan
"jatuh" karena disisipkan via Section Header (built-in Word feature).
