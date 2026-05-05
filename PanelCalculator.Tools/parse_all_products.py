#!/usr/bin/env python3
"""
PDF Parser Komprehensif - Schneider Electric & Chint
Mengekstrak data produk dari semua PDF daftar harga ke satu CSV.
"""

import re
import csv
import sys
import os
from pathlib import Path
from collections import Counter

try:
    import pdfplumber
except ImportError:
    print("Error: pdfplumber tidak terinstall. Jalankan: python -m pip install pdfplumber")
    sys.exit(1)

# ===========================================================================
# KONFIGURASI FILE PDF
# ===========================================================================
BASE_DIR = Path(r"C:\Projects\Panel Calculator\PanelCalculator.Tools\PDF Daftar Harga")
OUTPUT_CSV = Path(r"C:\Projects\Panel Calculator\PanelCalculator.Tools\products_all.csv")

SCHNEIDER_DIR = BASE_DIR / "1. Schneider"
CHINT_DIR = BASE_DIR / "2. Chint"

SCHNEIDER_FILES = [
    ("1. MCB.pdf",                    "MCB",                    "Schneider Electric"),
    ("2. ELCB, RCCB, RCBO.pdf",       "RCCB",                   "Schneider Electric"),
    ("3. SURGE ARRESTER.pdf",         "Surge Arrester",         "Schneider Electric"),
    ("4. KONTAKTOR 1P.pdf",           "Kontaktor",              "Schneider Electric"),
    ("5. MCCB GoPact, CVS, EZC.pdf",  "MCCB",                   "Schneider Electric"),
    ("5. MCCB NSXm, NSX, NS.pdf",     "MCCB",                   "Schneider Electric"),
    ("6. ACB.pdf",                    "ACB",                    "Schneider Electric"),
    ("7. KONTAKTOR.pdf",              "Kontaktor",              "Schneider Electric"),
    ("8. MOTOR CIRCUIT BREAKER.pdf",  "Motor Circuit Breaker",  "Schneider Electric"),
]

CHINT_FILES = [
    ("1. MCB.pdf",                             "MCB",            "Chint"),
    ("2. MCCB.pdf",                            "MCCB",           "Chint"),
    ("3. ACB.pdf",                             "ACB",            "Chint"),
    ("4. Kontaktor, VSD.pdf",                  "Kontaktor",      "Chint"),
    ("5. ATS, LBS, Capacitor Bank, PFC.pdf",   "ATS",            "Chint"),
]

CSV_FIELDS = ["category", "reference_code", "product_name", "specifications", "price", "stock_status", "vendor"]

# ===========================================================================
# UTILITY
# ===========================================================================

def parse_price(price_str: str) -> int:
    """Konversi '1.234.567' atau '1.234.567,99' ke integer."""
    s = str(price_str).strip()
    # Hapus koma desimal dan apapun setelahnya (harga tidak pakai desimal di dokumen ini)
    s = re.sub(r'[,\.](\d{1,2})$', '', s)
    # Hapus semua titik/koma sebagai pemisah ribuan
    s = re.sub(r'[\.,]', '', s)
    s = re.sub(r'[^\d]', '', s)
    return int(s) if s else 0


def clean_text(t: str) -> str:
    """Bersihkan teks dari karakter tidak perlu."""
    if not t:
        return ''
    t = re.sub(r'\s+', ' ', t).strip()
    # Hapus karakter khusus yang sering muncul dari PDF
    t = t.replace('●', '').replace('■', '').replace('◾', '').replace('⚬', '').strip()
    return t


def make_ref_code(vendor_prefix: str, category: str, product_name: str, counter: list) -> str:
    """Generate ref code jika tidak ada kode yang jelas."""
    counter[0] += 1
    # Bersihkan nama
    name_part = re.sub(r'[^A-Za-z0-9]', '', product_name.upper())[:8]
    return f"{vendor_prefix}_{category[:4].upper()}_{name_part}_{counter[0]:04d}"


