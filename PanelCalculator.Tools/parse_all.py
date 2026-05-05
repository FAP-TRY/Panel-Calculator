#!/usr/bin/env python3
"""
Parser daftar harga: Schneider Electric + Chint Indonesia
Output: products_all.csv  (siap import ke PanelCalculator)

Format output:
  category, reference_code, product_name, specifications, price, stock_status, vendor
"""

import re, csv, sys
from pathlib import Path
from collections import defaultdict

try:
    import pdfplumber
except ImportError:
    print("Jalankan dulu: pip install pdfplumber"); sys.exit(1)

# ─────────────────────────────────────────────────────────────────────────────
#  REGEX POLA BERSAMA
# ─────────────────────────────────────────────────────────────────────────────

# Harga Indonesia: 43.999  /  772.000  /  1.168.000
PRICE_RE = re.compile(r'\b(\d{1,3}(?:\.\d{3})+)\b')

# Kode referensi Schneider: minimal 6 karakter huruf+angka kapital, dimulai huruf
SCH_REF  = re.compile(r'\b([A-Z][A-Z0-9]{5,14})\b')
# Kode numerik Chint: 5-7 digit
CHT_CODE = re.compile(r'\b(\d{5,7})\b')

SKIP_WORDS = {
    'HDCFD','PPCCB','SNI','IEC','VSD','ATS','LBS','PFC',
    'MCB','MCCB','RCCB','RCBO','ELCB','ACB','AFD',
    'NULL','SLIM','VIGI','AFDD','TM','DC','AC',
}

# Nilai ampere umum untuk pembuatan tripan (RCCB/MCB/MCCB/ACB)
AMPERE_VALUES = {
    1, 2, 3, 4, 6, 8, 10, 13, 16, 20, 25, 32, 40, 50, 63, 80, 100, 125,
    160, 200, 250, 320, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3200, 4000
}

# Pola "30 mA" atau "30mA" (untuk sensitivitas RCCB)
SENS_RE  = re.compile(r'\b(\d+)\s*m[Aa]\b')
# Pola "N Kutub" atau "NP"
KUTUB_RE = re.compile(r'\b(\d)\s*[Kk]utub\b')
POLE_RE  = re.compile(r'\b([1-4])\s*P(?:ole)?\b', re.IGNORECASE)


def price_int(s: str) -> int:
    return int(s.replace('.', ''))


def is_sch_ref(code: str) -> bool:
    if code in SKIP_WORDS: return False
    if len(code) < 6: return False
    has_l = any(c.isalpha() for c in code)
    has_d = any(c.isdigit() for c in code)
    return has_l and has_d


# ─────────────────────────────────────────────────────────────────────────────
#  SPEC EXTRACTION HELPERS
# ─────────────────────────────────────────────────────────────────────────────

def extract_sensitivity(tokens: list[str]) -> str:
    """Find sensitivity (mA) value from token list. Returns '' if not found."""
    raw = ' '.join(tokens)
    m = SENS_RE.search(raw)
    return (m.group(1) + 'mA') if m else ''


def extract_poles(tokens: list[str]) -> str:
    """Find pole count (1-4) from token list.  Returns '' if not found."""
    raw = ' '.join(tokens)

    # Explicit "N Kutub" form
    m = KUTUB_RE.search(raw)
    if m:
        return m.group(1) + 'P'

    # Explicit "NP" form (e.g. "2P", "4P", "1P+N")
    m = POLE_RE.search(raw)
    if m:
        return m.group(1) + 'P'

    # Also handle "1P+N" and "3P+N" that are common in Easy9/Domae RCCB
    m = re.search(r'\b([134])\s*P\s*\+\s*N\b', raw, re.IGNORECASE)
    if m:
        # 1P+N = effectively 2 poles, but keep the notation
        return m.group(1) + 'P+N'

    # Bare digit 1–4 followed by an ampere value on the same line
    for i, tok in enumerate(tokens):
        if re.fullmatch(r'[1-4]', tok):
            # Check that next non-empty token is an ampere-like value
            for rest in tokens[i+1:]:
                try:
                    v = int(rest)
                    if v in AMPERE_VALUES:
                        return tok + 'P'
                    break   # some other number – stop looking
                except ValueError:
                    continue  # skip non-numeric tokens
    return ''


