# Rabkit 02 Library Pack Design

_Generated 2026-05-28 dari workflow `generic-rab-calculator-plan` (4 agents)._

---

# Library Pack Schema — RAB Calculator Multi-Tenant SaaS

## 1. JSON Schema — Library Pack File

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://rab-calc.io/schemas/library-pack/v1.json",
  "title": "RAB Calculator Library Pack",
  "type": "object",
  "required": ["pack", "categories", "items"],
  "properties": {
    "pack": {
      "type": "object",
      "required": ["id", "name", "version", "industry", "publisher", "currency"],
      "properties": {
        "id":         { "type": "string", "pattern": "^[a-z0-9-]+$", "description": "Slug unik: 'tts-panel-listrik'" },
        "name":       { "type": "string", "description": "'Panel Listrik PT TTS'" },
        "version":    { "type": "string", "pattern": "^\\d{4}\\.Q[1-4](\\.\\d+)?$", "description": "'2026.Q1' atau '2026.Q1.2'" },
        "industry":   { "enum": ["panel-listrik", "trafo", "konstruksi-sipil", "mep", "carrosserie", "custom"] },
        "publisher":  { "type": "object", "properties": { "name": {"type":"string"}, "company": {"type":"string"}, "email": {"type":"string"} } },
        "currency":   { "type": "string", "enum": ["IDR", "USD", "SGD"], "default": "IDR" },
        "locale":     { "type": "string", "default": "id-ID" },
        "released":   { "type": "string", "format": "date" },
        "expires":    { "type": "string", "format": "date", "description": "Setelah tanggal ini app tampil warning 'pricelist outdated'" },
        "sources":    { "type": "array", "items": { "type": "object", "properties": {
                          "vendor": {"type":"string"}, "document": {"type":"string"},
                          "date": {"type":"string","format":"date"}, "sha256": {"type":"string"} } } },
        "checksum":   { "type": "string", "description": "SHA-256 dari items array — auto-generated" }
      }
    },

    "categories": {
      "type": "array",
      "description": "Tree kategori — path = 'Material/Elektrikal/MCB/1-pole'",
      "items": {
        "type": "object",
        "required": ["path"],
        "properties": {
          "path":     { "type": "string", "pattern": "^[A-Za-z0-9 \\-_/]+$" },
          "icon":     { "type": "string" },
          "unit":     { "type": "string", "description": "Default unit: 'pcs', 'm', 'm2', 'kg', 'set'" },
          "metadata": { "type": "object", "additionalProperties": true }
        }
      }
    },

    "vendors": {
      "type": "array",
      "items": { "type": "object", "properties": {
        "code": {"type":"string"}, "name":{"type":"string"},
        "tier": {"enum":["premium","standard","economy"]},
        "discount_chain": {"type":"string","description":"'40+10+5' untuk fallback price calc"}
      }}
    },

    "items": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["ref", "name", "category", "price"],
        "properties": {
          "ref":        { "type": "string", "description": "Kode produsen: 'C60N-1P-10A'" },
          "name":       { "type": "string" },
          "category":   { "type": "string", "description": "Match dengan categories[].path" },
          "vendor":     { "type": "string", "description": "Match dengan vendors[].code" },
          "specs":      { "type": "object", "additionalProperties": true, "description": "{amp:10, pole:1, kA:6}" },
          "unit":       { "type": "string", "description": "Override category unit" },
          "price":      {
            "oneOf": [
              { "type": "number", "description": "Harga fixed" },
              { "type": "object", "properties": {
                  "list":    { "type": "number" },
                  "net":     { "type": "number" },
                  "tiers":   { "type": "object", "description": "{'<100':1.0,'100-500':0.95,'>500':0.90}" },
                  "regions": { "type": "object", "description": "{'jakarta':1.0,'surabaya':1.05}" },
                  "formula": { "type": "string", "description": "JS-safe expr: 'list * length * (1 + waste/100)'" }
                }
              }
            ]
          },
          "stock":      { "enum": ["available", "indent", "discontinued"] },
          "image":      { "type": "string", "description": "Relative path dalam zip: 'images/c60n-1p.jpg'" },
          "tags":       { "type": "array", "items": {"type":"string"} },
          "valid_from": { "type": "string", "format": "date" },
          "valid_to":   { "type": "string", "format": "date" }
        }
      }
    },

    "formulas": {
      "type": "object",
      "description": "Reusable named formulas",
      "additionalProperties": {
        "type": "object",
        "properties": {
          "inputs":     { "type": "array", "items": { "type": "object", "properties": {
                            "name":{"type":"string"}, "label":{"type":"string"},
                            "type":{"enum":["number","string","select"]}, "unit":{"type":"string"},
                            "default":{}, "options":{"type":"array"} } } },
          "expression": { "type": "string", "description": "'price_per_kg * (length * width * thickness * 7.85)'" },
          "output_unit":{ "type": "string" }
        }
      }
    },

    "templates": {
      "type": "array",
      "description": "Pre-set RAB templates: 'Panel Distribusi 6-group', 'Rumah Type 36'",
      "items": { "type": "object", "properties": {
        "name": {"type":"string"},
        "items": { "type":"array", "items": { "type":"object",
                    "properties": { "ref":{"type":"string"}, "qty":{"type":"number"}, "notes":{"type":"string"} } } }
      }}
    }
  }
}
```

---

## 2. Mapping ke Existing DB Schema

Tabel saat ini (`Products`, `Estimations`, `EstimationDetails`, `Users`, `Settings`) perlu extension berikut:

### Extension Required

```sql
-- NEW: Library packs installed per tenant
CREATE TABLE LibraryPacks (
  Id              INTEGER PRIMARY KEY,
  PackId          TEXT NOT NULL,           -- 'tts-panel-listrik'
  Version         TEXT NOT NULL,           -- '2026.Q1'
  Industry        TEXT NOT NULL,
  PublisherName   TEXT,
  Currency        TEXT DEFAULT 'IDR',
  InstalledAt     DATETIME,
  ExpiresAt       DATETIME,
  Checksum        TEXT NOT NULL,           -- SHA-256
  SourceFile      TEXT,                    -- Path to original .rabpack
  IsActive        INTEGER DEFAULT 1,
  UNIQUE(PackId, Version)
);

