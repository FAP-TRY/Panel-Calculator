# Multi-Panel Surat Penawaran — Implementation Log

**Tanggal:** 2026-05-24
**Versi app:** v1.2.5 (tidak di-bump pada fase ini)
**Branch:** `claude/sharp-jemison-2e465a`

User story (verbatim):
> Saat pembuatan surat penawaran harga, ada kemungkinan panel yang ditawarkan
> lebih dari 1. Saat ini hanya 1 estimasi panel saja yang dapat dimasukkan
> dalam surat. Buat agar bisa memasukkan beberapa estimasi harga dalam 1
> surat. PPN dihitung terakhir, bukan per panel.

---

## Ringkasan keputusan desain

| Aspek                 | Keputusan                                                                 |
|-----------------------|---------------------------------------------------------------------------|
| Cara pilih estimasi   | Checkbox kolom kiri di EstimationHistoryForm; multi-select bebas urutan   |
| Trigger compose       | Tombol baru "📑 Penawaran Gabungan (N)" — enabled saat ≥ 2 baris dicentang|
| Margin / PPh per panel| INDEPENDEN — pakai nilai asli yang sudah di-save                          |
| PPN                   | DIHITUNG SEKALI di akhir = round(DPP × 11%) — bukan SUM PPN per panel     |
| Ongkir                | SATU nilai gabungan di akhir (input user di dialog, default 0). Ongkir per-panel di-IGNORE karena gabungan biasanya satu pengiriman |
| Customer info         | Ambil dari estimasi pertama. WARNING bar di dialog kalau berbeda          |
| Format PDF            | Kedua format didukung: Surat Formal (kop) + Modern (warna-warni section)  |
| Skema DB              | TIDAK BERUBAH — semua data per panel sudah cukup, agregator murni in-memory|

---

## Arsitektur perubahan

```
┌──────────────────────────┐
│ EstimationHistoryForm    │  (checkbox column + tombol baru)
│   ↓ pilih 2+ estimasi    │
│   ↓ klik tombol gabungan │
└──────────┬───────────────┘
           ▼
┌──────────────────────────┐
│ CombineEstimationsDialog │  (input: ongkir, nomor surat, format PDF)
└──────────┬───────────────┘
           ▼
┌──────────────────────────┐
│ CombinedQuotationCalc-   │  (Core — agregasi murni, unit-tested)
│ ulator.Build(...)        │
│   → CombinedSummary      │
└──────────┬───────────────┘
           ▼
┌──────────────────────────┐
│ PdfLetterExport.Generate │  ATAU  PdfQuotationExport.GenerateCombined
│ Combined(...)            │        (Modern)
│ (Formal)                 │
└──────────────────────────┘
```

---

## File baru

| File                                                                              | Tujuan |
|-----------------------------------------------------------------------------------|--------|
| `PanelCalculator.Core/Services/CombinedQuotationCalculator.cs`                    | Core agregator, pure function, tidak depend ke EF/WinForms |
| `PanelCalculator.WinForms/Forms/CombineEstimationsDialog.cs`                      | UI dialog untuk compose (nomor surat, ongkir, format) |
| `PanelCalculator.Tests/Format/CombinedQuotationCalculatorTests.cs`                | 12 unit test menutup: subtotal sum, single PPN calc, SUM PPh, ongkir override, terbilang, edge cases, customer mismatch |
| `docs/multi-panel-implementation-log.md`                                          | Dokumen ini |

---

## File yang di-edit

| File                                                                | Apa yang berubah |
|---------------------------------------------------------------------|------------------|
| `PanelCalculator.WinForms/Services/PdfLetterExport.cs`              | Tambah method `GenerateCombined(...)` + record `CombinedPanel`. Format Formal multi-panel: salam pembuka, per-panel section dengan ringkasan section + sub-total panel, ringkasan akhir (sub-total per panel + DPP + PPN sekali + PPh total + grand total + terbilang), syarat ketentuan, tanda tangan, rincian material per panel di halaman terpisah |
| `PanelCalculator.WinForms/Services/PdfQuotationExport.cs`           | Tambah method `GenerateCombined(...)` + record `CombinedPanel`. Format Modern: banner panel berwarna (judul + subtotal), tabel item per panel dengan section colors, sub-total panel, ringkasan akhir, grand total banner, terbilang, S&K, signature |
| `PanelCalculator.WinForms/Forms/EstimationHistoryForm.cs`           | + Kolom checkbox `ColPick` di kiri, + tombol `📑 Penawaran Gabungan (N)`, + helper `UpdateCombineButtonState()` & `GetCheckedEstimationIds()`, + handler `BtnCombine_Click(...)`. Tinggi panel bawah dinaikkan 56→76 supaya muat hint label |

**TIDAK** di-edit: tidak ada perubahan ke model `Estimation`/`EstimationDetail`, tidak ada perubahan migrasi DB, semua fitur single-panel existing tetap utuh.

---

## Logic kalkulasi (Core)

`CombinedQuotationCalculator.Build(estimations, combinedShippingCost, taxPercent=11)`:

```
PanelSubtotal[i] = Estimation[i].SubTotal + Estimation[i].Margin
                   (per-panel shipping tidak masuk PanelSubtotal — di-ignore)

GrandSubtotal    = SUM(PanelSubtotal[i])
DPP              = GrandSubtotal + combinedShippingCost
TaxAmount        = round(DPP × taxPercent / 100, 0, AwayFromZero)   ← sekali!
TotalPPh         = SUM(Estimation[i].PPh)
GrandTotal       = DPP + TaxAmount − TotalPPh
Terbilang        = TerbilangFormatter.ToRupiah(GrandTotal)
```