def extract_ampere(tokens: list[str]) -> str:
    """Find ampere rating from token list (rightmost plausible match)."""
    raw = ' '.join(tokens)

    # Explicit "N A" or "NA" not preceded by 'm' (to avoid matching mA)
    for m in re.finditer(r'(?<![mM])(\d+(?:\.\d+)?)\s*A\b', raw):
        try:
            val = int(float(m.group(1)))
            if val in AMPERE_VALUES:
                return str(val) + 'A'
        except ValueError:
            pass

    # Rightmost bare number matching AMPERE_VALUES
    nums = re.findall(r'\b(\d+)\b', raw)
    for n in reversed(nums):
        val = int(n)
        if val in AMPERE_VALUES:
            return str(val) + 'A'
    return ''


def decode_ampere_from_ref(ref: str) -> str:
    """
    Fallback: try to decode ampere rating from the numeric suffix of a
    Schneider reference code.  e.g. A9R71240 → last 2 digits "40" → 40A.
    """
    m = re.search(r'(\d{2,4})$', ref)
    if not m:
        return ''
    suffix = m.group(1)
    # Try last 2 digits first, then last 3
    for length in (2, 3):
        if len(suffix) >= length:
            val = int(suffix[-length:])
            if val in AMPERE_VALUES:
                return str(val) + 'A'
    # Special: "91" at end often encodes 100A in Schneider RCCB high-current refs
    if suffix.endswith('91'):
        return '100A'
    return ''


# ── Schneider A9R RCCB direct decode from reference code ─────────────────────
# The code A9R-{type_2}-{pole_1}-{amp_2} encodes sensitivity, poles, and ampere.
_A9R_SENS = {
    '10': '10mA', '14': '10mA', '80': '10mA',   # 10mA variants
    '30': '30mA', '31': '30mA', '71': '30mA',   # 30mA variants
    '74': '300mA','75': '300mA','76': '300mA',   # 300mA variants
}

def decode_a9r_spec(ref: str) -> tuple[str, str, str]:
    """
    Decode sensitivity, poles, ampere from an A9R RCCB reference code.
    Returns ('', '', '') if not decodable.
    e.g.  A9R71240  →  ('30mA', '2P', '40A')
          A9R14491  →  ('10mA', '4P', '100A')
          A9R74463  →  ('300mA', '4P', '63A')
    """
    if not ref.startswith('A9R') or len(ref) < 8:
        return '', '', ''
    digits = ref[3:]     # e.g. "10216", "71240", "14491"
    if len(digits) < 5:
        return '', '', ''

    type_code  = digits[:2]          # first 2 digits → sensitivity
    pole_digit = digits[2]           # 3rd digit     → poles
    amp_suffix = digits[3:]          # remaining     → current

    sens  = _A9R_SENS.get(type_code, '')
    poles = (pole_digit + 'P') if pole_digit in ('1','2','3','4') else ''

    amp = ''
    try:
        # Try 2-digit current first
        v2 = int(amp_suffix[-2:])
        if v2 in AMPERE_VALUES:
            amp = str(v2) + 'A'
        # 3-digit (e.g. 100A, 125A)
        elif len(amp_suffix) >= 3:
            v3 = int(amp_suffix[-3:])
            if v3 in AMPERE_VALUES:
                amp = str(v3) + 'A'
        # "91" encodes 100A in some Schneider RCCB codes
        if not amp and amp_suffix.endswith('91'):
            amp = '100A'
    except ValueError:
        pass

    return sens, poles, amp