# ===========================================================================
# POLA REGEX - SCHNEIDER
# ===========================================================================

# Ref code Schneider: 6-14 karakter alfanumerik kapital, minimal 2 huruf + 2 angka
SCHNEIDER_REF_RE = re.compile(r'\b([A-Z][A-Z0-9]{5,13})\b')

# Pola harga Indonesia: angka dengan titik sebagai pemisah ribuan
PRICE_RE = re.compile(r'((?:\d{1,3}\.)+\d{3}(?:\.\d{3})*)')

# Pola baris produk Schneider: nama_spec REF HARGA SS
SCHNEIDER_LINE_RE = re.compile(
    r'^(.*?)\s+([A-Z][A-Z0-9]{5,13})\s+((?:\d{1,3}\.)+\d{3})\s+([12])\s*$'
)

# Pola lebih fleksibel: REF di tengah/akhir, harga, SS
SCHNEIDER_LINE_RE2 = re.compile(
    r'([A-Z][A-Z0-9]{5,13})\s+((?:\d{1,3}\.)+\d{3})\s+([12])\s*$'
)

SKIP_WORDS = {
    'HDCFD', 'RCCB', 'ELCB', 'RCBO', 'MCB', 'SNI', 'IEC', 'DIN', 'NULL',
    'PPN', 'SLIM', 'VIGI', 'AFDD', 'MCCB', 'ACB', 'PPCTR', 'STOCK', 'INDENT',
    'HARGA', 'SEBELUM', 'REFERENSI', 'STATUS', 'PENGENAL', 'JUMLAH', 'KUTUB',
    'DESKRIPSI', 'TIPE', 'MODUL', 'LEBAR', 'PANJANG', 'DIMENSI', 'KONTAK',
    'BANTU', 'SETTING', 'WAKTU', 'UNTUK', 'PADA', 'DARI', 'SAMPAI', 'DENGAN',
    'ATS', 'LBS', 'VSD', 'SPD', 'NXU', 'RFI', 'EMC', 'LED', 'UPS',
    'LAEN', 'LADN',  # Skip 4-5 char yang bukan ref code
}

def is_valid_schneider_ref(code: str) -> bool:
    """Validasi ref code Schneider."""
    if len(code) < 6 or len(code) > 14:
        return False
    if code in SKIP_WORDS:
        return False
    has_letter = any(c.isalpha() for c in code)
    has_digit = any(c.isdigit() for c in code)
    if not (has_letter and has_digit):
        return False
    # Harus mulai dengan huruf
    if not code[0].isalpha():
        return False
    # Jangan biarkan kata-kata umum bahasa Indonesia
    lower = code.lower()
    for skip in ['jumlah', 'harga', 'sebelum', 'referensi']:
        if skip in lower:
            return False
    return True


# ===========================================================================
# MAPPING KATEGORI & NAMA PRODUK - SCHNEIDER
# ===========================================================================

