"""解析考選部測驗式試題 PDF 與標準答案 PDF。

只輸出通過完整性檢查的選擇題；無法完整還原（含圖表、選項數不符、答案無法對應）
的題目一律排除並記錄原因，不做任何人工補寫。
"""
import re, sys, json
import pymupdf as fitz

GENERIC_MARK = '\uf8ff'
MARK = {GENERIC_MARK: '?', '': 'A', '': 'B', '': 'C', '': 'D', '': 'E'}
SYMBOL_MAP = {'\uf02d': '－', '\uf02b': '＋', '\uf03d': '＝'}  # Symbol 字型常見符號
HEADER_RE = re.compile(r'代號|頁次')
SECTION_RE = re.compile(r'^\s*([一二三四五六甲乙丙]、|說明：|※)')
GROUP_RE = re.compile(r'第\s*(\d+)\s*題至第\s*(\d+)\s*題')


def page_lines(page):
    chars = []
    for b in page.get_text('rawdict')['blocks']:
        for l in b.get('lines', []):
            for s in l['spans']:
                for c in s['chars']:
                    if c['c'].strip() == '' and c['c'] != ' ':
                        continue
                    x0, y0, x1, y1 = c['bbox']
                    if 'Symbol' in s['font'] and c['c'] in SYMBOL_MAP:
                        c = dict(c, c=SYMBOL_MAP[c['c']])
                    chars.append({'c': c['c'], 'x0': x0, 'x1': x1, 'y0': y0, 'y1': y1,
                                  'yc': (y0 + y1) / 2, 'font': s['font'], 'size': s['size']})
    # header region
    lines = group_lines(chars)
    header_boxes = []
    for ln in lines:
        txt = ''.join(c['c'] for c in ln)
        if HEADER_RE.search(txt) and ln[0]['x0'] > 300 or re.match(r'^\s*代號：', txt):
            header_boxes.append((min(c['x0'] for c in ln) - 15, min(c['y0'] for c in ln) - 15,
                                 max(c['x1'] for c in ln) + 15, max(c['y1'] for c in ln) + 15))
    def in_header(r):
        return any(r[0] >= h[0] and r[2] <= h[2] and r[1] >= h[1] and r[3] <= h[3] for h in header_boxes)
    # drawings: thin horizontal lines with no glyph above = blank; thin line under glyphs = underline; others = figure/table
    blanks, figures = [], []
    for dr in page.get_drawings():
        r = dr['rect']
        rr = (r.x0, r.y0, r.x1, r.y1)
        if in_header(rr):
            continue
        w, h = r.x1 - r.x0, r.y1 - r.y0
        if h < 1.6 and w > 6:
            over = [c for c in chars if c['x1'] > r.x0 + 1 and c['x0'] < r.x1 - 1 and -1.5 <= r.y0 - c['y1'] <= 1.5]
            near = [c for c in chars if c['x1'] > r.x0 + 1 and c['x0'] < r.x1 - 1 and -1.5 <= r.y0 - c['y1'] <= 4.5]
            if over:
                for c in over:
                    if not c['c'] in MARK:
                        c['u'] = True
            elif near:
                figures.append(rr)  # rule line sitting near text = table border
            else:
                blanks.append({'c': '＿＿＿', 'x0': r.x0, 'x1': r.x1, 'y0': r.y0 - 11, 'y1': r.y0,
                               'yc': r.y0 - 5, 'font': 'blank', 'size': 10})
        elif w < 1.6 and h < 3:
            continue
        else:
            figures.append(rr)
    for im in page.get_image_info():
        x0, y0, x1, y1 = im['bbox']
        if (y1 - y0) < 14 and (x1 - x0) < 32 and im['width'] <= 64 and im['height'] <= 64:
            # 部分年度的選項標記（A、B、C、D）是小圖示而非字型；依出現順序給字母
            chars.append({'c': GENERIC_MARK, 'x0': x0, 'x1': x1, 'y0': y0, 'y1': y1,
                          'yc': (y0 + y1) / 2, 'font': 'img-mark', 'size': 10})
        else:
            figures.append((x0, y0, x1, y1))
    chars += blanks
    lines = group_lines(chars)
    out = []
    for ln in lines:
        txt = ''.join(c['c'] for c in ln)
        if in_header((ln[0]['x0'], ln[0]['y0'], ln[-1]['x1'], ln[-1]['y1'])) or re.match(r'^\s*(代號：|頁次：)', txt):
            continue
        out.append(ln)
    return out, figures