def build_spec(spec_parts: list[str]) -> str:
    """Join non-empty spec parts with a single space."""
    return ' '.join(p for p in spec_parts if p)


# ── Schneider Domae RCCB: DOMR0{type}{pole}{amp} ─────────────────────────────
_DOMR_SENS = {
    '1': '30mA',    # residential standard
    '2': '300mA',   # selective / industrial
}

def decode_domr_spec(ref: str) -> tuple[str, str, str]:
    """
    Decode RCCB Domae spec from reference code.
    Format: DOMR + 0 + {type:1} + {pole:1} + {amp:2}
    e.g.  DOMR01225 → ('30mA', '2P', '25A')
          DOMR02463 → ('300mA', '4P', '63A')
    """
    if not ref.startswith('DOMR') or len(ref) < 9:
        return '', '', ''
    body = ref[4:]          # e.g. "01225"
    if len(body) < 5:
        return '', '', ''
    type_code  = body[1]    # '1' or '2' → sensitivity
    pole_digit = body[2]    # '2' or '4' → poles
    amp_str    = body[3:]   # '25', '40', '63'

    sens  = _DOMR_SENS.get(type_code, '')
    poles = (pole_digit + 'P') if pole_digit in ('2', '4') else ''
    amp = ''
    try:
        v = int(amp_str)
        if v in AMPERE_VALUES:
            amp = str(v) + 'A'
    except ValueError:
        pass
    return sens, poles, amp


# ── Schneider Easy9 RCCB: EZ9R0{type}{pole}{amp} ─────────────────────────────
_EZ9R_SENS = {
    '4': '30mA',    # standard Easy9 RCCB (AC type, 30mA)
}

def decode_ez9r_spec(ref: str) -> tuple[str, str, str]:
    """
    Decode RCCB Easy9 spec from reference code.
    Format: EZ9R + 0 + {type:1} + {pole:1} + {amp:2}
    e.g.  EZ9R04225 → ('30mA', '2P', '25A')
          EZ9R04440 → ('30mA', '4P', '40A')
    """
    if not ref.startswith('EZ9R') or len(ref) < 9:
        return '', '', ''
    body = ref[4:]          # e.g. "04225"
    if len(body) < 5:
        return '', '', ''
    type_code  = body[1]    # '4' → 30mA (Easy9 only has 30mA)
    pole_digit = body[2]    # '2' or '4'
    amp_str    = body[3:]   # '25', '40'

    sens  = _EZ9R_SENS.get(type_code, '30mA')   # Easy9 only = 30mA
    poles = (pole_digit + 'P') if pole_digit in ('1', '2', '3', '4') else ''
    amp = ''
    try:
        v = int(amp_str)
        if v in AMPERE_VALUES:
            amp = str(v) + 'A'
    except ValueError:
        pass
    return sens, poles, amp


# ── Schneider RCBO iDPN Vigi: A9D{type_2}{series_1}{amp_2} ───────────────────
_A9D_SENS = {
    '31': '30mA',    # iDPN Vigi type A 30mA
    '41': '300mA',   # iDPN Vigi type AC 300mA
}

def decode_a9d_spec(ref: str) -> tuple[str, str, str]:
    """
    Decode RCBO iDPN Vigi spec from reference code.
    Format: A9D + {type:2} + {series:1} + {amp:2}
    e.g.  A9D31606 → ('30mA', '1P+N', '6A')
          A9D41640 → ('300mA', '1P+N', '40A')
    """
    if not ref.startswith('A9D') or len(ref) < 8:
        return '', '', ''
    digits = ref[3:]
    if len(digits) < 5:
        return '', '', ''
    type_code = digits[:2]          # '31' or '41'
    # digits[2] = series/variant digit (not poles – iDPN is always 1P+N)
    amp_str   = digits[3:]          # '06', '10', '16', ...

    sens  = _A9D_SENS.get(type_code, '')
    poles = '1P+N'                  # iDPN Vigi is always 1-pole + neutral
    amp = ''
    try:
        v = int(amp_str[-2:])
        if v in AMPERE_VALUES:
            amp = str(v) + 'A'
        elif len(amp_str) >= 3:
            v = int(amp_str[-3:])
            if v in AMPERE_VALUES:
                amp = str(v) + 'A'
    except ValueError:
        pass
    return sens, poles, amp


