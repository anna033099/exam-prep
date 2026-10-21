"""解析台灣金融研訓院公告的中華郵政甄試考古題 PDF（A3 橫式、雙欄、答案標在題號前【n】）。

與考選部解析器相同原則：只輸出能完整還原的四選一題目；含表格、圖片、上下標、
特殊符號或選項數不符者一律排除並記錄原因。
"""
import re, sys, json
import pymupdf as fitz

MARKS = {'\x81': 'A', '\x82': 'B', '\x83': 'C', '\x84': 'D',
         '': 'A', '': 'B', '': 'C', '': 'D'}
SYMBOL_MAP = {'': '→', '': '－', '': '＋', '': '＝'}
Q_RE = re.compile(r'^【(改)?([1-4])】\s*(\d+)\s*[\.．]?\s*')
SECTION_RE = re.compile(r'^\s*([壹貳參肆伍]、|[一二三四五六七八九十]、|第[一二三四五六七八九十]+篇[：:]|第[一二三四五六七八九十]+部分|【第\s*\d+\s*[-~～至]\s*\d+\s*題|Questions?\s+\d+)')


def chars_of(page):
    out = []
    for b in page.get_text('rawdict')['blocks']:
        for l in b.get('lines', []):
            for s in l['spans']:
                for c in s['chars']:
                    ch = c['c']
                    if 'Symbol' in s['font'] and ch in SYMBOL_MAP:
                        ch = SYMBOL_MAP[ch]
                    if ch != ' ' and not ch.strip():
                        continue
                    x0, y0, x1, y1 = c['bbox']
                    out.append({'c': ch, 'x0': x0, 'x1': x1, 'y0': y0, 'y1': y1, 'yc': (y0 + y1) / 2,
                                'size': s['size'], 'font': s['font']})
    return out


def join_text(a, b):
    """接續換行文字：英數字之間補一個空格，中文直接相連。"""
    if a and b and re.match(r'[A-Za-z0-9,.;:?!\'")\]’”]', a[-1]) and re.match(r'[A-Za-z0-9(\[‘“]', b[0]):
        return a + ' ' + b
    return a + b


def lines_of(chars):
    chars = sorted(chars, key=lambda c: (c['yc'], c['x0']))
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
    return sorted(lines, key=lambda ln: ln[0]['yc'])


def text_of(chars):
    chars = [c for i, c in enumerate(chars) if c['c'] != ' ' or (i > 0 and chars[i - 1]['c'] != ' ')]
    s, prev, inu = '', None, False
    for c in chars:
        u = c.get('u', False)
        if u and not inu:
            s += '[u]'
        if inu and not u:
            s += '[/u]'
        inu = u
        if prev is not None and prev['c'] != ' ' and c['c'] != ' ' and c['x0'] - prev['x1'] > 3 and re.match(r'[A-Za-z0-9(]', c['c']) \
                and re.match(r'[A-Za-z0-9.,)?!:;\'"]', prev['c']):
            s += ' '
        s += c['c']
        prev = c
    if inu:
        s += '[/u]'
    return s


def regions(page, chars):
    """回傳 (圖表範圍, 填空底線字元)。白色底色忽略；細橫線依上方是否有字判斷為底線或填空。"""
    figs, blanks = [], []
    for dr in page.get_drawings():
        r = dr['rect']
        w, h = r.x1 - r.x0, r.y1 - r.y0
        fill, stroke = dr.get('fill'), dr.get('color')
        if fill and all(v > 0.95 for v in fill) and not stroke:
            continue  # 白色底色
        if h < 1.8 and w > 6:
            over = [c for c in chars if c['x1'] > r.x0 + 1 and c['x0'] < r.x1 - 1 and -1.8 <= r.y0 - c['y1'] <= 1.8]
            near = [c for c in chars if c['x1'] > r.x0 + 1 and c['x0'] < r.x1 - 1 and -1.8 <= r.y0 - c['y1'] <= 4.5]
            if over:
                for c in over:
                    c['u'] = True
            elif near:
                figs.append((r.x0, r.y0, r.x1, r.y1))
            else:
                blanks.append({'c': '＿＿＿', 'x0': r.x0, 'x1': r.x1, 'y0': r.y0 - 11, 'y1': r.y0, 'yc': r.y0 - 5,
                               'size': 0, 'font': 'blank'})
            continue
        if w < 1.8 and h < 3:
            continue
        figs.append((r.x0, r.y0, r.x1, r.y1))
    for im in page.get_image_info():
        figs.append(tuple(im['bbox']))
    return figs, blanks


