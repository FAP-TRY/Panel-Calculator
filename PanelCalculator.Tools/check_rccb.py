import csv, sys
sys.stdout.reconfigure(encoding='utf-8')
rows = list(csv.DictReader(open('products_all.csv', encoding='utf-8-sig')))
rccb_a9r = [r for r in rows if r['vendor']=='Schneider Electric' and r['category']=='RCCB' and r['reference_code'].startswith('A9R')]
print(f'A9R RCCB count: {len(rccb_a9r)}\n')
for r in rccb_a9r[:25]:
    print(f"{r['reference_code']:12s} | {r['product_name'][:52]:52s} | {r['specifications']:25s} | SS={r['stock_status']}")