# Prefix ref code -> (category, product_family)
SCHNEIDER_PREFIX_MAP = [
    # Domae
    ('DOMF',    'MCB',              'MCB Domae'),
    ('DOMR',    'RCCB',             'RCCB Domae'),
    ('DOMH',    'MCB Box',          'MCB Box Domae'),
    ('DOMD',    'RCCB',             'RCBO Slim Domae'),
    ('DOML',    'Surge Arrester',   'Surge Arrester Domae PF'),
    # Easy9
    ('EZ9F54',  'MCB',              'MCB Easy9 4.5kA'),
    ('EZ9R04',  'RCCB',             'RCCB Easy9'),
    ('EZ9XPH',  'Busbar',           'Busbar Sisir Easy9'),
    # Acti9 MCB
    ('A9K14',   'MCB',              'MCB iK60a 4.5kA'),
    ('A9K24',   'MCB',              'MCB iK60N 6kA'),
    ('A9K27',   'MCB',              'MCB iK60N 6kA'),
    ('A9F74',   'MCB',              'MCB iC60N 6kA'),
    ('A9F84',   'MCB',              'MCB iC60H 10kA'),
    ('A9F85',   'MCB',              'MCB iC60H Kurva D 10kA'),
    ('A9F94',   'MCB',              'MCB iC60L'),
    ('A9N183',  'MCB',              'MCB C120N 10kA'),
    ('A9N184',  'MCB',              'MCB C120H 15kA'),
    ('A9N615',  'MCB',              'MCB C60H-DC'),
    ('A9N61',   'MCB',              'MCB C60H-DC'),
    ('A9N18',   'MCB',              'MCB C120N/H'),
    # Acti9 RCCB/RCBO
    ('A9R10',   'RCCB',             'RCCB iID 10mA'),
    ('A9R71',   'RCCB',             'RCCB iID 30mA'),
    ('A9R74',   'RCCB',             'RCCB iID 300mA'),
    ('A9R14',   'RCCB',             'RCCB iID'),
    ('A9D31',   'RCCB',             'RCBO iDPN N Vigi 30mA'),
    ('A9D41',   'RCCB',             'RCBO iDPN N Vigi 300mA'),
    ('A9V41',   'RCCB',             'Modul Vigi 30mA'),
    ('A9V44',   'RCCB',             'Modul Vigi 300mA'),
    ('A9V15',   'RCCB',             'Modul Vigi 300mA Selektif'),
    # Acti9 Surge Arrester
    ('A9L',     'Surge Arrester',   'Surge Arrester Acti9'),
    # Acti9 Accessories
    ('A9XPH',   'Busbar',           'Busbar Linergy Acti9'),
    ('A9C',     'Kontaktor',        'Kontaktor Acti9'),
    ('A9TA',    'Accessories',      'AFDD Arc Fault Acti9'),
    ('A9TD',    'Accessories',      'AFDD Arc Fault Acti9'),
    ('A9S',     'Accessories',      'Switch Acti9'),
    ('A9A',     'Accessories',      'Aksesori Acti9'),
    ('A9Z',     'Accessories',      'Aksesori Acti9'),
    # MCB Box Domae
    ('MIP',     'MCB Box',          'Box Mini Pragma'),
    # Surge Arrester (lain)
    ('DOMLS',   'Surge Arrester',   'Surge Arrester Domae'),
    # Kontaktor TeSys
    ('LC1E0',   'Kontaktor',        'Kontaktor Easy TeSys 3P'),
    ('LC1E1',   'Kontaktor',        'Kontaktor Easy TeSys 3P'),
    ('LC1E2',   'Kontaktor',        'Kontaktor Easy TeSys 3P'),
    ('LC1E3',   'Kontaktor',        'Kontaktor Easy TeSys 3P'),
    ('LC1E4',   'Kontaktor',        'Kontaktor Easy TeSys 3P'),
    ('LC1E5',   'Kontaktor',        'Kontaktor Easy TeSys 3P'),
    ('LC1E6',   'Kontaktor',        'Kontaktor Easy TeSys 3P'),
    ('LC1E00',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E09',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E12',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E18',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E25',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E38',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E40',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E65',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E80',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1E95',  'Kontaktor',        'Kontaktor Easy TeSys 4P'),
    ('LC1K',    'Kontaktor',        'Kontaktor TeSys K'),
    ('LP1K',    'Kontaktor',        'Kontaktor TeSys K DC'),
    ('LC1D09',  'Kontaktor',        'Kontaktor TeSys Deca 9A'),
    ('LC1D12',  'Kontaktor',        'Kontaktor TeSys Deca 12A'),
    ('LC1D18',  'Kontaktor',        'Kontaktor TeSys Deca 18A'),
    ('LC1D25',  'Kontaktor',        'Kontaktor TeSys Deca 25A'),
    ('LC1D32',  'Kontaktor',        'Kontaktor TeSys Deca 32A'),
    ('LC1D38',  'Kontaktor',        'Kontaktor TeSys Deca 38A'),
    ('LC1D40',  'Kontaktor',        'Kontaktor TeSys Deca 40A'),
    ('LC1D50',  'Kontaktor',        'Kontaktor TeSys Deca 50A'),
    ('LC1D65',  'Kontaktor',        'Kontaktor TeSys Deca 65A'),
    ('LC1D80',  'Kontaktor',        'Kontaktor TeSys Deca 80A'),
    ('LC1D95',  'Kontaktor',        'Kontaktor TeSys Deca 95A'),
    ('LC1D11',  'Kontaktor',        'Kontaktor TeSys Deca 115A'),
    ('LC1D15',  'Kontaktor',        'Kontaktor TeSys Deca 150A'),
    ('LC1DT',   'Kontaktor',        'Kontaktor TeSys Deca 4P'),
    ('LC1G',    'Kontaktor',        'Kontaktor TeSys Giga'),
    # Relay beban lebih
    ('LRE',     'Accessories',      'Relay Beban Lebih Easy TeSys'),
    ('LRD',     'Accessories',      'Relay Beban Lebih TeSys Deca'),
    ('LR2K',    'Accessories',      'Relay Beban Lebih TeSys K'),
    ('LR9G',    'Accessories',      'Relay Beban Lebih TeSys Giga'),
    ('LR97D',   'Accessories',      'EOCR Electronic Overload Relay'),
    ('LT47',    'Accessories',      'EOCR LT47'),
    # Aksesori kontaktor
    ('LAEN',    'Accessories',      'Kontak Bantu Easy TeSys'),
    ('LADN',    'Accessories',      'Kontak Bantu TeSys Deca'),
    ('LA1KN',   'Accessories',      'Kontak Bantu TeSys K'),
    ('LAD8N',   'Accessories',      'Kontak Bantu TeSys Deca Samping'),
    ('LADT',    'Accessories',      'Kontak Blok Tunda Waktu'),
    ('LADR',    'Accessories',      'Kontak Blok Tunda Waktu'),
    ('LADS',    'Accessories',      'Kontak Blok Tunda Waktu'),
    ('LAET',    'Accessories',      'Kontak Blok Tunda Waktu'),
    ('LXD',     'Accessories',      'Koil Cadangan Kontaktor'),
    ('LX1D',    'Accessories',      'Koil Cadangan Kontaktor'),
    ('LAD',     'Accessories',      'Aksesori TeSys'),
    ('LAG',     'Accessories',      'Aksesori TeSys Giga'),
    ('GV2AF',   'Accessories',      'Wiring Kit Motor Starter'),
    ('GV3S',    'Accessories',      'S-Busbar Motor Starter'),
    ('LAD9R',   'Accessories',      'Reverser Kit'),
    ('LAD9SD',  'Accessories',      'Star Delta Kit'),
    ('LAD91',   'Accessories',      'Star Delta Kit'),
    ('LAD93',   'Accessories',      'Star Delta Kit'),
    ('LAD96',   'Accessories',      'Wiring Kit'),
    # Motor Circuit Breaker
    ('GV2',     'Motor Circuit Breaker', 'Motor Circuit Breaker GV2'),
    ('GV3',     'Motor Circuit Breaker', 'Motor Circuit Breaker GV3'),
    ('GV4',     'Motor Circuit Breaker', 'Motor Circuit Breaker GV4'),
    ('GV5',     'Motor Circuit Breaker', 'Motor Circuit Breaker GV5'),
    ('GV7',     'Motor Circuit Breaker', 'Motor Circuit Breaker GV7'),
    # MCCB
    ('LV4',     'MCCB',             'MCCB Compact NSXm/NSX'),
    ('LV5',     'MCCB',             'MCCB Compact NSX/NS'),
    ('LV6',     'MCCB',             'MCCB Compact NS'),
    ('LV1',     'MCCB',             'MCCB EZC'),
    ('EZC',     'MCCB',             'MCCB EZC'),
    # ACB
    ('NW',      'ACB',              'ACB MasterPact NW'),
    ('NT',      'ACB',              'ACB MasterPact NT'),
    ('MTZ',     'ACB',              'ACB MasterPact MTZ'),
    # Kontaktor 1P (Acti9)
    ('A9MEM',   'Kontaktor',        'Kontaktor Acti9 iATL 1P'),
    ('A9S',     'Accessories',      'Switch Disconnector Acti9'),
]


def get_schneider_meta(ref: str) -> tuple:
    """Kembalikan (category, product_family) berdasarkan prefix ref code."""
    ref_upper = ref.upper()
    for prefix, category, family in sorted(SCHNEIDER_PREFIX_MAP, key=lambda x: len(x[0]), reverse=True):
        if ref_upper.startswith(prefix.upper()):
            return category, family
    return None, None


# ===========================================================================
# PARSER SCHNEIDER
# ===========================================================================

def parse_schneider_pdf(pdf_path: Path, default_category: str, vendor: str) -> list:
    """Parse satu PDF Schneider."""
    products = []
    seen_refs = set()

    try:
        with pdfplumber.open(str(pdf_path)) as pdf:
            for page_num, page in enumerate(pdf.pages):
                text = page.extract_text() or ''
                lines = text.split('\n')

                for line in lines:
                    line = line.strip()
                    if not line or len(line) < 10:
                        continue

                    # Coba pola utama: apapun REF HARGA SS
                    m = SCHNEIDER_LINE_RE2.search(line)
                    if not m:
                        continue

                    ref_code = m.group(1)
                    price_str = m.group(2)
                    ss_str = m.group(3)

                    if not is_valid_schneider_ref(ref_code):
                        continue
                    if ref_code in seen_refs:
                        continue

                    price = parse_price(price_str)
                    if price < 1000:
                        continue

                    # Ambil prefix sebelum ref code sebagai spec
                    idx = line.rfind(ref_code)
                    spec_raw = line[:idx].strip() if idx > 0 else ''
                    spec_clean = clean_text(spec_raw)

                    # Tentukan kategori dan nama produk dari ref code
                    cat, family = get_schneider_meta(ref_code)
                    if not cat:
                        cat = default_category
                    if not family:
                        family = ref_code[:4] + ' Series'

                    # Buat product name
                    if spec_clean and len(spec_clean) < 80:
                        product_name = f"{family} - {spec_clean}"
                    else:
                        product_name = family

                    # Spec: gabungkan spec yang ditemukan
                    specifications = spec_clean[:200] if spec_clean else ''

                    seen_refs.add(ref_code)
                    products.append({
                        'category':       cat,
                        'reference_code': ref_code,
                        'product_name':   product_name[:250],
                        'specifications': specifications,
                        'price':          price,
                        'stock_status':   int(ss_str),
                        'vendor':         vendor,
                    })

    except Exception as e:
        print(f"  [ERROR] Gagal parse {pdf_path.name}: {e}")
        return []

    return products


# ===========================================================================
# POLA REGEX - CHINT
# ===========================================================================

# Chint: ref code = item code (numerik 6 digit) atau nama model (alfanumerik)
# Format tabel Chint lebih bervariasi

# Item code Chint berupa 6 digit angka (dalam kolom Code)
CHINT_CODE_RE = re.compile(r'\b(\d{6})\b')

# Pola baris Chint dengan item name, code, harga, qty, ss
# Item    Code  Price(Rp)  QTY/CTN  SS
CHINT_LINE_RE = re.compile(
    r'(.+?)\s+(\d{6})\s+([\d,\.]+(?:\.\d{3})*)\s+\d+\s+([12])\s*$'
)

# Nama model Chint (alfanumerik dengan tanda hubung)
CHINT_MODEL_RE = re.compile(
    r'((?:NXB|NXH|NXM|NXC|NXA|NXU|NXT|NCH|NCL|NBS|NLB|NPC|NFC|NRB|NR|NC|CPS|'
    r'NXBLE|NB3LE|NB4LE|NM8|NM10|AH3|DW1|DZ47|DZ10|DZ20|'
    r'SRD|VFD|NVF|ATV|NFC|NC2|NC3|NC6|NC7|CJ20|CJ40|CJX|'
    r'NTS|NPS|NRDB|NFB|NDW|FH|NL|NQ)[A-Z0-9\-\.]+)',
    re.IGNORECASE
)

def is_valid_chint_ref(code: str) -> bool:
    """Validasi kode Chint (6 digit numerik atau model alfanumerik)."""
    if re.match(r'^\d{6}$', code):
        return True
    if CHINT_MODEL_RE.match(code):
        return True
    return False


def parse_chint_line_advanced(line: str) -> tuple:
    """
    Parse satu baris Chint. Kembalikan (item_name, code, price, ss) atau None.
    Format: ITEM_NAME  CODE(6digit)  PRICE  QTY  SS
    """
    # Cari 6-digit code
    codes = CHINT_CODE_RE.findall(line)
    if not codes:
        return None

    # Ambil kode pertama yang realistis
    for code in codes:
        # Cari harga setelah code
        idx = line.find(code)
        after = line[idx + len(code):].strip()
        # Cari harga (angka dengan titik/koma)
        price_match = re.search(r'((?:\d{1,3}[\.,])+\d{3})', after)
        if not price_match:
            # Coba harga tanpa titik
            price_match2 = re.search(r'(\d{5,})', after)
            if price_match2:
                price_str = price_match2.group(1)
            else:
                continue
        else:
            price_str = price_match.group(1)

        # Cari SS (1 atau 2) di akhir baris
        ss_match = re.search(r'\b([12])\s*$', line.rstrip())
        if not ss_match:
            continue

        ss = ss_match.group(1)
        # Nama item = bagian sebelum code
        item_name = clean_text(line[:idx])

        price = parse_price(price_str)
        if price < 1000:
            continue

        return (item_name, code, price, int(ss))

    return None


def parse_chint_pdf(pdf_path: Path, default_category: str, vendor: str) -> list:
    """Parse satu PDF Chint."""
    products = []
    seen_refs = set()
    gen_counter = [0]

    try:
        with pdfplumber.open(str(pdf_path)) as pdf:
            current_model_name = ''
            current_category = default_category

            for page_num, page in enumerate(pdf.pages):
                # Coba extract tables dulu
                tables = page.extract_tables()
                page_text = page.extract_text() or ''

                # Deteksi model dari teks halaman
                model_matches = re.findall(
                    r'\b(NXB-63H?|NXB-125|NXBLE-63|NB3LE-AFD|NB4LE-AFD|'
                    r'NCH8|NXU-IIG|NM8[NS]?|NM10[NS]?|NM1[0-9]|'
                    r'NC2|NC3|NC6|NBS|NLB|NPC|NFC|NRB|NRC|NPS|'
                    r'NTS|NDW2?|NDW3?|AH3|DW1|NVF2|CPS|'
                    r'SRD\w*|NCD3|NCB|NDF|NFC|CJ[A-Z0-9]+)\b',
                    page_text
                )
                if model_matches:
                    current_model_name = model_matches[0]

                # Deteksi kategori dari teks halaman
                page_lower = page_text.lower()
                if any(k in page_lower for k in ['miniature circuit breaker', 'mcb', 'nxb-63', 'nxb-125']):
                    current_category = 'MCB'
                elif any(k in page_lower for k in ['mccb', 'molded case', 'nm8', 'nm10', 'ndw']):
                    current_category = 'MCCB'
                elif any(k in page_lower for k in ['air circuit breaker', 'acb', 'dw1', 'nw']):
                    current_category = 'ACB'
                elif any(k in page_lower for k in ['contactor', 'kontaktor', 'nc2', 'nc3', 'nch8', 'cj']):
                    current_category = 'Kontaktor'
                elif any(k in page_lower for k in ['surge arrester', 'spd', 'nxu']):
                    current_category = 'Surge Arrester'
                elif any(k in page_lower for k in ['rcbo', 'rccb', 'elcb', 'residual', 'nxble']):
                    current_category = 'RCCB'
                elif any(k in page_lower for k in ['automatic transfer', 'ats', 'nts', 'nps']):
                    current_category = 'ATS'
                elif any(k in page_lower for k in ['load break', 'lbs', 'nlb']):
                    current_category = 'LBS'
                elif any(k in page_lower for k in ['capacitor bank', 'power factor', 'pfc']):
                    current_category = 'Capacitor Bank'
                elif any(k in page_lower for k in ['variable speed drive', 'vsd', 'vfd', 'nvf', 'frequency']):
                    current_category = 'VSD'
                elif any(k in page_lower for k in ['afd', 'arc fault', 'nb3le', 'nb4le']):
                    current_category = 'RCCB'  # AFD termasuk RCCB category
                else:
                    current_category = default_category

                if tables:
                    for table in tables:
                        if not table:
                            continue
                        for row in table:
                            if not row:
                                continue
                            # Coba parse setiap baris tabel
                            row_text = ' '.join(str(c) for c in row if c is not None)
                            result = parse_chint_line_advanced(row_text)
                            if result:
                                item_name, code, price, ss = result
                                if code in seen_refs:
                                    continue
                                # Buat nama produk
                                if item_name and len(item_name) > 3:
                                    product_name = item_name[:250]
                                elif current_model_name:
                                    product_name = f"{current_model_name} - {code}"
                                else:
                                    product_name = f"Chint {current_category} {code}"

                                # Gunakan item name sebagai spec juga
                                specs = item_name[:200] if item_name else ''

                                # Ref code: gunakan kode 6-digit sebagai primary
                                ref_code = code

                                seen_refs.add(ref_code)
                                products.append({
                                    'category':       current_category,
                                    'reference_code': ref_code,
                                    'product_name':   product_name,
                                    'specifications': specs,
                                    'price':          price,
                                    'stock_status':   ss,
                                    'vendor':         vendor,
                                })
                else:
                    # Fallback: parse baris teks
                    lines = page_text.split('\n')
                    for line in lines:
                        line = line.strip()
                        if not line or len(line) < 10:
                            continue
                        result = parse_chint_line_advanced(line)
                        if result:
                            item_name, code, price, ss = result
                            if code in seen_refs:
                                continue
                            if item_name and len(item_name) > 3:
                                product_name = item_name[:250]
                            elif current_model_name:
                                product_name = f"{current_model_name} - {code}"
                            else:
                                product_name = f"Chint {current_category} {code}"

                            specs = item_name[:200] if item_name else ''
                            ref_code = code
                            seen_refs.add(ref_code)
                            products.append({
                                'category':       current_category,
                                'reference_code': ref_code,
                                'product_name':   product_name,
                                'specifications': specs,
                                'price':          price,
                                'stock_status':   ss,
                                'vendor':         vendor,
                            })

    except Exception as e:
        print(f"  [ERROR] Gagal parse {pdf_path.name}: {e}")
        return []

    return products


# ===========================================================================
# MAIN
# ===========================================================================

def main():
    all_products = []
    file_summary = []
    failed_files = []

    print("=" * 60)
    print("PDF Parser - Schneider Electric & Chint")
    print("=" * 60)

    # ---- Parse Schneider files ----
    print("\n[SCHNEIDER ELECTRIC]")
    for filename, default_cat, vendor in SCHNEIDER_FILES:
        pdf_path = SCHNEIDER_DIR / filename
        if not pdf_path.exists():
            print(f"  [SKIP] File tidak ditemukan: {filename}")
            failed_files.append((filename, "File tidak ditemukan"))
            continue

        print(f"  Parsing: {filename} ...", end=' ', flush=True)
        products = parse_schneider_pdf(pdf_path, default_cat, vendor)
        count = len(products)
        all_products.extend(products)
        file_summary.append((filename, count, vendor))
        print(f"{count} produk")

    # ---- Parse Chint files ----
    print("\n[CHINT]")
    for filename, default_cat, vendor in CHINT_FILES:
        pdf_path = CHINT_DIR / filename
        if not pdf_path.exists():
            print(f"  [SKIP] File tidak ditemukan: {filename}")
            failed_files.append((filename, "File tidak ditemukan"))
            continue

        print(f"  Parsing: {filename} ...", end=' ', flush=True)
        products = parse_chint_pdf(pdf_path, default_cat, vendor)
        count = len(products)
        all_products.extend(products)
        file_summary.append((filename, count, vendor))
        print(f"{count} produk")

    # ---- Deduplikasi berdasarkan reference_code ----
    print(f"\nTotal sebelum dedup: {len(all_products)} produk")
    seen = set()
    deduped = []
    for p in all_products:
        key = p['reference_code']
        if key not in seen:
            seen.add(key)
            deduped.append(p)

    print(f"Total setelah dedup: {len(deduped)} produk")

    # ---- Validasi: hapus baris dengan field wajib kosong ----
    valid = []
    for p in deduped:
        if not p.get('category'):
            continue
        if not p.get('product_name'):
            continue
        if not p.get('price') or p['price'] < 1000:
            continue
        valid.append(p)

    print(f"Total setelah validasi: {len(valid)} produk")

    # ---- Simpan CSV ----
    OUTPUT_CSV.parent.mkdir(parents=True, exist_ok=True)
    with open(str(OUTPUT_CSV), 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(valid)

    print(f"\nCSV disimpan ke: {OUTPUT_CSV}")

    # ---- Ringkasan per file ----
    print("\n" + "=" * 60)
    print("RINGKASAN PER FILE")
    print("=" * 60)
    for fname, count, vendor in file_summary:
        print(f"  {vendor:25s} | {fname:45s} | {count:4d} produk")

    # ---- Ringkasan per kategori per vendor ----
    print("\n" + "=" * 60)
    print("RINGKASAN PER KATEGORI PER VENDOR")
    print("=" * 60)
    from collections import defaultdict
    cat_vendor = defaultdict(int)
    for p in valid:
        cat_vendor[(p['vendor'], p['category'])] += 1

    current_vendor = None
    for (vendor, category), count in sorted(cat_vendor.items()):
        if vendor != current_vendor:
            print(f"\n  [{vendor}]")
            current_vendor = vendor
        print(f"    {category:30s}: {count:4d} produk")

    # ---- 5 baris pertama CSV ----
    print("\n" + "=" * 60)
    print("SAMPLE 5 BARIS PERTAMA CSV")
    print("=" * 60)
    print(f"{'category':20s} | {'reference_code':15s} | {'product_name':40s} | {'price':12s} | {'ss':2s} | {'vendor':20s}")
    print("-" * 120)
    for p in valid[:5]:
        print(f"{p['category']:20s} | {p['reference_code']:15s} | {p['product_name'][:40]:40s} | {p['price']:12,d} | {p['stock_status']:2d} | {p['vendor']:20s}")

    if failed_files:
        print("\n" + "=" * 60)
        print("FILE GAGAL DIPARSE")
        print("=" * 60)
        for fname, reason in failed_files:
            print(f"  {fname}: {reason}")

    print(f"\nSelesai! Total {len(valid)} produk di {OUTPUT_CSV.name}")


if __name__ == '__main__':
    main()
