#!/usr/bin/env python3
"""
PDF Parser untuk Schneider Electric Pricelist
Versi 2 - disesuaikan dengan format PDF asli
"""

import re
import csv
import sys
from pathlib import Path

try:
    import pdfplumber
except ImportError:
    print("Error: pdfplumber tidak terinstall. Jalankan: python -m pip install pdfplumber")
    sys.exit(1)

# Pattern: ref_code (6-12 huruf/angka kapital), harga (format ribuan titik), stock status (1 atau 2)
PRODUCT_LINE_RE = re.compile(
    r'^(.*?)\s+([A-Z][A-Z0-9]{5,11})\s+'      # anything + ref_code
    r'((?:\d{1,3}\.)+\d{3})\s+'               # harga: e.g. 120.500 atau 1.168.000
    r'([12])\s*$'                              # stock status
)

# Ref code harus mengandung setidaknya 2 huruf dan 2 angka
REF_CODE_RE = re.compile(r'^[A-Z][A-Z0-9]*[0-9][A-Z0-9]*$')

# Mapping keyword di header halaman -> kategori
PAGE_CATEGORY_MAP = [
    (['mcb box', 'mini pragma', 'pragma'], 'Box'),
    (['busbar', 'isobar', 'linergy'], 'Busbar'),
    (['rccb', 'elcb', 'rcbo', 'pengaman arus bocor', 'vigi'], 'RCCB'),
    (['surge arrester', 'iprd', 'ipf'], 'Surge Arrester'),
    (['kontaktor', 'relay', 'control', 'socket', 'switch', 'disconnection',
      'aksesori', 'acti9 afdd', 'arc fault', 'command'], 'Accessories'),
    (['miniature circuit breaker', 'mcb', 'ik60', 'ic60', 'easy9', 'domae'], 'MCB'),
]


def get_category_from_header(header_text: str) -> str:
    h = header_text.lower()
    for keywords, category in PAGE_CATEGORY_MAP:
        if any(k in h for k in keywords):
            return category
    return 'Other'


def parse_price(price_str: str) -> int:
    """Ubah '120.500' atau '1.168.000' ke integer"""
    return int(price_str.replace('.', ''))


def is_valid_ref_code(code: str) -> bool:
    """Validasi: ref code harus punya huruf dan angka, bukan kata biasa"""
    if not REF_CODE_RE.match(code):
        return False
    has_letter = any(c.isalpha() for c in code)
    has_digit = any(c.isdigit() for c in code)
    # Exclude kata-kata bahasa Indonesia yang kebetulan cocok polanya
    skip_words = {'HDCFD', 'RCCB', 'ELCB', 'RCBO', 'MCB', 'SNI', 'IEC', 'DIN',
                  'NULL', 'PPN', 'SLIM', 'VIGI', 'AFDD'}
    return has_letter and has_digit and code not in skip_words


def extract_products(pdf_path: str) -> list:
    products = []
    seen_refs = set()

    with pdfplumber.open(pdf_path) as pdf:
        for page_num, page in enumerate(pdf.pages):
            text = page.extract_text() or ''
            lines = text.split('\n')

            # Ambil header halaman (3 baris pertama yang tidak kosong)
            header_lines = [l.strip() for l in lines[:6] if l.strip()]
            header_text = ' '.join(header_lines[:3])
            category = get_category_from_header(header_text)

            # Cari nama produk utama dari header (contoh: "MCB Domae", "MCB iK60a", dll)
            product_family = extract_product_family(header_lines)

            # Scan baris untuk data produk
            for line in lines:
                line = line.strip()
                if not line:
                    continue

                m = PRODUCT_LINE_RE.match(line)
                if not m:
                    continue

                spec_part, ref_code, price_str, ss = m.group(1), m.group(2), m.group(3), m.group(4)

                if not is_valid_ref_code(ref_code):
                    continue
                if ref_code in seen_refs:
                    continue

                # Bersihkan spec_part
                spec_clean = clean_spec(spec_part)

                # Bangun nama produk
                product_name = build_product_name(product_family, spec_clean, ref_code)

                price = parse_price(price_str)
                if price < 1000:  # Harga tidak masuk akal
                    continue

                # Override kategori & nama keluarga berdasarkan kode referensi (lebih akurat)
                final_category = correct_category_by_refcode(ref_code) or category
                ref_family = get_product_family_from_ref(ref_code)
                effective_family = ref_family if ref_family else product_family
                product_name = build_product_name(effective_family, spec_clean, ref_code)

                seen_refs.add(ref_code)
                products.append({
                    'category':       final_category,
                    'reference_code': ref_code,
                    'product_name':   product_name,
                    'specifications': spec_clean,
                    'price':          price,
                    'stock_status':   int(ss),
                    'vendor':         'Schneider Electric',
                })

    return products


def extract_product_family(header_lines: list) -> str:
    """Ekstrak nama keluarga produk dari baris header"""
    for line in header_lines:
        # Baris yang berisi nama produk utama biasanya panjang dan informatif
        l = line.strip().lstrip('■ ').strip()
        if len(l) > 6 and not l.upper() == l:  # Bukan ALL CAPS
            # Hapus kata-kata umum
            for skip in ['HDCFD', 'dan', 'atau']:
                l = l.replace(skip, '').strip()
            if l:
                return l[:80]
    return header_lines[0].replace('HDCFD', '').strip() if header_lines else ''


