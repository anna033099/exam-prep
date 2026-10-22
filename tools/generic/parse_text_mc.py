"""通用的文字型選擇題 PDF 解析器（國營事業甄試、金融證照等非考選部試卷）。

這類試卷由 Word 轉成 PDF，題號是「1.」「2.」…，選項是「(A)」「(1)」「A.」等一般文字。
答案有兩種放法：題號前的「[A] 1.」「【A】1.」，或另外一張答案表（由呼叫端讀出後傳入）。

只輸出通過檢查的題目：題號連續、選項 4–5 個且依序出現、沒有圖表與上下標、答案是單純選項；
其餘題目一律排除並記錄原因。題目文字之後還會由呼叫端逐題和 pdftotext 讀出的原卷文字比對。
"""
import re, os, sys
import pymupdf as fitz

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from textnorm import canon

OPT_RE = re.compile(r'[\(（]\s*([A-Ea-e1-5])\s*[\)）]|(?:(?<=\s)|^)([A-E])\s*[.．](?=\s*\S)')
Q_RE = re.compile(r'^\s*(?:[\[【［]\s*([A-E1-5#＃]|[A-E](?:或|,|、)[A-E])\s*[\]】］])?\s*(\d{1,3})\s*[.．、](?!\d)\s*(.*)$')
SECTION_RE = re.compile(r'^\s*([一二三四五六七八九十壹貳參肆伍陸柒捌玖拾]+、|[甲乙丙丁]、|第\s*\d+\s*頁|【?請翻頁】?|（?請接背面）?|科目[:：]|類別[:：])')
PAGE_HEAD_RE = re.compile(r'第\s*\d+\s*頁\s*[,，]?\s*共\s*\d+\s*頁')
LETTER = {'1': 'A', '2': 'B', '3': 'C', '4': 'D', '5': 'E'}


def find_gutter(page):
    """A3 雙欄排版：找頁面中間沒有任何文字跨過的直線位置；單欄回傳 None。"""
    w = page.rect.width
    lines = [l['bbox'] for b in page.get_text('dict')['blocks'] for l in b.get('lines', [])
             if ''.join(s['text'] for s in l['spans']).strip()]
    if len(lines) < 10:
        return None
    best = None
    for x in range(int(w * 0.35), int(w * 0.65), 4):
        cross = sum(1 for b in lines if b[0] < x - 2 and b[2] > x + 2)
        left = sum(1 for b in lines if b[2] <= x)
        right = sum(1 for b in lines if b[0] >= x)
        if cross <= max(1, len(lines) * 0.02) and left >= 5 and right >= 5:
            score = (cross, abs(x - w / 2))
            if best is None or score < best[0]:
                best = (score, x)
    return best[1] if best else None