-- NEW: Hierarchical categories (replaces flat string in Products.Category)
CREATE TABLE Categories (
  Id              INTEGER PRIMARY KEY,
  PackId          INTEGER REFERENCES LibraryPacks(Id),
  Path            TEXT NOT NULL,           -- 'Material/Elektrikal/MCB/1-pole'
  ParentId        INTEGER REFERENCES Categories(Id),
  Depth           INTEGER,                 -- Denormalized for fast filter
  DefaultUnit     TEXT,
  Icon            TEXT,
  Metadata        TEXT                     -- JSON blob
);

-- EXTEND existing Products table
ALTER TABLE Products ADD COLUMN PackId       INTEGER REFERENCES LibraryPacks(Id);
ALTER TABLE Products ADD COLUMN CategoryId   INTEGER REFERENCES Categories(Id);
ALTER TABLE Products ADD COLUMN Unit         TEXT DEFAULT 'pcs';
ALTER TABLE Products ADD COLUMN PriceJson    TEXT;     -- Object form (tiers/regions/formula)
ALTER TABLE Products ADD COLUMN SpecsJson    TEXT;     -- {amp:10, pole:1}
ALTER TABLE Products ADD COLUMN FormulaRef   TEXT;     -- Lookup ke Formulas table
ALTER TABLE Products ADD COLUMN ValidFrom    DATETIME;
ALTER TABLE Products ADD COLUMN ValidTo      DATETIME;

-- NEW: Reusable formulas
CREATE TABLE Formulas (
  Id              INTEGER PRIMARY KEY,
  PackId          INTEGER REFERENCES LibraryPacks(Id),
  Name            TEXT NOT NULL,
  InputsJson      TEXT,                    -- [{name,label,type,unit,default}]
  Expression      TEXT NOT NULL,           -- Safe expr engine (NCalc / Jint)
  OutputUnit      TEXT
);

-- NEW: RAB templates
CREATE TABLE Templates (
  Id              INTEGER PRIMARY KEY,
  PackId          INTEGER REFERENCES LibraryPacks(Id),
  Name            TEXT,
  ItemsJson       TEXT                     -- [{ref,qty,notes}]
);

