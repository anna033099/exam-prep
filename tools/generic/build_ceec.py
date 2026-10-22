"""大學入學考試中心（學測、分科測驗／指考）歷年試題 → 網站題庫包。

來源：大考中心網站「歷年試題及答題卷」（https://www.ceec.edu.tw/），試題 PDF 與「選擇題答案」PDF。
答案以兩種方法讀取（pdftotext -layout 的「題號 答案」配對、PyMuPDF 文字座標），一致才收；
題目文字須逐字出現在原卷文字中；含圖表、上下標（數理科常見）的題目排除並註明原因。
多選題依試卷「第 n 題至第 m 題…其中至少有一個是正確的選項」的說明判斷。
用法：python build_ceec.py <manifest.json> <輸出資料夾>
"""
import collections, gzip, json, os, re, subprocess, sys, unicodedata
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from parse_text_mc import parse, parse_essays
from textnorm import canon
import pymupdf as fitz

EXAM = {'gsat': ('gsat', '學科能力測驗'), 'ast': ('ast', '分科測驗／指定科目考試')}


def norm(s):
    s = canon(s or '')
    return re.sub(r'[\s-_]', '', s)


def poppler(pdf):
    return '\n'.join(norm(subprocess.run(['pdftotext', *m, pdf, '-'], capture_output=True, text=True).stdout) for m in ([], ['-layout']))


def answers_layout(pdf):
    t = unicodedata.normalize('NFKC', subprocess.run(['pdftotext', '-layout', pdf, '-'], capture_output=True, text=True).stdout)
    out = {}
    for line in t.splitlines():
        for m in re.finditer(r'(?<!\d)(\d{1,2})\s+([A-E]{1,5}|／|/|-)(?=\s|$)', line):
            out.setdefault(int(m.group(1)), m.group(2))
    return out


def answers_words(pdf):
    out = {}
    for page in fitz.open(pdf):
        rows = collections.defaultdict(list)
        for w in page.get_text('words'):
            rows[round((w[1] + w[3]) / 2 / 4)].append((w[0], unicodedata.normalize('NFKC', w[4])))
        for _, ws in sorted(rows.items()):
            ws.sort()
            toks = [t for _, t in ws]
            for i in range(len(toks) - 1):
                if re.fullmatch(r'\d{1,2}', toks[i]) and re.fullmatch(r'[A-E]{1,5}|／|/|-', toks[i + 1]):
                    out.setdefault(int(toks[i]), toks[i + 1])
    return out


def multi_ranges(pdf):
    text = re.sub(r'\s+', '', canon(''.join(p.get_text() for p in fitz.open(pdf))))
    nums = set()
    for m in re.finditer(r'第(\d+)題至第(\d+)題[^。]{0,40}?(其中至少有一個是正確|多選)', text):
        nums.update(range(int(m.group(1)), int(m.group(2)) + 1))
    return nums


def main():
    man = json.load(open(sys.argv[1], encoding='utf-8'))
    base = os.path.dirname(os.path.abspath(sys.argv[1]))
    out_dir = sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    packs = collections.defaultdict(list)
    report = []
    for m in man:
        if not m.get('q_pdf'):
            continue
        slug, ename = EXAM[m['exam']]
        title = m['title']
        y = re.match(r'(\d{2,3})', title)
        if not y:
            continue
        year = int(y.group(1))
        subj = re.split(r'[－-]', title)[-1].strip()
        qpdf = os.path.join(base, m['q_pdf'])
        try:
            first = canon(fitz.open(qpdf)[0].get_text())
        except Exception:
            report.append({'title': title, 'skip': 'PDF 無法開啟'})
            continue
        if '大學入學考試中心' not in re.sub(r'\s+', '', first):
            report.append({'title': title, 'skip': '抬頭不是大考中心原卷'})
            continue
        full = poppler(qpdf)
        questions, excluded, total, essays = [], [], 0, []
        if m.get('a_pdf'):
            apdf = os.path.join(base, m['a_pdf'])
            k1, k2 = answers_layout(apdf), answers_words(apdf)
            key = {n: v for n, v in k1.items() if re.fullmatch(r'[A-E]{1,5}', v)}
            r = parse(qpdf, answers=key, markers='alpha', multi=multi_ranges(qpdf))
            total = r['total']
            excluded = list(r['excluded'])
            for q in r['questions']:
                parts = [q['stem'], *q['options'].values(), q['passage'] or '']
                if not all(norm(x) in full for x in parts if x):
                    excluded.append({'no': q['no'], 'reason': '含特殊排版，文字與原卷比對不一致'})
                elif k2.get(q['no']) != q['answer']:
                    excluded.append({'no': q['no'], 'reason': '標準答案以兩種方法讀取結果不一致，為避免錯誤不收錄'})
                else:
                    questions.append(q)
        else:
            for e in parse_essays(qpdf):
                if e['scripts'] or norm(e['text']) not in full or len(norm(e['text'])) < 6:
                    continue
                essays.append({'no': e['no'], 'text': e['text'], 'remark': '本題含圖表，請對照原卷 PDF' if e['figure'] else None})
        excluded.sort(key=lambda e: e['no'])
        code = f"CEEC-{m['exam'].upper()}-{year}"
        makeup = '補考' if re.search(r'補考|補救', title) else ''
        pcode = re.sub(r'[^\w]', '', subj)[:24] + ('-補考' if makeup else '')
        n = 2
        while any(x['paperCode'] == pcode for x in packs[code]):   # 同年度同科目有多份（補考等）
            pcode = f"{pcode.rsplit('#', 1)[0]}#{n}"
            n += 1
        if questions or essays:
            packs[code].append({
                'examCode': code, 'paperCode': pcode, 'examSlug': slug, 'examSlugs': [],
                'examTitle': f'{year}學年度{ename}' + (f'（{makeup}）' if makeup else ''), 'rocYear': year, 'categories': [], 'subjectName': subj,
                'sourceQuestionUrl': m['q_url'], 'sourceAnswerUrl': m.get('a_url') or '',
                'sourceNote': '大學入學考試中心網站公布之歷年試題與選擇題答案。', 'answerNote': '',
                'officialQuestionCount': total, 'excluded': excluded, 'questions': questions, 'essays': essays,
            })
        report.append({'title': title, 'total': total, 'ok': len(questions), 'essays': len(essays)})
    for code, ps in packs.items():
        with gzip.open(f'{out_dir}/papers_{code}.json.gz', 'wt', encoding='utf-8') as f:
            json.dump(ps, f, ensure_ascii=False, separators=(',', ':'))
    json.dump(report, open(os.path.join(base, 'build_report.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f"試卷 {sum(len(p) for p in packs.values())}；選擇題 {sum(r.get('ok', 0) for r in report)}/{sum(r.get('total', 0) for r in report)}；略過 {sum(1 for r in report if 'skip' in r)}")


if __name__ == '__main__':
    main()