def lines_of(doc):
    """回傳 [(page, y0, y1, x0, text, min_size, body_size_flag)]，依頁、y、x 排序並合併同一行。"""
    out = []
    sizes = []
    for pno, page in enumerate(doc):
        gutter = find_gutter(page)
        d = page.get_text('dict')
        for b in d['blocks']:
            for l in b.get('lines', []):
                spans = [s for s in l['spans'] if s['text'].strip()]
                if not spans:
                    continue
                t = ''.join(s['text'] for s in l['spans'])
                x0, y0, x1, y1 = l['bbox']
                # 底線、標點的字級常比較小，不算上下標；只看含文字或數字的片段
                real = [s for s in spans if re.search(r'[A-Za-z0-9\u4e00-\u9fff]', s['text'])]
                col = 1 if gutter and x0 >= gutter else 0
                out.append([pno * 2 + col, y0, y1, x0, t, min((s['size'] for s in real), default=99), x1])
                sizes += [s['size'] for s in spans for _ in s['text'].strip()]
    out.sort(key=lambda r: (r[0], (r[1] + r[2]) / 2))
    groups = []
    for r in out:
        g = groups[-1] if groups else None
        if g and g[0][0] == r[0] and abs((g[0][1] + g[0][2]) / 2 - (r[1] + r[2]) / 2) < 3.5:
            g.append(r)
        else:
            groups.append([r])
    merged = []
    for g in groups:
        g.sort(key=lambda r: r[3])
        text, prev_x1 = '', None
        for r in g:
            sep = ' ' if prev_x1 is not None and r[3] - prev_x1 > 4 else ''
            text = (text.rstrip() + sep + r[4].lstrip()) if text else r[4]
            prev_x1 = r[6]
        merged.append([g[0][0], min(r[1] for r in g), max(r[2] for r in g), g[0][3], text,
                       min(r[5] for r in g), max(r[6] for r in g)])
    sizes.sort()
    body = sizes[len(sizes) // 2] if sizes else 11
    return merged, body


def figures_of(doc):
    """各頁（雙欄時各欄）圖片與線條圖的垂直範圍。鍵與 lines_of 相同：頁碼×2＋欄。"""
    figs = {}
    for pno, page in enumerate(doc):
        gutter = find_gutter(page)
        rects = []
        for im in page.get_image_info():
            x0, y0, x1, y1 = im['bbox']
            if (x1 - x0) > 20 and (y1 - y0) > 12:
                rects.append((x0, y0, x1, y1))
        for dr in page.get_drawings():
            r = dr['rect']
            fill = dr.get('fill')
            if dr.get('color') is None and fill is not None and min(fill) > 0.95:
                continue
            if (r.y1 - r.y0) > 6 and (r.x1 - r.x0) > 6:
                rects.append((r.x0, r.y0, r.x1, r.y1))
        for x0, y0, x1, y1 in rects:
            cols = [0] if not gutter else ([0] if x1 <= gutter + 2 else [1] if x0 >= gutter - 2 else [0, 1])
            for c in cols:
                figs.setdefault(pno * 2 + c, []).append((y0, y1))
    return figs


def join(a, b):
    if a and b and re.match(r'[A-Za-z0-9,.;:?!\'")\]_]', a[-1]) and re.match(r'[A-Za-z0-9(\[]', b[0]):
        return a + ' ' + b
    return a + b


def clean(s):
    s = canon(s)
    s = re.sub(r'\s+', ' ', s).strip()
    s = re.sub(r'(?<=[⺀-鿿　-〿＀-￯])\s+(?=[⺀-鿿　-〿＀-￯])', '', s)
    return s


def ok_marker(m, markers):
    raw = (m.group(1) or m.group(2)).upper()
    return not (markers == 'num' and not raw.isdigit() or markers == 'alpha' and raw.isdigit())


def paragraphs(items):
    """把排版換行接回段落：上一行寫到接近右邊界就接續，否則保留換行（原卷分段）。"""
    if not items:
        return None
    right = max(x for _, x in items)
    text = ''
    for i, (t, x) in enumerate(items):
        if i == 0:
            text = t
        elif items[i - 1][1] < right - 30:
            text += '\n' + t
        else:
            text = join(text.rstrip(), t.lstrip())
    return text


def clean_keep(s):
    if not s:
        return None
    return '\n'.join(clean(x) for x in s.split('\n') if x.strip())


def parse(pdf_path, answers=None, markers=None, multi=None):
    """answers：{題號: 'A'}（答案表讀出）；None 時使用題號前的 [A] 標記。
    回傳 {'total', 'questions', 'excluded'}。"""
    doc = fitz.open(pdf_path)
    lines, body = lines_of(doc)
    figs = figures_of(doc)
    qs, cur, expected = [], None, 1
    pending, pending_small, section = [], False, 0
    for pno, y0, y1, x0, text, msize, x1 in lines:
        t = canon(text).strip()
        if not t or PAGE_HEAD_RE.search(t):
            continue
        m = Q_RE.match(t)
        if m and int(m.group(2)) == 1 and expected > 1 and not any(q['options'] for q in qs + ([cur] if cur else [])):
            # 前面的「1. 2. 3.」是注意事項，不是題目：從這裡重新開始
            qs, cur, expected = [], None, 1
        if m and int(m.group(2)) == expected:
            if cur:
                qs.append(cur)
            cur = {'no': expected, 'mark': m.group(1), 'stem': '', 'options': {}, 'target': 'stem',
                   'pages': [(pno, y0, y1)], 'small': pending_small, 'passage': paragraphs(pending),
                   'section': section}
            prev = qs[-1] if qs else None
            if not pending and prev and prev.get('passage') and prev['section'] == section:
                cur['passage'] = prev['passage']   # 同一篇文章的後續題目
                cur['small'] = cur['small'] or prev.get('passage_small', False)
            cur['passage_small'] = pending_small if pending else (prev.get('passage_small', False) if cur['passage'] and prev else False)
            pending, pending_small = [], False
            expected += 1
            t = m.group(3)
        elif cur is None:
            continue
        elif SECTION_RE.match(t) and not (OPT_RE.match(t) and ok_marker(OPT_RE.match(t), markers)):
            cur['target'] = 'closed'
            pending, pending_small = [], False
            section += 1
            continue
        elif (len(cur['options']) >= 4 and not any(ok_marker(o, markers) for o in OPT_RE.finditer(t))
              and (cur['target'] == 'closed' or x0 < cur.get('opt_line_x', 0) - 2)):
            # 選項都出現後，又回到左邊界的文字：下一組題目的題組文章（克漏字、閱讀測驗）
            cur['target'] = 'closed'
            pending.append((t, x1))
            if msize < body * 0.8:
                pending_small = True
            continue
        cur['pages'].append((pno, y0, y1))
        if msize < body * 0.8:
            cur['small'] = True
        # 依序切出選項
        pos, parts = 0, []
        for om in OPT_RE.finditer(t):
            raw = (om.group(1) or om.group(2)).upper()
            if markers == 'num' and not raw.isdigit() or markers == 'alpha' and raw.isdigit():
                continue   # 選項記號只認一種（例如 (1)–(4)），題幹裡的 (A)(B) 小項不當選項
            key = LETTER.get(raw, raw)
            nxt = 'ABCDE'[len(cur['options']) + len(parts)] if len(cur['options']) + len(parts) < 5 else None
            if key != nxt:
                continue
            parts.append((key, om.start(), om.end()))
        if parts:
            head = t[:parts[0][1]].strip()
            if head:
                if cur['target'] == 'stem':
                    cur['stem'] = join(cur['stem'], head)
                elif cur['target'] in cur['options']:
                    cur['options'][cur['target']] = join(cur['options'][cur['target']], head)
            for i, (key, s, e) in enumerate(parts):
                end = parts[i + 1][1] if i + 1 < len(parts) else len(t)
                cur['options'][key] = t[e:end].strip()
                cur['target'] = key
            cur['opt_line_x'] = x0
        elif cur['target'] == 'stem':
            cur['stem'] = join(cur['stem'], t)
        elif cur['target'] in cur['options']:
            cur['options'][cur['target']] = join(cur['options'][cur['target']], t)
    if cur:
        qs.append(cur)
    good, bad = [], []
    for q in qs:
        letters = sorted(q['options'])
        a = answers.get(q['no']) if answers is not None else q['mark']
        a = LETTER.get(a, a) if a else a
        ys = {}
        for p, a0, a1 in q['pages']:
            lo, hi = ys.get(p, (a0, a1))
            ys[p] = (min(lo, a0), max(hi, a1))
        fig = any(f0 < hi + 2 and f1 > lo - 2 for p, (lo, hi) in ys.items() for f0, f1 in figs.get(p, []))
        reason = None
        if fig:
            reason = '含圖表，無法以純文字完整呈現'
        elif q['small']:
            reason = '含上下標、數學式或特殊符號，純文字無法正確呈現'
        elif any('' <= ch <= '' for ch in q['stem'] + ''.join(q['options'].values())):
            reason = '含特殊符號，純文字無法正確呈現'
        elif not q['stem'].strip() and not q.get('passage'):
            reason = '題幹為空'
        elif letters not in (['A', 'B', 'C', 'D'], ['A', 'B', 'C', 'D', 'E']):
            reason = f'選項數不符：{letters}'
        elif any(not q['options'][k].strip() for k in letters):
            reason = '有空白選項'
        elif not a or not re.fullmatch(r'[A-E]{1,5}', a) or any(ch not in letters for ch in a):
            reason = f'標準答案非單純選項（{a or "無"}），可能為送分或更正題'
        if reason:
            bad.append({'no': q['no'], 'reason': reason})
            continue
        good.append({'no': q['no'], 'type': 'multi' if len(a) > 1 or q['no'] in (multi or set()) else 'single',
                     'passage': clean_keep(q.get('passage')), 'stem': clean(q['stem']),
                     'options': {k: clean(q['options'][k]) for k in letters}, 'answer': a})
    return {'total': len(qs), 'questions': good, 'excluded': bad}


ESSAY_RE = re.compile(r'^\s*([一二三四五六七八九十]{1,3})、\s*(.*)$')
CN = '一二三四五六七八九十'


def parse_essays(pdf_path):
    """非選擇題：「一、」「二、」…各題的文字。注意事項之後才開始找。"""
    doc = fitz.open(pdf_path)
    lines, body = lines_of(doc)
    figs = figures_of(doc)
    out, cur = [], None
    started = False
    for pno, y0, y1, x0, text, msize, x1 in lines:
        t = canon(text).strip()
        if not t or PAGE_HEAD_RE.search(t):
            continue
        if not started:
            if re.search(r'考試時間|作答時須|注意事項|事\s*$', t):
                started = True
            continue
        m = ESSAY_RE.match(t)
        if m and CN.index(m.group(1)[-1]) + 1 == (cur['no'] + 1 if cur else 1) and len(m.group(1)) == 1:
            if cur:
                out.append(cur)
            cur = {'no': CN.index(m.group(1)) + 1, 'lines': [t], 'pages': [(pno, y0, y1)], 'small': False}
            continue
        if cur:
            if re.match(r'^[【（(]?(請翻頁|請接背面|背面尚有試題)', t):
                continue
            cur['lines'].append(t)
            cur['pages'].append((pno, y0, y1))
            if msize < body * 0.8:
                cur['small'] = True
    if cur:
        out.append(cur)
    res = []
    for e in out:
        ys = {}
        for p, a0, a1 in e['pages']:
            lo, hi = ys.get(p, (a0, a1))
            ys[p] = (min(lo, a0), max(hi, a1))
        fig = any(f0 < hi + 2 and f1 > lo - 2 for p, (lo, hi) in ys.items() for f0, f1 in figs.get(p, []))
        text = ''
        for i, l in enumerate(e['lines']):
            if i and re.match(r'^([（(][一二三四五六七八九十0-9]+[）)]|\d+[.、]|[①②③④⑤])', l):
                text += '\n' + l
            else:
                text = join(text, l)
        res.append({'no': e['no'], 'text': '\n'.join(clean(x) for x in text.split('\n')), 'figure': fig, 'scripts': e['small']})
    return res