def join_text(a, b):
    """接續換行文字：英數字之間補一個空格，中文直接相連。"""
    if a and b and re.match(r'[A-Za-z0-9,.;:?!\'")\]’”]', a[-1]) and re.match(r'[A-Za-z0-9(\[‘“]', b[0]):
        return a + ' ' + b
    return a + b


def group_lines(chars):
    chars = sorted(chars, key=lambda c: (round(c['yc']), c['x0']))
    lines = []
    for c in chars:
        for ln in lines[-3:]:
            if abs(ln[0]['yc'] - c['yc']) < 4:
                ln.append(c)
                break
        else:
            lines.append([c])
    for ln in lines:
        ln.sort(key=lambda c: c['x0'])
    lines.sort(key=lambda ln: ln[0]['yc'])
    return lines


def text_of(chars):
    s, prev, inu = '', None, False
    for c in chars:
        if prev is not None and c['x0'] - prev['x1'] > 4 and not s.endswith(' '):
            # gap between ASCII words keeps a space; CJK gaps are layout only
            if re.match(r'[A-Za-z0-9(（]', c['c']) and re.match(r'[A-Za-z0-9.,)）]', prev['c']):
                s += ' '
        u = c.get('u', False)
        if u and not inu:
            s += '[u]'
        if inu and not u:
            s += '[/u]'
        inu = u
        s += c['c']
        prev = c
    if inu:
        s += '[/u]'
    return s


