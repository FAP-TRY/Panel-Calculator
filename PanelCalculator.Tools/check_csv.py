import csv, sys
sys.stdout.reconfigure(encoding='utf-8')

rows = list(csv.DictReader(open(r'C:\Projects\Panel Calculator\PanelCalculator.Tools\products_all.csv', encoding='utf-8-sig')))
print(f'Total rows: {len(rows)}')

def show(label, data, n=5):
    print(f'\n=== {label} ===')
    for r in data[:n]:
        ref  = r['reference_code']
        name = r['product_name'][:55]
        price = int(r['price'])
        ss   = r['stock_status']
        print(f'  {ref:15s} | {name:55s} | Rp {price:>12,} | SS={ss}')

show('Schneider MCCB', [x for x in rows if x['vendor']=='Schneider Electric' and x['category']=='MCCB'])
show('Schneider Kontaktor', [x for x in rows if x['vendor']=='Schneider Electric' and x['category']=='Kontaktor'])
show('Chint MCB', [x for x in rows if x['vendor']=='Chint' and x['category']=='MCB'])
show('Chint MCCB', [x for x in rows if x['vendor']=='Chint' and x['category']=='MCCB'])
show('Chint ACB', [x for x in rows if x['vendor']=='Chint' and x['category']=='ACB'])
show('Schneider ACB', [x for x in rows if x['vendor']=='Schneider Electric' and x['category']=='ACB'])
