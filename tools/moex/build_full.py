"""把 download_full.py 下載的考選部試題批次解析成網站題庫包（全部考試、全部類科、含申論題）。

每份試卷：
- 有測驗式標準答案者：用 parse_exam.build 解析選擇題，逐題文字比對原卷（poppler 兩種模式＋PyMuPDF），
  對不上的題目排除並註明原因。
- 申論題：parse_exam.parse_essays 取出「一、二、三…」各題，文字同樣逐題比對原卷；含圖表或特殊符號的
  題目照收但加註「請對照原卷」。
- 考試類別：taxonomy.classify 依類科判斷；判斷不了時讀試卷第一頁的「等別」「類科」。

輸出：<out>/papers_<考試代碼>.json.gz，以及 build_full_report.json（各卷收錄／排除統計）。
用法：python build_full.py <輸出資料夾> [代碼前綴...] [--workers 4]
"""
import collections, gzip, json, os, re, subprocess, sys
from concurrent.futures import ProcessPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from parse_exam import build, parse_essays
from taxonomy import classify_header, CATEGORIES
from textnorm import canon
import pymupdf as fitz

GARBLED = re.compile(r'([\uac00-\ud7af])\1\1')
SYM = {'': '－', '': '＋', '': '＝'}


def norm(s):
    s = canon(s or '')
    s = s.replace('[u]', '').replace('[/u]', '').replace('＿＿＿', '')
    s = re.sub(r'[①②③④□]', '', s)
    return re.sub(r'[\s-]', '', s)


def poppler_text(pdf):
    t = ''
    for mode in ([], ['-layout']):
        out = subprocess.run(['pdftotext', *mode, pdf, '-'], capture_output=True, text=True).stdout
        out = '\n'.join(l for l in out.splitlines() if not re.search(r'代號[:：]|頁次[:：]', l))
        for k, v in SYM.items():
            out = out.replace(k, v)
        t += norm(out) + '\n'
    blocks = ''.join(b[4] for pg in fitz.open(pdf) for b in pg.get_text('blocks') if not re.search('代號：|頁次：', b[4]))
    for k, v in SYM.items():
        blocks = blocks.replace(k, v)
    return t + '\n' + norm(blocks)


def answers_layout(pdf):
    """第二種讀法：用 pdftotext -layout 逐行讀「題號／答案」表，和 parse_exam 的座標讀法交叉核對。"""
    import unicodedata
    t = subprocess.run(['pdftotext', '-layout', pdf, '-'], capture_output=True, text=True).stdout.splitlines()
    t = [unicodedata.normalize('NFKC', l) for l in t]
    out = {}
    for i, line in enumerate(t):
        nums = re.findall(r'第(\d+)題', line) or (re.findall(r'\b(\d{1,3})\b', line) if re.match(r'題[號序]', line.strip()) else [])
        if nums and i + 1 < len(t) and t[i + 1].strip().startswith('答案'):
            vals = t[i + 1].strip()[2:].split()
            for n, v in zip(nums, vals):
                out[int(n)] = v
    return out


def process(m):
    """解析一份試卷，回傳 (paper dict 或 None, 報告列)。單份試卷出錯不影響其他試卷。"""
    try:
        return _process(m)
    except Exception as e:
        return None, {'key': [m['exam_code'], m['s']], 'error': f'解析失敗：{type(e).__name__}: {e}'}