# ─────────────────────────────────────────────────────────────────────────────
#  PARSING SCHNEIDER
# ─────────────────────────────────────────────────────────────────────────────

SCH_CATEGORY_MAP = {
    'MCB':    ['mcb','miniature circuit breaker','domae','easy9','acti9 ik60','acti9 ic60'],
    'RCCB':   ['rccb','elcb','rcbo','residual current','id ','vigi'],
    'MCCB':   ['mccb','molded case','gopact','cvs','ezc','nsxm','nsx ','ns '],
    'ACB':    ['acb','air circuit breaker','masterpact','compact ns','nw '],
    'Kontaktor': ['contactor','kontaktor','lc1','lrd','tesys'],
    'Motor CB':  ['motor circuit breaker','gbx','gv2','gv3'],
    'Surge Arrester': ['surge','arrester','iprd','parafoudre'],
    'Box':    ['box','pragma','panelboard','enclosure'],
    'Busbar': ['busbar','busbar sisir','isobar','linergy'],
    'Accessories': ['aksesori','auxiliary','socket','switch','relay','afdd','arc fault'],
}

def sch_category(text: str) -> str:
    t = text.lower()
    for cat, kws in SCH_CATEGORY_MAP.items():
        if any(k in t for k in kws):
            return cat
    return 'Other'

SCH_FAMILY_BY_PREFIX = {
    'DOMF': 'MCB Domae',       'DOMR': 'RCCB/ELCB Domae',
    'DOMH': 'MCB Box Domae',   'DOMD': 'RCBO Slim Domae',
    'DOML': 'Surge Arrester Domae',
    'EZ9F': 'MCB Easy9',       'EZ9R': 'RCCB Easy9',
    'EZ9X': 'Busbar Easy9',
    'A9K' : 'MCB Acti9 iK60',  'A9F' : 'MCB Acti9 iC60',
    'A9N' : 'MCB Acti9 iC60N', 'A9P' : 'MCB Acti9 iC60H-DC',
    'A9R' : 'RCCB Acti9',      'A9D' : 'RCBO iDPN Vigi Acti9',
    'A9L' : 'Surge Arrester Acti9', 'A9XP': 'Busbar Linergy',
    'A9TA': 'AFDD Acti9',      'A9TD': 'AFDD Acti9',
    'A9C' : 'Kontaktor Acti9', 'A9S' : 'Switch Acti9',
    'A9A' : 'Aksesori Acti9',  'A9Z' : 'Aksesori Acti9',
    'MIP' : 'MCB Box Mini Pragma',
    'SEA9': 'Busbar Isobar',
    'G12' : 'MCCB GoPact 125', 'G16' : 'MCCB GoPact 160',
    'G25' : 'MCCB GoPact 250', 'NSY' : 'MCCB NSXm',
    'LV4' : 'MCCB CVS/NSX',   'EZC1': 'MCCB EasyPact EZC',
    'LV8' : 'ACB Masterpact',
    'LC1' : 'Kontaktor TeSys', 'LC3' : 'Kontaktor TeSys',
    'LRD' : 'Relay Thermal TeSys',
    'GBX' : 'Motor CB GV2',    'GV2' : 'Motor CB GV2',
    'GV3' : 'Motor CB GV3',
    # Vigi RCCB add-on modules for Compact NSX/NSXm MCCB
    'C10F': 'Vigi NSX 100F',   'C16F': 'Vigi NSX 160F',
    'C25F': 'Vigi NSX 250F',   'C40F': 'Vigi NSX 400F',
    'C63F': 'Vigi NSX 630F',
    'C10N': 'Vigi NSX 100N',   'C16N': 'Vigi NSX 160N',
    'C25N': 'Vigi NSX 250N',   'C40N': 'Vigi NSX 400N',
    'C63N': 'Vigi NSX 630N',
    'C10H': 'Vigi NSX 100H',   'C16H': 'Vigi NSX 160H',
    'C25H': 'Vigi NSX 250H',   'C40H': 'Vigi NSX 400H',
    'C63H': 'Vigi NSX 630H',
}

