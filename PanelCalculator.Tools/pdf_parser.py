#!/usr/bin/env python3
"""
PDF Parser untuk Schneider Electric Pricelist
Extracts product data dan menghasilkan CSV untuk import ke database
"""

import re
import csv
import sys
from pathlib import Path

try:
    import pdfplumber
except ImportError:
    print("Error: pdfplumber tidak terinstall. Jalankan: pip install pdfplumber")
    sys.exit(1)


def extract_products_from_pdf(pdf_path):
    """Extract products dari PDF dan return list of dicts"""
    products = []

    # Category mapping berdasarkan analisis PDF
    category_keywords = {
        'MCB': ['Miniature Circuit Breaker', 'MCB', 'iK60', 'iC60', 'C120', 'C60H-DC'],
        'RCCB': ['RCCB', 'ELCB', 'iID', 'iDPN'],
        'Box': ['MCB Box', 'Mini Pragma', 'Pragma'],
        'Busbar': ['Busbar', 'Busbar Sisir', 'Linergy'],
        'Surge Arrester': ['Surge Arrester', 'iPRD', 'iPF'],
        'Accessories': ['Aksesori', 'Kontaktor', 'Relay', 'Switch', 'iTL', 'iRT'],
        'Other': []
    }

    try:
        with pdfplumber.open(pdf_path) as pdf:
            # Extract all tables from all pages
            all_products = []

            for page_num, page in enumerate(pdf.pages):
                tables = page.extract_tables()
                if tables:
                    for table in tables:
                        if table:
                            # Process each row in the table
                            for row in table:
                                if row and len(row) >= 3:
                                    # Filter out header rows
                                    if any(keyword in str(row[0] or '').lower()
                                           for keyword in ['referensi', 'harga', 'pengenal', 'jumlah']):
                                        continue

                                    # Try to extract reference code and price
                                    ref_code = str(row[0] or '').strip() if row[0] else None
                                    price_str = None

                                    # Find price (usually in last few columns)
                                    for col in reversed(row):
                                        if col:
                                            col_str = str(col).strip()
                                            # Check if it looks like a price (numbers with dots)
                                            if re.match(r'^\d{1,}\.?\d*$', col_str.replace('.', '')):
                                                price_str = col_str
                                                break

                                    # Validate reference code format
                                    if ref_code and len(ref_code) > 3 and not any(
                                        c in ref_code.lower() for c in ['kutub', 'pengenal', 'spesif']):

                                        # Extract product name and specifications
                                        product_info = ' '.join(str(col or '').strip() for col in row[1:-1] if col)

                                        if product_info and price_str:
                                            all_products.append({
                                                'reference_code': ref_code,
                                                'product_info': product_info,
                                                'price': price_str,
                                                'page': page_num + 1
                                            })

            # Parse extracted data
            for item in all_products:
                category = 'Other'
                product_info_lower = item['product_info'].lower()

                for cat, keywords in category_keywords.items():
                    if any(kw.lower() in product_info_lower for kw in keywords):
                        category = cat
                        break

                try:
                    # Parse price (remove dots used as thousands separator)
                    price_clean = item['price'].replace('.', '')
                    price = int(price_clean) if price_clean else 0
                except:
                    price = 0

                product = {
                    'category': category,
                    'reference_code': item['reference_code'],
                    'product_name': item['product_info'][:200],  # Limit length
                    'specifications': item['product_info'][200:] if len(item['product_info']) > 200 else '',
                    'price': price,
                    'stock_status': 1,  # Default: Stock
                    'vendor': 'Schneider Electric'
                }

                # Deduplicate by reference code
                if not any(p['reference_code'] == product['reference_code'] for p in products):
                    products.append(product)

        return products

    except Exception as e:
        print(f"Error parsing PDF: {e}")
        return []


def save_to_csv(products, output_path):
    """Save extracted products to CSV"""
    if not products:
        print("No products to save")
        return False

    try:
        with open(output_path, 'w', newline='', encoding='utf-8') as f:
            writer = csv.DictWriter(f, fieldnames=[
                'category', 'reference_code', 'product_name',
                'specifications', 'price', 'stock_status', 'vendor'
            ])
            writer.writeheader()
            writer.writerows(products)

        print(f"✓ Saved {len(products)} products to {output_path}")
        return True
    except Exception as e:
        print(f"Error saving CSV: {e}")
        return False


def main():
    if len(sys.argv) < 2:
        print("Usage: python pdf_parser.py <pdf_path> [output_csv_path]")
        print("Example: python pdf_parser.py Schneider.pdf products.csv")
        sys.exit(1)

    pdf_path = sys.argv[1]
    output_path = sys.argv[2] if len(sys.argv) > 2 else "products.csv"

    if not Path(pdf_path).exists():
        print(f"Error: File not found: {pdf_path}")
        sys.exit(1)

    print(f"Parsing PDF: {pdf_path}")
    products = extract_products_from_pdf(pdf_path)

    if products:
        save_to_csv(products, output_path)
        print(f"\n✓ Successfully extracted {len(products)} products")
    else:
        print("\n✗ No products extracted")
        sys.exit(1)


if __name__ == "__main__":
    main()
