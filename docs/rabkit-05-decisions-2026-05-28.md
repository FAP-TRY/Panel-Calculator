# RAB Kit — Strategic Decisions

_Decided 2026-05-28 oleh pemilik PT Tritunggal Swarna_

## 1. Brand Identity — Generic Edition

- **Brand baru**: **RAB Cepat**
- Custom Edition tetap pakai brand TTS (Kalkulator Panel Tritunggal Swarna)
- Domain target: `rabcepat.id` (atau `rabcepat.com` kalau .id taken — perlu dicek)
- App display name: "RAB Cepat" (tagline TBD — saran: "Estimasi Cepat, Penawaran Tepat")
- Single codebase, branding-swap via `RabCepat.Branding.dll` (parallel ke `Panel.Branding.dll` TTS)

## 2. License Model — Both Recurring + Lifetime

| Model | Untuk siapa | Pricing kasar (TBD) |
|-------|-------------|---------------------|
| **Subscription Recurring** | SMB yang butuh library update terus | Free trial → Basic (Rp 99rb/bln) → Pro (Rp 299rb/bln) → Business (Rp 599rb/bln) |
| **Lifetime One-Time** | User yang prefer beli sekali, library snapshot | Lifetime Premium (Rp 2.5jt - Rp 5jt — TBD setelah validate market) |

**Perbedaan utama:**
- Subscription: dapat **library update bulanan** selama aktif (kalau berhenti bayar → library frozen di versi terakhir)
- One-time: dapat **library snapshot** saat beli; kalau mau update library berikutnya → beli pack baru per item di marketplace

## 3. Payment Gateway — Xendit

- Pilihan: **Xendit** (Indonesia-native)
- Support: VA (BCA/Mandiri/BNI/BRI/Permata), QRIS, Credit Card, e-wallet (DANA/OVO/GoPay/ShopeePay/LinkAja)
- Setup yang user harus lakukan (di luar coding):
  - Daftar akun Xendit business
  - KYC: NPWP business, SIUP/NIB, rekening business
  - Webhook URL untuk activation/renewal
  - Test mode dulu sebelum production

## 4. Library Pack Pricing — 3 Tier

| Tier | Konten | Update | Price |
|------|--------|--------|-------|
| **Free** | ~50 SKU starter per-industry (sample) + watermark di PDF/Word output | Tidak ada update | Rp 0 (untuk trial 14 hari → unlock kalau upgrade) |
| **Standard** | 200-500 SKU per-industry, dari pricelist publik (mis. Schneider/Himel public catalog) | Quarterly (per 3 bulan) | Rp 199rb/tahun ATAU termasuk di subscription Pro |
| **Premium** | Full catalog (1000+ SKU per-industry, harga dealer/negotiated) | **Monthly** untuk subscription, **snapshot only** untuk one-time | Subscription Business OR Rp 1.5jt-Rp 3jt one-time |

Standard kalau bagi user yang sudah punya akses pricelist sendiri tapi mau tool. Premium kalau user mau tool + tidak mau pusing maintain catalog.

## 5. Eksekusi — Solo (12 Minggu)

- Pemilik = solo developer (dibantu Claude)
- Estimasi: 12 minggu dari Week 1
- Pemilik tugas non-coding:
  - **Week 4**: dark-launch dengan 1-2 admin TTS (soak test)
  - **Week 7**: setup akun Xendit + KYC + webhook
  - **Week 7**: beli domain `rabcepat.id` ($ ~Rp 250rb/tahun)
  - **Week 8**: bikin minimal landing page copy (saya bisa draft)
  - **Week 9-10**: cari leads untuk customer Custom Edition #2 (carrosserie/MEP)
  - **Week 10**: kurasi atau partner untuk konten library Konstruksi Sipil
  - **Ongoing**: respond ke trial signup, support email

## Pilihan industri pertama untuk Generic Edition Standard library

Direkomendasikan **Panel Listrik** sebagai industri pertama karena:
- Pemilik sudah punya domain expertise (PT TTS sudah jalan 8.000+ SKU)
- Library Free/Standard bisa di-derive dari katalog publik (Schneider/Himel/HOWIG website)
- Premium = harga dealer TTS (anonymized, no NPWP-binding)
- Cross-sell potential: customer Generic yang grow → upgrade ke Custom Edition khusus mereka

## Decisions yang masih open

- Tagline final "RAB Cepat" — saya draft 5 opsi di Week 5 untuk pilih
- Logo "RAB Cepat" — perlu desainer (Fiverr Rp 500rb-1jt) atau pakai AI generated
- Email domain untuk transactional (`noreply@rabcepat.id` atau `support@rabcepat.id`)
- Trial duration default: 14 hari atau 20 estimasi (mana yang lebih dulu)
- Refund policy