def sch_family(ref: str) -> str:
    for pfx in sorted(SCH_FAMILY_BY_PREFIX, key=len, reverse=True):
        if ref.upper().startswith(pfx):
            return SCH_FAMILY_BY_PREFIX[pfx]
    return ''

def sch_category_by_ref(ref: str) -> str:
    r = ref.upper()
    prefix_cat = {
        ('DOMF','EZ9F','A9K','A9F','A9N','A9P','A9Q'):   'MCB',
        ('DOMR','EZ9R','A9R','A9D'):                     'RCCB',
        ('DOMH','MIP'):                                   'Box',
        ('DOML','A9L'):                                   'Surge Arrester',
        ('EZ9X','A9XP','SEA9'):                           'Busbar',
        ('A9TA','A9TD','A9C','A9S','A9A','A9Z','LRD'):    'Accessories',
        ('LC1','LC3'):                                     'Kontaktor',
        ('GBX','GV2','GV3'):                              'Motor CB',
        ('G12','G16','G25','NSY','LV4','EZC1','LV5','LV6','LV8','NW'): 'MCCB',
        # Vigi add-on modules are MCCB accessories, not standalone RCCB
        ('C10F','C16F','C25F','C40F','C63F',
         'C10N','C16N','C25N','C40N','C63N',
         'C10H','C16H','C25H','C40H','C63H'): 'Accessories',
    }
    for prefixes, cat in prefix_cat.items():
        for p in prefixes:
            if r.startswith(p): return cat
    if r.startswith('LV8') or r.startswith('NW'): return 'ACB'
    return ''