def parse(pdf):
    doc = fitz.open(pdf)
    all_sizes = sorted(c['size'] for p in doc for c in chars_of(p))
    body = all_sizes[len(all_sizes) // 2] if all_sizes else 10
    qs, cur, target, opt_x = [], None, None, {}
    pending, passage = [], None
    started, in_section, reading = False, False, False
    for page in doc:
        mid = page.rect.width / 2
        chars = chars_of(page)
        figs, blanks = regions(page, chars)
        chars += blanks
        for col in (0, 1):
            cc = [c for c in chars if (c['x0'] < mid) == (col == 0)]
            for ln in lines_of(cc):
                raw = ''.join(c['c'] for c in ln)
                if not raw.strip() or re.fullmatch(r'\s*【請(接續|翻)背面】\s*|\s*第\s*\d+\s*頁.*', raw):
                    continue
                y0, y1 = min(c['y0'] for c in ln), max(c['y1'] for c in ln)
                x_left = ln[0]['x0']
                m = Q_RE.match(raw)
                expected = (cur['no'] + 1) if cur else 1
                if m and int(m.group(3)) == expected:
                    started = True
                    if cur:
                        qs.append(cur)
                    if pending:
                        if reading:
                            passage = '\n'.join(pending)
                        elif cur:
                            cur['figure'] = True  # 題目後方出現無法歸屬的文字：多為表格內容
                        pending = []
                    cur = {'no': expected, 'answer': 'ABCD'[int(m.group(2)) - 1], 'corrected': bool(m.group(1)),
                           'stem': '', 'options': {}, 'passage': passage, 'figure': False, 'odd': False}
                    # 去掉【n】題號. 前綴字元
                    prefix_len = len(m.group(0).rstrip())
                    consumed, body_chars = 0, []
                    for c in ln:
                        if consumed < prefix_len:
                            consumed += len(c['c'])
                            continue
                        body_chars.append(c)
                    ln = body_chars
                    target, opt_x = 'stem', {}
                    stem_x = ln[0]['x0'] if ln else x_left
                elif SECTION_RE.match(raw):
                    in_section = True
                    reading = bool(re.search(r'閱讀|克漏字|篇|文意|下文|段落|題組', raw))
                    passage, pending, target = None, [], 'pending'
                    if re.search(r'第[一二三四五六七八九十]+篇', raw):
                        rest = re.sub(r'^\s*第[一二三四五六七八九十]+篇[：:]\s*', '', raw).strip()
                        if rest:
                            pending.append(rest)
                    continue
                elif not started and not in_section:
                    continue
                # 圖表、上下標、特殊符號
                if cur and target != 'pending':
                    for f in figs:
                        if f[1] < y1 + 2 and f[3] > y0 - 2 and f[0] < ln[-1]['x1'] + 2 and f[2] > ln[0]['x0'] - 2 if ln else False:
                            cur['figure'] = True
                    if any(c['font'] != 'blank' and c['size'] < body * 0.8 for c in ln) or any('' <= c['c'] <= '' and c['c'] not in MARKS for c in ln):
                        cur['odd'] = True
                # 依選項標記切段
                segs, buf, mk = [], [], None
                for c in ln:
                    if c['c'] in MARKS and cur is not None and target != 'pending':
                        segs.append((mk, buf))
                        mk, buf = (MARKS[c['c']], c['x0']), []
                    else:
                        buf.append(c)
                segs.append((mk, buf))
                first = segs[0][1]
                if first:
                    t = text_of(first).strip()
                    if target == 'pending' or cur is None:
                        pending.append(t)
                    elif target == 'stem':
                        cur['stem'] = join_text(cur['stem'], t)
                    else:
                        # 選項換行：依欄位 x 位置歸給同一列最接近左側的選項
                        x0 = first[0]['x0']
                        row = opt_x[target][2]
                        cands = [(k, v) for k, v in opt_x.items() if v[2] == row and v[0] <= x0 + 4]
                        if len(segs) == 1 and x0 < opt_x[target][1] - 6:
                            # 比選項文字更靠左的行：視為下一段文章
                            pending.append(t)
                            target = 'pending'
                        else:
                            key = max(cands, key=lambda kv: kv[1][0])[0] if cands else target
                            cur['options'][key] = join_text(cur['options'][key], t)
                for mk2, b2 in segs[1:]:
                    letter, mx = mk2
                    if letter in cur['options']:
                        cur['odd'] = True  # 同一題出現重複的選項標記（題幹內的①②③）
                    tx = b2[0]['x0'] if b2 else mx + 10
                    opt_x[letter] = (mx, tx, round(y0))
                    cur['options'][letter] = cur['options'].get(letter, '') + text_of(b2).strip()
                    target = letter
    if cur:
        qs.append(cur)
    return qs


def clean(s):
    s = re.sub(r'\[u\]\s*\[/u\]', '＿＿＿', s or '')
    s = re.sub(r'\s+', ' ', s).strip()
    return re.sub(r'(?<=[\u2e80-\u9fff\u3000-\u303f\uff00-\uffef])\s+(?=[\u2e80-\u9fff\u3000-\u303f\uff00-\uffef])', '', s)


def build(pdf):
    qs = parse(pdf)
    good, bad = [], []
    for q in qs:
        letters = sorted(q['options'])
        reason = None
        if q['figure']:
            reason = '含圖表，無法以純文字完整呈現'
        elif q['odd']:
            reason = '含上下標、數學式或特殊符號，純文字無法正確呈現'
        elif letters != ['A', 'B', 'C', 'D']:
            reason = f'選項數不符：{letters}'
        elif any(not q['options'][k].strip() for k in letters):
            reason = '有空白選項'
        elif not q['stem'].strip() and not q['passage']:
            reason = '題幹為空'
        elif any(len(v) > 220 for v in q['options'].values()):
            reason = '選項過長，可能誤併入下一段文章'
        if reason:
            bad.append({'no': q['no'], 'reason': reason})
            continue
        good.append({'no': q['no'], 'type': 'single', 'passage': clean(q['passage']) or None,
                     'stem': clean(q['stem']), 'options': {k: clean(v) for k, v in q['options'].items()},
                     'answer': q['answer'], 'corrected': q['corrected']})
    return {'total': len(qs), 'questions': good, 'excluded': bad}


if __name__ == '__main__':
    r = build(sys.argv[1])
    print(json.dumps({'total': r['total'], 'ok': len(r['questions']), 'excluded': r['excluded']}, ensure_ascii=False))
    for q in r['questions'][:int(sys.argv[2]) if len(sys.argv) > 2 else 3]:
        print(q['no'], q['answer'], (q['passage'] or '')[:60], '|', q['stem'][:80], q['options'])