def clean_spec(spec: str) -> str:
    """Bersihkan teks spesifikasi dari karakter sampah"""
    # Hapus digit/karakter yang hanya berupa nomor halaman atau referensi silang
    spec = re.sub(r'^\d+\s+', '', spec.strip())  # leading single number
    spec = re.sub(r'\s+', ' ', spec).strip()
    # Batasi panjang
    return spec[:150]


def build_product_name(family: str, spec: str, ref_code: str) -> str:
    """Gabungkan family + spec menjadi nama produk yang informatif"""
    # Jika family sudah ada spec di dalamnya, pakai family saja + spec penting
    parts = []
    if family:
        parts.append(family.strip())
    if spec and spec not in family:
        # Hanya tambahkan spec yang informatif (bukan hanya angka)
        if any(c.isalpha() for c in spec):
            parts.append(spec.strip())
        elif spec:
            # Spec berupa angka-angka (misal dimensi) - tetap tambahkan
            parts.append(spec.strip())
    name = ' - '.join(parts) if parts else ref_code
    return name[:250]


REF_PRODUCT_FAMILY = {
    'DOMF': 'MCB Domae',
    'DOMR': 'RCCB Domae',
    'DOMH': 'MCB Box Domae',
    'DOMD': 'RCBO Slim Domae',
    'DOML': 'Surge Arrester Domae',
    'EZ9F': 'MCB Easy9',
    'EZ9R': 'RCCB Easy9',
    'EZ9X': 'Busbar Sisir Easy9',
    'A9K1': 'MCB Acti9 iK60a 4.5kA',
    'A9K2': 'MCB Acti9 iK60N 6kA',
    'A9F7': 'MCB Acti9 iC60N 6kA',
    'A9F8': 'MCB Acti9 iC60H 10kA',
    'A9N1': 'MCB Acti9 iC60N',
    'A9P1': 'MCB Acti9 iC60H-DC',
    'A9R':  'RCCB/ID Acti9',
    'A9D':  'RCBO iDPN Acti9',
    'A9XP': 'Busbar Linergy Acti9',
    'A9L':  'Surge Arrester Acti9',
    'A9TA': 'AFDD Arc Fault Acti9',
    'A9TD': 'AFDD Arc Fault Acti9',
    'A9C':  'Kontaktor Acti9',
    'A9S':  'Switch Acti9',
    'A9A':  'Aksesori Acti9',
    'MIP':  'MCB Box Mini Pragma',
    'SEA9': 'Busbar Isobar',
}


def get_product_family_from_ref(ref: str) -> str:
    """Cari nama produk berdasarkan prefix ref code (lebih akurat dari header halaman)"""
    ref = ref.upper()
    for prefix in sorted(REF_PRODUCT_FAMILY, key=len, reverse=True):
        if ref.startswith(prefix):
            return REF_PRODUCT_FAMILY[prefix]
    return ''


def correct_category_by_refcode(ref: str) -> str | None:
    """Override kategori berdasarkan prefix kode referensi Schneider"""
    ref = ref.upper()
    rules = [
        # Domae series
        ('DOMF',  'MCB'),
        ('DOMR',  'RCCB'),
        ('DOMH',  'Box'),
        ('DOMD',  'RCCB'),    # RCBO Slim -> grouped under RCCB
        ('DOML',  'Surge Arrester'),
        # Easy9
        ('EZ9F',  'MCB'),
        ('EZ9R',  'RCCB'),
        ('EZ9X',  'Busbar'),
        # Acti9
        ('A9K',   'MCB'),
        ('A9F',   'MCB'),
        ('A9N',   'MCB'),
        ('A9P',   'MCB'),
        ('A9Q',   'MCB'),
        ('A9R',   'RCCB'),
        ('A9D',   'RCCB'),    # iDPN RCBO
        ('A9L',   'Surge Arrester'),
        ('A9XPH', 'Busbar'),
        ('A9TAB', 'Accessories'),  # AFDD
        ('A9TDF', 'Accessories'),  # AFDD
        ('A9C',   'Accessories'),  # Contactors
        ('A9S',   'Accessories'),  # Switches
        ('A9A',   'Accessories'),  # Other accessories
        ('A9Z',   'Accessories'),
        # Boxes
        ('MIP',   'Box'),
        # Isobar
        ('SEA9',  'Busbar'),
    ]
    for prefix, cat in rules:
        if ref.startswith(prefix):
            return cat
    return None


def save_csv(products: list, output_path: str) -> bool:
    if not products:
        return False
    with open(output_path, 'w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=[
            'category', 'reference_code', 'product_name',
            'specifications', 'price', 'stock_status', 'vendor'
        ])
        writer.writeheader()
        writer.writerows(products)
    return True


def main():
    if len(sys.argv) < 2:
        print("Usage: python pdf_parser.py <pdf_path> [output.csv]")
        sys.exit(1)

    pdf_path  = sys.argv[1]
    out_path  = sys.argv[2] if len(sys.argv) > 2 else 'products.csv'

    if not Path(pdf_path).exists():
        print(f"File tidak ditemukan: {pdf_path}")
        sys.exit(1)

    print(f"Membaca PDF: {pdf_path}")
    products = extract_products(pdf_path)

    if not products:
        print("Tidak ada produk ditemukan.")
        sys.exit(1)

    save_csv(products, out_path)

    # Tampilkan ringkasan per kategori
    from collections import Counter
    counts = Counter(p['category'] for p in products)
    print(f"\nBerhasil ekstrak {len(products)} produk:")
    for cat, n in sorted(counts.items()):
        print(f"  {cat:20s}: {n} produk")
    print(f"\nCSV disimpan ke: {out_path}")


if __name__ == '__main__':
    main()