def parse_schneider(pdf_path: Path) -> list[dict]:
    products = []
    seen     = set()

    with pdfplumber.open(pdf_path) as pdf:
        for page in pdf.pages:
            text  = page.extract_text() or ''
            lines = text.splitlines()

            # Determine page-level category from first few lines
            header   = ' '.join(lines[:6])
            page_cat = sch_category(header)

            # ── Section-level spec trackers ─────────────────────────────────
            # These persist across rows within a page to handle merged-cell PDFs
            # (sensitivity and pole values only appear on first row of each group)
            current_sens  = ''   # e.g. '10mA', '30mA', '300mA'
            current_poles = ''   # e.g. '2P', '4P', '2P+N'

            for line in lines:
                line   = line.strip()
                tokens = line.split()

                # ── Update spec trackers on EVERY line ──────────────────────
                # Sensitivity/poles appear on their own header rows in merged-cell
                # PDFs (e.g., "10 mA  2" on a row with no product code).
                line_sens  = extract_sensitivity(tokens)
                line_poles = extract_poles(tokens)

                if line_sens and line_sens != current_sens:
                    current_sens  = line_sens
                    current_poles = ''    # poles reset when sensitivity changes

                # Additional check: bare digit 1–4 at end of line that has sensitivity
                # catches "10 mA  2" (2 = poles) and "30 mA  4" (4 = 4P) header rows
                if not line_poles and line_sens:
                    for tok in reversed(tokens):
                        if re.fullmatch(r'[1-4]', tok):
                            line_poles = tok + 'P'
                            break
                        if SENS_RE.match(tok) or tok.lower() == 'ma':
                            break

                if line_poles:
                    current_poles = line_poles

                # ── Skip non-product lines ──────────────────────────────────
                prices = PRICE_RE.findall(line)
                refs   = [m for m in SCH_REF.findall(line) if is_sch_ref(m)]
                if not refs or not prices: continue

                i = 0
                while i < len(tokens):
                    tok = tokens[i]
                    if is_sch_ref(tok) and SCH_REF.fullmatch(tok):
                        ref = tok
                        # ── Find price in next few tokens ───────────────────
                        for j in range(i+1, min(i+4, len(tokens))):
                            pm = PRICE_RE.fullmatch(tokens[j].rstrip(','))
                            if pm:
                                price_str = pm.group()
                                ss = 1
                                if j+1 < len(tokens) and tokens[j+1] in ('1','2'):
                                    ss = int(tokens[j+1])
                                price_val = price_int(price_str)
                                if price_val < 1000: break

                                if ref not in seen:
                                    seen.add(ref)

                                    # ── Extract spec from tokens BEFORE ref ─
                                    idx_ref    = tokens.index(ref, i)
                                    pre_tokens = tokens[:idx_ref]

                                    # Remove other ref codes and prices from the
                                    # PREVIOUS product on the same line
                                    pre_tokens = [t for t in pre_tokens
                                                  if not is_sch_ref(t)
                                                  and not PRICE_RE.fullmatch(t)]

                                    # Remove trailing SS (1 or 2) that may bleed
                                    # from the preceding product's stock status
                                    while pre_tokens and pre_tokens[-1] in ('1','2'):
                                        pre_tokens.pop()

                                    # ── Parse spec components ───────────────
                                    new_sens  = extract_sensitivity(pre_tokens)
                                    new_poles = extract_poles(pre_tokens)
                                    new_amp   = extract_ampere(pre_tokens)

                                    # When sensitivity changes → reset poles
                                    # (a new sensitivity section restarts pole grouping)
                                    if new_sens and new_sens != current_sens:
                                        current_sens  = new_sens
                                        current_poles = ''   # will be updated below

                                    if new_poles:
                                        current_poles = new_poles

                                    # If still no ampere, try decoding from ref code
                                    if not new_amp:
                                        new_amp = decode_ampere_from_ref(ref)

                                    # ── Build final spec string ─────────────
                                    family = sch_family(ref)
                                    cat    = sch_category_by_ref(ref) or page_cat

                                    # Decode spec directly from reference code for
                                    # products where PDF text order is unreliable.
                                    decoded_sens, decoded_poles, decoded_amp = '', '', ''

                                    if ref.startswith('A9R'):
                                        decoded_sens, decoded_poles, decoded_amp = decode_a9r_spec(ref)
                                    elif ref.startswith('DOMR'):
                                        decoded_sens, decoded_poles, decoded_amp = decode_domr_spec(ref)
                                    elif ref.startswith('EZ9R'):
                                        decoded_sens, decoded_poles, decoded_amp = decode_ez9r_spec(ref)
                                    elif ref.startswith('A9D'):
                                        decoded_sens, decoded_poles, decoded_amp = decode_a9d_spec(ref)

                                    if decoded_sens or decoded_poles or decoded_amp:
                                        # Use direct-decode (authoritative) path
                                        spec_parts = []
                                        if decoded_sens:  spec_parts.append(decoded_sens)
                                        if decoded_poles: spec_parts.append(decoded_poles)
                                        if decoded_amp:   spec_parts.append(decoded_amp)
                                        elif new_amp:     spec_parts.append(new_amp)
                                    else:
                                        # Fall back to PDF-tracker values
                                        spec_parts = []
                                        if current_sens:  spec_parts.append(current_sens)
                                        if current_poles: spec_parts.append(current_poles)
                                        if new_amp:       spec_parts.append(new_amp)

                                    spec = build_spec(spec_parts)

                                    # For non-RCCB products that have no mA sensitivity,
                                    # the spec is just poles + ampere (or just ampere)
                                    name = (family + (' - ' + spec if spec else '')).strip(' -')
                                    if not name: name = ref

                                    products.append({
                                        'category':       cat,
                                        'reference_code': ref,
                                        'product_name':   name[:250],
                                        'specifications': spec[:150],
                                        'price':          price_val,
                                        'stock_status':   ss,
                                        'vendor':         'Schneider Electric',
                                    })
                                break
                    i += 1
    return products