def _process(m):
    try:
        doc = fitz.open(m['q_pdf'])
        first = doc[0].get_text()
    except Exception as e:  # PDF 損壞
        return None, {'key': [m['exam_code'], m['s']], 'error': f'無法開啟：{e}'}
    code = re.search(r'代\s*號[：:]\s*([0-9A-Za-z]+)', first)
    code = code.group(1) if code else f"{m['c']}-{m['s']}"
    slugs = list(m['slugs'])
    if not slugs:
        h = classify_header(canon(first))
        slugs = [h] if h else ['special_other']
    full = poppler_text(m['q_pdf'])
    questions, excluded, total, note = [], [], 0, ''
    ans_mismatch = []
    if m.get('a_pdf') and os.path.exists(m['a_pdf']):
        r = build(m['q_pdf'], m['a_pdf'])
        total, note = r['total'], r['answer_note']
        excluded = r['excluded']
        ans2 = answers_layout(m['a_pdf'])
        for q in r['questions']:
            parts = [q['stem'], *q['options'].values(), q['passage'] or '']
            if not all(norm(x) in full for x in parts if x):
                excluded.append({'no': q['no'], 'reason': '含上下標、數學式或特殊排版，純文字無法正確呈現'})
            elif GARBLED.search(''.join(x for x in parts if x)):
                # 字型對應錯誤時兩種讀法會讀出同樣的亂碼，逐字比對抓不到；韓文同一音節連續三次以上視為亂碼
                excluded.append({'no': q['no'], 'reason': '原卷文字編碼異常（字型對應錯誤），無法正確呈現'})
            elif ans2.get(q['no']) != q['answer']:
                excluded.append({'no': q['no'], 'reason': '標準答案以兩種方法讀取結果不一致，為避免錯誤不收錄'})
                ans_mismatch.append(q['no'])
            else:
                questions.append(q)
        excluded.sort(key=lambda e: e['no'])
    essays = []
    has_essay_part = not m.get('a_pdf') or re.search(r'申論|作文|甲、|非選擇題', canon(''.join(p.get_text() for p in doc)))
    essay_excluded = []
    if has_essay_part:
        for e in parse_essays(m['q_pdf']):
            if not e['text'] or len(norm(e['text'])) < 6:
                continue
            if e.get('scripts'):
                essay_excluded.append({'no': e['no'], 'reason': '含上下標或數學式，純文字無法正確呈現'})
                continue
            if norm(e['text']) not in full:
                essay_excluded.append({'no': e['no'], 'reason': '含圖表、公式或特殊排版，文字與原卷比對不一致'})
                continue
            remark = None
            if e['figure']:
                remark = '本題含圖表，請對照原卷 PDF'
            elif e['subsup'] or '□' in e['text']:
                remark = '本題含上下標或特殊符號，文字可能不完整，請對照原卷 PDF'
            essays.append({'no': e['no'], 'text': e['text'], 'remark': remark})
    paper = {
        'examCode': m['exam_code'], 'paperCode': code, 'examSlug': slugs[0], 'examSlugs': slugs[1:],
        'examTitle': re.sub(r'^(第.梯次[:：])?\d+年', '', m['exam_title']), 'rocYear': int(m['exam_code'][:3]),
        'categories': m['categories'], 'subjectName': m['subject'],
        'sourceQuestionUrl': m['q_url'], 'sourceAnswerUrl': m.get('a_url') or '',
        'answerNote': note, 'officialQuestionCount': total,
        'excluded': excluded + [{'no': 1000 + e['no'], 'reason': e['reason']} for e in essay_excluded],
        'questions': questions, 'essays': essays,
    }
    rep = {'key': [m['exam_code'], m['s']], 'code': code, 'slug': slugs[0], 'total': total, 'ok': len(questions),
           'essays': len(essays), 'essay_excluded': len(essay_excluded), 'essay_reasons': essay_excluded,
           'ans_mismatch': ans_mismatch}
    return paper, rep


def main():
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument('out_dir')
    ap.add_argument('prefixes', nargs='*')
    ap.add_argument('--workers', type=int, default=4)
    a = ap.parse_args()
    out_dir, prefixes, workers = a.out_dir, a.prefixes, a.workers
    os.makedirs(out_dir, exist_ok=True)
    man = [m for m in json.load(open('manifest_full.json', encoding='utf-8')) if m.get('ok')]
    if prefixes:
        man = [m for m in man if any(m['exam_code'].startswith(p) for p in prefixes)]
    packs = collections.defaultdict(list)
    seen = {}
    report = []
    with ProcessPoolExecutor(workers) as ex:
        for paper, rep in ex.map(process, man, chunksize=4):
            report.append(rep)
            if paper is None:
                continue
            key = (paper['examCode'], paper['paperCode'])
            if key in seen:  # 同一份試卷出現在多個科目代碼下：合併類科與考試類別
                p0 = seen[key]
                p0['categories'] += [c for c in paper['categories'] if c not in p0['categories']]
                for s in [paper['examSlug'], *paper['examSlugs']]:
                    if s != p0['examSlug'] and s not in p0['examSlugs']:
                        p0['examSlugs'].append(s)
                continue
            seen[key] = paper
            packs[paper['examCode']].append(paper)
    for code, ps in packs.items():
        ps = [p for p in ps if p['questions'] or p['essays']]
        ps.sort(key=lambda p: p['paperCode'])
        with gzip.open(f'{out_dir}/papers_{code}.json.gz', 'wt', encoding='utf-8', compresslevel=9) as f:
            json.dump(ps, f, ensure_ascii=False, separators=(',', ':'))
    json.dump(report, open('build_full_report.json', 'w', encoding='utf-8'), ensure_ascii=False)
    tot = sum(r.get('total', 0) for r in report); ok = sum(r.get('ok', 0) for r in report)
    es = sum(r.get('essays', 0) for r in report); ee = sum(r.get('essay_excluded', 0) for r in report)
    errs = [r for r in report if 'error' in r]
    am = sum(len(r.get('ans_mismatch', [])) for r in report)
    print(f'答案兩種讀法不一致而排除：{am} 題')
    by = collections.Counter(r.get('slug') for r in report)
    print(f'試卷 {len(report)}（無法開啟 {len(errs)}）；選擇題 官方 {tot} 收錄 {ok}（{ok / max(tot, 1):.1%}）；申論題 收錄 {es} 排除 {ee}')
    print('類別：', dict(by.most_common()))


if __name__ == '__main__':
    main()