def parse_questions(pdf_path):
    doc = fitz.open(pdf_path)
    sizes = sorted(c['size'] for pg in doc for b in pg.get_text('rawdict')['blocks'] for l in b.get('lines', [])
                   for sp in l['spans'] for c in [sp] if ''.join(ch['c'] for ch in sp['chars']).strip())
    body_size = sizes[len(sizes) // 2] if sizes else 12
    qs, cur, target, opt_x = [], None, None, {}
    pending, section_multi = [], False
    started = False
    for pno, page in enumerate(doc):
        lines, figures = page_lines(page)
        for ln in lines:
            raw = ''.join(c['c'] for c in ln)
            y0, y1 = min(c['y0'] for c in ln), max(c['y1'] for c in ln)
            # question number at left margin
            m_num = None
            digits = ''
            for c in ln:
                if c['x0'] < 58 and c['c'] in '0123456789':
                    digits += c['c']
                else:
                    break
            expected = (qs[-1]['no'] + 1) if qs else 1
            if cur:
                expected = cur['no'] + 1
            if digits and int(digits) == expected:
                m_num = int(digits)
            if not started:
                if m_num == 1:
                    started = True
                else:
                    if '複選' in raw:
                        section_multi = True
                    continue
            if m_num:
                if cur:
                    qs.append(cur)
                cur = {'no': m_num, 'stem': '', 'options': {}, 'passage': None, 'multi': section_multi,
                       'page': pno, 'figure': False, 'y0': y0, 'pages': {pno}}
                body = ln[len(digits):]
                target, opt_x = 'stem', {}
                if pending:
                    cur['passage'] = '\n'.join(pending)
                    pending = []
                ln = body
            elif cur is None:
                continue
            # section header lines
            if not m_num and SECTION_RE.match(raw):
                if '複選' in raw:
                    section_multi = True
                if '單選' in raw:
                    section_multi = False
                target = 'section'
                continue
            if cur:
                cur['pages'].add(pno)
                if any(c['font'] not in ('blank', 'img-mark') and c['c'] not in MARK and c['size'] < body_size * 0.8
                       and not c['c'].isspace() for c in ln):
                    cur['subsup'] = True
                if any('\ue000' <= c['c'] <= '\uf8ff' and c['c'] not in MARK for c in ln):
                    cur['symbol'] = True
                for f in figures:
                    if f[1] < y1 + 2 and f[3] > y0 - 2 and f[0] < 560 and f[2] > 30:
                        cur['figure'] = True
            # split by markers
            segs, buf = [], []
            marker = None
            for c in ln:
                if c['c'] in MARK:
                    segs.append((marker, buf))
                    letter = MARK[c['c']]
                    if letter == '?':
                        used = len(cur['options']) + sum(1 for sg in segs[1:] if sg[0])
                        letter = 'ABCDE'[used] if used < 5 else 'Z'
                    marker, buf = (letter, c['x0']), []
                else:
                    buf.append(c)
            segs.append((marker, buf))
            first_marker, first_buf = segs[0]
            if target == 'section' and not m_num and len(segs) == 1:
                t = text_of(first_buf)
                if re.search(r'答對|答錯|零分|配分|每題|作答|選出', t) and not GROUP_RE.search(t):
                    continue
                target = 'pending'
            if first_buf:
                x0 = first_buf[0]['x0']
                t = text_of(first_buf).strip()
                if target in ('stem',) or m_num:
                    if target == 'pending':
                        pending.append(t)
                    else:
                        cur['stem'] = join_text(cur['stem'], t)
                elif target == 'pending':
                    pending.append(t)
                else:
                    # continuation of an option, or start of a passage for later questions
                    opt_text_x = opt_x.get(target, (0, 0))[1]
                    if len(segs) == 1 and x0 < opt_text_x - 6:
                        target = 'pending'
                        pending.append(t)
                    else:
                        # pick option column by x
                        cands = [(k, v) for k, v in opt_x.items() if v[2] == opt_x[target][2] and v[0] <= x0 + 4]
                        key = max(cands, key=lambda kv: kv[1][0])[0] if cands else target
                        cur['options'][key] = join_text(cur['options'][key], t)
            for mk, buf in segs[1:]:
                letter, mx = mk
                tx = buf[0]['x0'] if buf else mx + 10
                opt_x[letter] = (mx, tx, round(y0))
                cur['options'][letter] = cur['options'].get(letter, '') + text_of(buf).strip()
                target = letter
    if cur:
        qs.append(cur)
    # resolve passages for grouped questions
    for i, q in enumerate(qs):
        if q['passage']:
            m = GROUP_RE.search(q['passage'])
            if m:
                a, b = int(m.group(1)), int(m.group(2))
                for q2 in qs:
                    if a <= q2['no'] <= b and q2 is not q:
                        q2['passage'] = q2['passage'] or q['passage']
    return qs


def parse_answers(pdf_path):
    doc = fitz.open(pdf_path)
    words, full = [], ''
    for page in doc:
        full += page.get_text()
        words += [w for w in page.get_text('words')]
    # 題號列：新版「第N題」；舊版（101–103 年）「題號 01 02 …」
    rows = {}
    for w in words:
        x0, y0, x1, y1, t = w[:5]
        m = re.match(r'^第(\d+)題$', t)
        if m:
            rows.setdefault(round(y0), []).append((int(m.group(1)), (x0 + x1) / 2))
    for w in words:
        if w[4] == '題號':
            y = round(w[1])
            nums = [(int(v[4]), (v[0] + v[2]) / 2) for v in words
                    if abs(v[1] - w[1]) < 3 and v[0] > w[2] and re.fullmatch(r'\d{1,3}', v[4])]
            if nums:
                rows.setdefault(y, []).extend(nums)
    ans = {}
    for y, cols in rows.items():
        below = [w for w in words if 4 < w[1] - y < 22 and w[4] not in ('答案',)]
        for w in below:
            if not re.fullmatch(r'[A-E#]+|[A-E]或[A-E]', w[4]):
                continue
            cx = (w[0] + w[2]) / 2
            no, _ = min(cols, key=lambda c: abs(c[1] - cx))
            ans[no] = w[4]
    note = ''
    m = re.search(r'備\s*註\s*：(.*)', full, re.S)
    if m:
        note = re.sub(r'\s+', ' ', m.group(1)).strip()
    meta = {}
    for k, pat in [('single', r'單選題數：\s*(\d+)'), ('multi', r'複選題數：\s*(\d+)')]:
        mm = re.search(pat, full)
        meta[k] = int(mm.group(1)) if mm else 0
    if meta['single'] == 0 and meta['multi'] == 0:
        mm = re.search(r'題\s*數：\s*(\d+)\s*題', full)
        if mm:
            meta['single'] = int(mm.group(1))
    return ans, note, meta


def clean(s):
    s = re.sub(r'\[/u\]\[u\]', '', s)
    s = re.sub(r'\[u\]\s*\[/u\]', '＿＿＿', s)
    s = re.sub(r'\s+', ' ', s).strip()
    s = re.sub(r'(?<=[\u2e80-\u9fff\u3000-\u303f\uff00-\uffef])\s+(?=[\u2e80-\u9fff\u3000-\u303f\uff00-\uffef])', '', s)
    s = re.sub(r'(?<=[A-Za-z,.;])＿＿＿', ' ＿＿＿', s)
    s = re.sub(r'＿＿＿(?=[A-Za-z])', '＿＿＿ ', s)
    return s


def build(qpdf, spdf):
    qs = parse_questions(qpdf)
    ans, note, meta = parse_answers(spdf)
    total = meta['single'] + meta['multi']
    good, bad = [], []
    for q in qs:
        reason = None
        letters = sorted(q['options'])
        a = ans.get(q['no'], '')
        if q['figure']:
            reason = '含圖表，無法以純文字完整呈現'
        elif q.get('subsup') or q.get('symbol'):
            reason = '含上下標、數學式或特殊符號，純文字無法正確呈現'
        elif not q['stem'].strip() and not q['passage']:
            reason = '題幹為空'
        elif letters not in (['A', 'B', 'C', 'D'], ['A', 'B', 'C', 'D', 'E']):
            reason = f'選項數不符：{letters}'
        elif any(not q['options'][k].strip() for k in letters):
            reason = '有空白選項'
        elif not re.fullmatch(r'[A-E]+', a) or any(ch not in letters for ch in a):
            reason = f'標準答案非單純選項（{a or "無"}），可能為送分或更正題'
        if reason:
            bad.append({'no': q['no'], 'reason': reason})
            continue
        is_multi = q['no'] > meta['single'] if meta['multi'] else False
        if len(a) > 1 and not is_multi:
            bad.append({'no': q['no'], 'reason': f'單選題對到多個答案（{a}）'})
            continue
        good.append({'no': q['no'], 'type': 'multi' if is_multi else 'single',
                     'passage': clean(q['passage']) if q['passage'] else None,
                     'stem': clean(q['stem']),
                     'options': {k: clean(q['options'][k]) for k in letters},
                     'answer': a})
    found = {q['no'] for q in qs}
    for n in range(1, total + 1):
        if n not in found:
            bad.append({'no': n, 'reason': '解析時未找到此題'})
    return {'total': total, 'questions': good, 'excluded': sorted(bad, key=lambda b: b['no']), 'answer_note': note}


if __name__ == '__main__':
    r = build(sys.argv[1], sys.argv[2])
    print(json.dumps({'total': r['total'], 'ok': len(r['questions']), 'excluded': r['excluded']}, ensure_ascii=False))
    if len(sys.argv) > 3:
        json.dump(r, open(sys.argv[3], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