# ─────────────────────────────────────────────────────────────────────────────
#  PARSING CHINT
# ─────────────────────────────────────────────────────────────────────────────

CHT_CATEGORY_MAP = {
    'MCB':    ['mcb','miniature circuit breaker','nxb','nb1','nb3','nb4','nb6'],
    'RCCB':   ['rccb','elcb','rcbo','residual','nbe','nl','nle'],
    'MCCB':   ['mccb','moulded','nm1','nm8','nm10','nc100','nk','ndb'],
    'ACB':    ['acb','air circuit breaker','nm10','nw','nws'],
    'Kontaktor': ['contactor','kontaktor','nc','nrc','nf'],
    'VSD':    ['vsd','variable speed','inverter','nv','sav','sv'],
    'ATS':    ['ats','automatic transfer','nz7','nz3','nza'],
    'LBS':    ['lbs','load break','nld','nk'],
    'Kapasitor Bank': ['capacitor','capaci','pfc','nrc'],
    'Surge Arrester': ['surge','spd','lightning'],
}

def cht_category(text: str, fname: str) -> str:
    t = (text + ' ' + fname).lower()
    for cat, kws in CHT_CATEGORY_MAP.items():
        if any(k in t for k in kws):
            return cat
    return 'Other'


def parse_chint(pdf_path: Path) -> list[dict]:
    products = []
    seen  = set()
    fname = pdf_path.stem

    with pdfplumber.open(pdf_path) as pdf:
        for page in pdf.pages:
            text  = page.extract_text() or ''
            lines = text.splitlines()

            header   = ' '.join(lines[:8])
            page_cat = cht_category(header, fname)

            # Product family name from header (first mixed-case title line)
            product_family = ''
            for ln in lines[:10]:
                ln = ln.strip()
                if len(ln) > 8 and not ln.isupper():
                    product_family = re.sub(r'\s+', ' ', ln)[:80]
                    break

            # Section-level trackers for Chint too
            current_sens  = ''
            current_poles = ''

            for line in lines:
                line   = line.strip()
                tokens = line.split()
                if len(tokens) < 4: continue

                for i, tok in enumerate(tokens):
                    if CHT_CODE.fullmatch(tok) and len(tok) >= 5:
                        code = tok
                        if code in seen: continue
                        if i+1 >= len(tokens): continue
                        pm = PRICE_RE.fullmatch(tokens[i+1].rstrip(','))
                        if not pm: continue
                        price_val = price_int(pm.group())
                        if price_val < 500: continue

                        ss = 1
                        for k in range(i+2, min(i+5, len(tokens))):
                            if tokens[k] in ('1', '2'):
                                ss = int(tokens[k])
                                break

                        # ── Spec from tokens BEFORE the code ────────────────
                        pre_tokens = tokens[:i]
                        pre_tokens = [t for t in pre_tokens
                                      if not CHT_CODE.fullmatch(t)
                                      and not PRICE_RE.fullmatch(t)]
                        while pre_tokens and pre_tokens[-1] in ('1','2'):
                            pre_tokens.pop()

                        new_sens  = extract_sensitivity(pre_tokens)
                        new_poles = extract_poles(pre_tokens)
                        new_amp   = extract_ampere(pre_tokens)

                        if new_sens and new_sens != current_sens:
                            current_sens  = new_sens
                            current_poles = ''
                        if new_poles:
                            current_poles = new_poles
                        if not new_amp:
                            new_amp = decode_ampere_from_ref(code)

                        # Build name from pre-tokens that aren't pure spec values
                        name_raw = ' '.join(pre_tokens).strip()
                        if len(name_raw) > 100:
                            parts = name_raw.split()
                            name_raw = ' '.join(parts[-10:]).strip()

                        # Build clean spec
                        spec_parts = []
                        if current_sens:  spec_parts.append(current_sens)
                        if current_poles: spec_parts.append(current_poles)
                        if new_amp:       spec_parts.append(new_amp)
                        spec = build_spec(spec_parts)

                        # Product name: use family from header if name_raw is empty/uninformative
                        base_name = name_raw or product_family or code
                        if spec and spec not in base_name:
                            name = f'{base_name} - {spec}'
                        else:
                            name = base_name

                        seen.add(code)
                        products.append({
                            'category':       page_cat,
                            'reference_code': code,
                            'product_name':   name[:250],
                            'specifications': spec[:150] if spec else name_raw[:150],
                            'price':          price_val,
                            'stock_status':   ss,
                            'vendor':         'Chint',
                        })
                        break
    return products