-- NEW: Multi-tenant isolation (SaaS layer)
CREATE TABLE Tenants (
  Id              INTEGER PRIMARY KEY,
  Slug            TEXT UNIQUE,
  CompanyName     TEXT,
  LicenseKey      TEXT,
  AllowedPacks    TEXT                     -- JSON array of pack ids
);
ALTER TABLE Users         ADD COLUMN TenantId INTEGER REFERENCES Tenants(Id);
ALTER TABLE Estimations   ADD COLUMN TenantId INTEGER REFERENCES Tenants(Id);
ALTER TABLE LibraryPacks  ADD COLUMN TenantId INTEGER REFERENCES Tenants(Id);
```

### Migration dari Existing Panel Calculator DB

```
existing.Products.Category (TEXT)        → Categories.Path
existing.Products.ReferenceCode          → Products.ref (kept)
existing.Products.Price (TEXT)           → Products.Price (number) OR PriceJson
existing.Products.Vendor                 → vendors[].code (new lookup table)
existing.Products.PriceYear+LastUpdated  → LibraryPacks.Version
```

Default tenant `tts` di-create saat first migration; semua existing data di-attach ke tenant ini + auto-create `LibraryPack` `tts-panel-listrik@2026.Q1`.

---

## 3. Packaging Format — `.rabpack` (ZIP)

```
tts-panel-listrik-2026.Q1.rabpack       (just a renamed .zip)
├── manifest.json                       # Pack metadata + checksums dari semua file
├── library.json                        # Main data (categories, items, formulas, templates)
├── branding/
│   ├── branding-config.json            # Colors, fonts, signature defaults
│   ├── letterhead.jpg                  # Header surat penawaran
│   ├── footer.jpg                      # Footer surat
│   ├── logo.png                        # Company logo
│   └── stamp.png                       # Cap perusahaan
├── images/                             # Product photos (optional, referenced from items[].image)
│   ├── c60n-1p.jpg
│   └── ...
├── templates/                          # DOCX/HTML output templates
│   ├── penawaran.docx
│   └── invoice.html
├── docs/
│   ├── README.md                       # Cara pakai library ini
│   └── changelog.md
├── license.key                         # Signed license (Ed25519) — pack_id + tenant + expiry
└── signature.sig                       # Publisher signature dari manifest.json
```

### Manifest Format

```json
{
  "pack_id": "tts-panel-listrik",
  "version": "2026.Q1.2",
  "files": {
    "library.json":            { "sha256": "abc...", "bytes": 458123 },
    "branding/letterhead.jpg": { "sha256": "def...", "bytes": 234567 }
  },
  "publisher_pubkey": "ed25519:...",
  "min_app_version": "1.3.0"
}
```

**Verifikasi flow saat install:**
1. Unzip ke temp folder
2. Verify `signature.sig` matches `manifest.json` dengan publisher pubkey (prevent tampering)
3. Verify setiap file SHA-256 match dengan manifest
4. Validate `license.key` (tenant ID match + not expired)
5. Validate `library.json` against JSON Schema
6. Begin transaction: insert ke `LibraryPacks`, `Categories`, `Products`, `Formulas`, `Templates`
7. Copy `branding/` ke `%AppData%/RABCalc/tenants/{tenantId}/packs/{packId}/`
8. Commit + activate

---

## 4. Versioning Strategy

### Format: `YYYY.QN[.patch]`

| Tipe | Contoh | Trigger |
|------|--------|---------|
| **Quarterly release** | `2026.Q1`, `2026.Q2` | Update harga reguler dari vendor (PDF baru rilis) |
| **Patch** | `2026.Q1.1`, `2026.Q1.2` | Koreksi typo, tambah produk lupa, fix formula |
| **Major** | `2026.Q1` → `2027.Q1` | Restructure category tree, breaking schema change |

### Rules

- **Patch backward-compatible** — user bisa upgrade tanpa kehilangan estimasi lama. Items lama yang dihapus tetap di-keep di DB dengan flag `IsObsolete=1`; estimasi yang reference produk tersebut tetap valid.
- **Major version requires migration script** — di-bundle dalam pack as `migrations/v2026-to-v2027.sql`.
- **App pinning** — `min_app_version` di manifest. App tolak install kalau versi app < required.
- **Co-existence** — beberapa versi pack bisa coexist (e.g., `tts-panel@2025.Q4` + `tts-panel@2026.Q1`). User pilih saat create estimasi. Estimasi lama tetap reference versi originalnya.
- **Diff updates** — patch release boleh ship hanya `library.diff.json` (JSON Patch RFC 6902) untuk bandwidth saving. App apply diff ke pack yang sudah terinstall.
- **Expiry warning** — kalau `pack.expires` lewat, banner orange "Pricelist sudah lebih dari 6 bulan — pertimbangkan update".

### Naming Convention File

```
{publisher}-{industry}-{scope}-{version}.rabpack