Kenapa SUM PPh per panel (bukan rehitung): tarif PPh per panel bisa beda
(mis. panel A jasa konstruksi 2%, panel B sewa alat 10%) — recalculate
butuh disimpan dasar per-panel, sedangkan nilai PPh per panel sudah benar
saat estimasi disimpan. Pilihan paling aman & akurat: preserve dan sum.

Kenapa ongkir per-panel DI-IGNORE: ongkir di Estimation seringkali sudah
ditulis sebagai biaya pengiriman estimasi tunggal. Untuk surat gabungan,
kurir biasanya satu trip → user wajib input ongkir gabungan yang baru
(default 0 → user bisa skip kalau tidak ingin tambah ongkir). Asumsi ini
dijelaskan ke user via tooltip/label di dialog.

---

## Test coverage baru

`PanelCalculator.Tests/Format/CombinedQuotationCalculatorTests.cs` — 12 test:

1. `Build_TwoPanels_GrandSubtotalIsSumOfPanelSubtotals` — sum benar
2. `Build_PpnCalculatedOnceFromGrandSubtotalPlusOngkir_NotSumPerPanel` — single calc beda dengan naive per-panel sum (asserted ≠)
3. `Build_TotalPphIsSumOfPanelPph`
4. `Build_GrandTotal_IsDppPlusPpnMinusPph` — formula end-to-end
5. `Build_CombinedShippingOverridesPerPanelShipping` — eksplisit ignore per-panel shipping
6. `Build_TerbilangMatchesGrandTotal` — round-trip dengan TerbilangFormatter
7. `Build_ThrowsOnEmptyList`
8. `Build_ThrowsOnNegativeShipping`
9. `Build_ThrowsOnNegativeTaxPercent`
10. `CheckCustomerInfoMismatch_ReturnsNullWhenConsistent`
11. `CheckCustomerInfoMismatch_ReturnsMessageWhenClientDiffers`
12. `CheckCustomerInfoMismatch_IgnoresCaseAndWhitespace`
13. `Build_PreservesProjectNameAndEstimationNumber`

(13 test, gabungan di file yang sama)

---

## Build & test status

| Stage              | Status                                                  |
|--------------------|---------------------------------------------------------|
| Baseline (master)  | 0 error · 1 warning (DashboardForm.cs:489, pre-existing) · 105/105 test pass |
| After this change  | 0 error · 1 warning (sama, pre-existing) · **120/120 test pass** (+15 baru: 13 unit Core + 2 smoke end-to-end) |

Build: `dotnet build PanelCalculator.sln --configuration Release` → 0 error
Test : `dotnet test PanelCalculator.Tests --configuration Release --no-build`

---

## Alur baru dari sisi sales (panduan pakai)

1. **Buka Riwayat Estimasi** — lihat list semua estimasi seperti biasa.
2. **Centang kotak** di kiri tiap baris untuk panel yang mau digabung
   (minimal 2). Tombol "📑 Penawaran Gabungan (N)" otomatis nyala dengan
   menampilkan jumlah panel yang dipilih.
3. **Klik tombol** — muncul dialog "Surat Penawaran Multi-Panel":
   - List panel yang dipilih (preview)
   - Warning kalau customer info beda (tapi tetap bisa lanjut)
   - Input **Nomor Surat** (auto-prefill `EST-COMBINED-YYYYMMDD-HHmm`)
   - Input **Ongkos Kirim Gabungan** (default 0)
   - Pilih format: **Surat Formal** (kop surat resmi) atau **Modern**
   - Klik **Generate PDF**
4. PDF langsung dibuka di viewer default. Dialog tanya apakah mau simpan
   permanen — kalau ya, pilih lokasi & nama file.

Customer info (Nama, Perusahaan, Alamat) di surat diambil dari panel
**pertama** yang dipilih. Order panel di PDF mengikuti urutan baris di grid.

---

## Yang TIDAK berubah / TIDAK rusak

- Export PDF single-panel (tombol "📄 Export PDF" lama) → tetap jalan,
  format identik dengan sebelumnya.
- Export/Import CSV → tidak disentuh.
- Model `Estimation` / `EstimationDetail` → tidak disentuh.
- Migrasi DB → tidak ada migrasi baru.
- Test existing 105 → harusnya tetap pass (kolom checkbox dgv adalah
  pure UI change, tidak ada test yang touch UI).

---

## Pertanyaan / catatan untuk user

1. **Nomor surat default**: pakai pattern `EST-COMBINED-YYYYMMDD-HHmm`.
   Kalau perlu pattern lain (mis. format manual yang user tulis sendiri
   seperti `136.Rev1/PR.BDG/V/2026`) — user tinggal ganti di textbox dialog.
2. **Ongkir per panel di-IGNORE**: kalau ternyata user butuh tetap mempertahankan
   ongkir per panel + tambah ongkir gabungan, beri tahu — saat ini hanya
   ongkir gabungan yang dipakai. Asumsi: surat gabungan = 1 pengiriman.
3. **Format CSV untuk multi-panel**: belum dibuat. Saat ini multi-panel
   hanya ada di flow PDF. Kalau perlu export CSV gabungan, tinggal request.
4. **Order panel di PDF**: mengikuti urutan baris di grid (terbaru → terlama
   karena grid sort desc by CreatedDate). Belum ada UI untuk reorder.
   Workaround: user uncheck & re-check dalam urutan yang diinginkan
   *(saat ini tidak applied karena GetCheckedEstimationIds() iterasi
   urutan grid). Kalau user butuh reorder UI, beri tahu.*