# ─────────────────────────────────────────────────────────────────────────────
#  MAIN
# ─────────────────────────────────────────────────────────────────────────────

BASE = Path(r'C:\Projects\Panel Calculator\PanelCalculator.Tools\PDF Daftar Harga')
OUT  = Path(r'C:\Projects\Panel Calculator\PanelCalculator.Tools\products_all.csv')

SCHNEIDER_PDFS = sorted((BASE / '1. Schneider').glob('*.pdf'))
CHINT_PDFS     = sorted((BASE / '2. Chint').glob('*.pdf'))

all_products = []
per_file     = {}

print('=== Parsing Schneider ===')
for p in SCHNEIDER_PDFS:
    prods = parse_schneider(p)
    per_file[p.name] = len(prods)
    all_products.extend(prods)
    print(f'  {p.name:50s} → {len(prods)} produk')

print('\n=== Parsing Chint ===')
for p in CHINT_PDFS:
    prods = parse_chint(p)
    per_file[p.name] = len(prods)
    all_products.extend(prods)
    print(f'  {p.name:50s} → {len(prods)} produk')

# Deduplicate by reference_code (keep first occurrence)
seen_refs = set()
unique = []
for prod in all_products:
    rc = prod['reference_code']
    if rc not in seen_refs:
        seen_refs.add(rc)
        unique.append(prod)

print(f'\nTotal sebelum dedup : {len(all_products)}')
print(f'Total setelah dedup : {len(unique)}')

# Write CSV
FIELDS = ['category','reference_code','product_name','specifications','price','stock_status','vendor']
with open(OUT, 'w', newline='', encoding='utf-8-sig') as f:
    w = csv.DictWriter(f, fieldnames=FIELDS)
    w.writeheader()
    w.writerows(unique)

print(f'\nCSV disimpan ke: {OUT}')

# Summary by category × vendor
from collections import Counter
counts = Counter((p['category'], p['vendor']) for p in unique)
print('\n=== Ringkasan per Kategori ===')
print(f"{'Kategori':25s} {'Vendor':22s} {'Jumlah':>8}")
print('-' * 58)
for (cat, vendor), n in sorted(counts.items()):
    print(f'{cat:25s} {vendor:22s} {n:8d}')

# Show sample RCCB rows to verify spec quality
from collections import defaultdict as dd
rccb = [p for p in unique if p['category'] == 'RCCB' and p['vendor'] == 'Schneider Electric']
print(f'\n=== Sample RCCB Schneider (first 15) ===')
print(f"{'Ref':15s} | {'Nama':50s} | {'Spec':25s} | {'Harga':>12}")
print('-' * 110)
for p in rccb[:15]:
    print(f"{p['reference_code']:15s} | {p['product_name'][:50]:50s} | {(p['specifications'] or ''):25s} | Rp {p['price']:>12,}")