tts-panel-listrik-2026.Q1.rabpack
acme-sipil-rumah-tinggal-2026.Q1.rabpack
johnson-mep-komersial-2026.Q2.rabpack
```

---

## 5. Tiga Contoh Konkret

### (a) Panel Listrik PT TTS Pack

```json
{
  "pack": {
    "id": "tts-panel-listrik",
    "name": "Panel Listrik PT Tritunggal Swarna",
    "version": "2026.Q1",
    "industry": "panel-listrik",
    "publisher": { "name": "Kuntjoro Handoko", "company": "PT TTS", "email": "info@tts.co.id" },
    "currency": "IDR",
    "released": "2026-01-15",
    "expires":  "2026-07-15",
    "sources": [
      { "vendor": "Schneider", "document": "Pricelist-2026-Q1.pdf", "date": "2026-01-02", "sha256": "..." },
      { "vendor": "Himel",     "document": "Himel-Catalog-2026.pdf", "date": "2026-01-05", "sha256": "..." }
    ]
  },
  "categories": [
    { "path": "Komponen/MCB/1-Pole",  "unit": "pcs", "icon": "bolt" },
    { "path": "Komponen/MCB/3-Pole",  "unit": "pcs" },
    { "path": "Komponen/MCCB",        "unit": "pcs" },
    { "path": "Komponen/Contactor",   "unit": "pcs" },
    { "path": "Kabel/NYY",            "unit": "m" },
    { "path": "Box/Wall-Mount",       "unit": "pcs" }
  ],
  "vendors": [
    { "code": "SCH", "name": "Schneider Electric", "tier": "premium",  "discount_chain": "40+10" },
    { "code": "HIM", "name": "Himel",              "tier": "standard", "discount_chain": "45+15" },
    { "code": "HOW", "name": "HOWIG",              "tier": "economy",  "discount_chain": "50+20" }
  ],
  "items": [
    { "ref": "A9F74110", "name": "iC60N 1P 10A",  "category": "Komponen/MCB/1-Pole",
      "vendor": "SCH", "specs": {"amp":10,"pole":1,"kA":6}, "price": 285000, "stock": "available" },
    { "ref": "HDB3-32",  "name": "Himel MCB 3P 32A", "category": "Komponen/MCB/3-Pole",
      "vendor": "HIM", "specs": {"amp":32,"pole":3,"kA":6}, "price": 425000 },
    { "ref": "NYY-4x10", "name": "Kabel NYY 4x10 mm2", "category": "Kabel/NYY",
      "unit": "m", "price": { "list": 89500, "formula": "list * length * (1 + waste/100)" } }
  ],
  "formulas": {
    "cable_with_waste": {
      "inputs": [
        {"name":"length","label":"Panjang","type":"number","unit":"m"},
        {"name":"waste","label":"Waste %","type":"number","default":5}
      ],
      "expression": "price * length * (1 + waste/100)",
      "output_unit": "IDR"
    }
  },
  "templates": [
    { "name": "Panel Distribusi 6-Group",
      "items": [
        {"ref":"A9F74110","qty":1,"notes":"Main MCB"},
        {"ref":"HDB3-32","qty":6,"notes":"Branch group"}
      ]}
  ]
}
```

### (b) Konstruksi Sipil Pack

```json
{
  "pack": { "id":"acme-sipil-rumah", "name":"Konstruksi Rumah Tinggal Jawa Barat",
            "version":"2026.Q1", "industry":"konstruksi-sipil", "currency":"IDR" },
  "categories": [
    { "path":"Material/Struktur/Beton", "unit":"m3" },
    { "path":"Material/Struktur/Besi-Beton", "unit":"kg" },
    { "path":"Material/Dinding/Bata", "unit":"pcs" },
    { "path":"Material/Finishing/Cat", "unit":"liter" },
    { "path":"Material/Atap/Genteng", "unit":"pcs" }
  ],
  "items": [
    { "ref":"BJTD-10", "name":"Besi Beton Polos D10", "category":"Material/Struktur/Besi-Beton",
      "unit":"kg",
      "price": { "list": 13500, "regions": {"bandung":1.0,"jakarta":1.08,"surabaya":1.05} } },
    { "ref":"BATA-MERAH-STD", "name":"Bata Merah Standard", "category":"Material/Dinding/Bata",
      "price": { "tiers": {"<1000":850,"1000-5000":800,">5000":750} } },
    { "ref":"BETON-K225", "name":"Beton Ready-Mix K-225", "category":"Material/Struktur/Beton",
      "unit":"m3", "price": 1150000,
      "formula": "price * volume + (volume > 5 ? 0 : 500000)" }
  ],
  "formulas": {
    "plat_lantai": {
      "inputs": [
        {"name":"luas","label":"Luas","type":"number","unit":"m2"},
        {"name":"tebal","label":"Tebal","type":"number","unit":"cm","default":12}
      ],
      "expression": "luas * (tebal/100) * (price_beton + price_besi_per_m3 + upah_per_m3)",
      "output_unit":"IDR"
    }
  },
  "templates": [
    { "name":"Rumah Type 36/72 — Struktur Saja",
      "items":[
        {"ref":"BETON-K225","qty":12,"notes":"Sloof + kolom + ringbalk"},
        {"ref":"BJTD-10","qty":850,"notes":"Tulangan kolom & balok"},
        {"ref":"BATA-MERAH-STD","qty":6500}
      ]}
  ]
}
```

### (c) MEP Pack

```json
{
  "pack": { "id":"johnson-mep-komersial", "name":"MEP Komersial — Plumbing & HVAC",
            "version":"2026.Q2", "industry":"mep", "currency":"IDR" },
  "categories": [
    { "path":"Plumbing/Pipa/PPR",      "unit":"m" },
    { "path":"Plumbing/Pipa/PVC",      "unit":"m" },
    { "path":"Plumbing/Fitting/PPR",   "unit":"pcs" },
    { "path":"Plumbing/Valve",         "unit":"pcs" },
    { "path":"Plumbing/Pompa",         "unit":"unit" },
    { "path":"HVAC/Pipa-Tembaga",      "unit":"m" }
  ],
  "items": [
    { "ref":"PPR-32-PN20", "name":"Pipa PPR 32mm PN20", "category":"Plumbing/Pipa/PPR",
      "unit":"m", "specs":{"diameter_mm":32,"pn":20}, "price":48500 },
    { "ref":"BALL-VALVE-1\"", "name":"Ball Valve Brass 1\"", "category":"Plumbing/Valve",
      "specs":{"size":"1 inch","material":"brass"}, "price":125000 },
    { "ref":"GRUNDFOS-CM5-4", "name":"Pompa Grundfos CM5-4", "category":"Plumbing/Pompa",
      "unit":"unit", "specs":{"head_m":40,"flow_lpm":83}, "price": 8750000 },
    { "ref":"CU-PIPE-1/2\"", "name":"Pipa Tembaga 1/2\" L-Type", "category":"HVAC/Pipa-Tembaga",
      "unit":"m", "price":{ "list":85000, "formula":"list * length + (length < 6 ? 50000 : 0)" } }
  ],
  "formulas": {
    "pipe_with_fittings": {
      "inputs": [
        {"name":"length","label":"Panjang Run","type":"number","unit":"m"},
        {"name":"elbow_count","label":"Jumlah Elbow","type":"number","default":0},
        {"name":"tee_count","label":"Jumlah Tee","type":"number","default":0}
      ],
      "expression": "(price_pipe * length) + (price_elbow * elbow_count) + (price_tee * tee_count)",
      "output_unit":"IDR"
    }
  },
  "templates": [
    { "name":"Toilet Set Komersial — Plumbing",
      "items":[
        {"ref":"PPR-32-PN20","qty":12,"notes":"Main feeder"},
        {"ref":"BALL-VALVE-1\"","qty":2,"notes":"Isolation valves"}
      ]}
  ]
}
```

---

## Ringkasan Design Decisions

| Concern | Solution |
|---|---|
| Non-tech edit | CSV import/export untuk `items[]`; library.json untuk advanced |
| Multi-level kategori | Path-based string (`A/B/C`) + denormalized depth field |
| Varian harga | `price` polymorphic: number OR object `{list, tiers, regions, formula}` |
| Formula custom | NCalc/Jint sandboxed expression engine + reusable named formulas |
| Versioning | `YYYY.QN.patch`; multi-version coexistence; JSON Patch untuk diff updates |
| Distribution | Signed `.rabpack` ZIP dengan Ed25519 publisher signature + SHA-256 manifest |
| Multi-tenant | `Tenants` table + license.key per-tenant binding; `AllowedPacks` whitelist |
| Backward compat existing TTS app | Default tenant `tts` + auto-migrate Products → LibraryPack `tts-panel-listrik@2026.Q1` |