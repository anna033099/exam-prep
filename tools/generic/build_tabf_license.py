"""台灣金融研訓院金融證照測驗（授信、外匯、理財、信託、內控內稽…）歷屆試題 → 網站題庫包。

來源：研訓院網站「歷屆試題」（https://www.tabf.org.tw/LicenseHistoryExam.aspx，robots.txt 允許），
每屆一份試題 PDF 與一份答案 PDF。答案表為「題號｜第一節｜第二節」欄位，選項以 (1)–(4) 表示。

答案以兩種方法讀取：pdftotext -layout 的欄位、PyMuPDF 的文字座標，兩者一致才收；
題目文字須逐字出現在原卷文字中。
用法：python build_tabf_license.py <manifest.json> <輸出資料夾>
"""
import collections, gzip, json, os, re, subprocess, sys, unicodedata
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from parse_text_mc import parse
from textnorm import canon
import pymupdf as fitz

NUM = {'1': 'A', '2': 'B', '3': 'C', '4': 'D', '5': 'E'}


def norm(s):
    s = canon(s or '')
    return re.sub(r'[\s\ue000-\uf8ff_]', '', s)


def poppler(pdf):
    return '\n'.join(norm(subprocess.run(['pdftotext', *m, pdf, '-'], capture_output=True, text=True).stdout) for m in ([], ['-layout']))


def header_cols(text):
    """答案表標題列的科目順序，例如 ['授信法規', '授信實務']。"""
    cols = [re.sub(r'\s+', '', x) for x in re.findall(r'第[一二三四五]節\s*[-－]\s*(\S+)', text)]
    if cols:
        return cols
    lines = [l for l in text.splitlines()]
    for i, l in enumerate(lines):
        if '節次' in l and '科目' in l:
            for nxt in lines[i + 1:i + 4]:
                if nxt.strip() and '題號' not in nxt:
                    return nxt.split()
    return ['答案']


def answers_layout(pdf):
    t = unicodedata.normalize('NFKC', subprocess.run(['pdftotext', '-layout', pdf, '-'], capture_output=True, text=True).stdout)
    cols = header_cols(t)
    out = collections.defaultdict(dict)
    for line in t.splitlines():
        m = re.match(r'^\s*(\d{1,3})\s+(.+)$', line)
        if not m:
            continue
        vals = m.group(2).split()
        for i, v in enumerate(vals[:len(cols)]):
            out[cols[i]][int(m.group(1))] = v
    return cols, out


def answers_words(pdf, cols):
    """第二種讀法：用文字座標，同一列（y）上依 x 排序（欄名沿用標題列）。"""
    doc = fitz.open(pdf)
    out = collections.defaultdict(dict)
    for page in doc:
        rows = collections.defaultdict(list)
        for w in page.get_text('words'):
            rows[round((w[1] + w[3]) / 2 / 4)].append((w[0], unicodedata.normalize('NFKC', w[4])))
        for _, ws in sorted(rows.items()):
            ws.sort()
            toks = [t for _, t in ws]
            if len(toks) >= 2 and re.fullmatch(r'\d{1,3}', toks[0]):
                for i, v in enumerate(toks[1:len(cols) + 1]):
                    out[cols[i]][int(toks[0])] = v
    return cols, out


def to_letter(v):
    return NUM.get(v) if v in NUM else None


def main():
    man = json.load(open(sys.argv[1], encoding='utf-8'))
    base = os.path.dirname(os.path.abspath(sys.argv[1]))
    out_dir = sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    papers, report = [], []
    for x in man:
        ans_f = [f for f in x['files'] if '答案' in f['label']]
        if not ans_f:
            continue
        apdf = os.path.join(base, ans_f[0]['file'])
        cols1, a1 = answers_layout(apdf)
        cols2, a2 = answers_words(apdf, cols1)
        atext = unicodedata.normalize('NFKC', ''.join(p.get_text() for p in fitz.open(apdf)))
        title = re.search(r'【([^】]*(?:屆|期|次)[^】]*測驗[^】]*)】', atext)
        exam_name = title.group(1) if title else f"{x['exam']}{x['session']}"
        for f in x['files']:
            if '答案' in f['label']:
                continue
            qpdf = os.path.join(base, f['file'])
            first = canon(fitz.open(qpdf)[0].get_text())
            if '台灣金融研訓院' not in re.sub(r'\s+', '', first):
                report.append({'exam': exam_name, 'file': f['file'], 'skip': '抬頭不是研訓院原卷'})
                continue
            subj = re.search(r'科\s*目\s*[:：]\s*(.+)', first)
            subj = re.sub(r'\s+', '', subj.group(1)) if subj else f['label']
            subj = re.split(r'[＊*]?入場通知書', subj)[0]
            col = next((c for c in cols1 if c and (c in subj or subj in c)), None)
            if col is None and len(cols1) == 1:
                col = cols1[0]
            if col is None:
                # 科目名稱與欄名不完全相同（如「家族信託規劃第一節」）：依節次順序對應
                order = [g['label'] for g in x['files'] if '答案' not in g['label']]
                if len(order) == len(cols1) and f['label'] in order:
                    col = cols1[order.index(f['label'])]
            if col is None:
                report.append({'exam': exam_name, 'subject': subj, 'skip': f'答案表找不到科目（{cols1}）'})
                continue
            key1 = {n: to_letter(v) for n, v in a1.get(col, {}).items()}
            key2 = {n: to_letter(v) for n, v in a2.get(col, {}).items()}
            r = parse(qpdf, answers={n: v for n, v in key1.items() if v}, markers='num')
            full = poppler(qpdf)
            qs, excl = [], list(r['excluded'])
            for q in r['questions']:
                parts = [q['stem'], *q['options'].values(), q['passage'] or '']
                if not all(norm(p) in full for p in parts if p):
                    excl.append({'no': q['no'], 'reason': '含特殊排版，文字與原卷比對不一致'})
                elif key2.get(q['no']) != q['answer']:
                    excl.append({'no': q['no'], 'reason': '標準答案以兩種方法讀取結果不一致，為避免錯誤不收錄'})
                else:
                    qs.append(q)
            excl.sort(key=lambda e: e['no'])
            year = re.search(r'(\d{3})年', exam_name)
            m = re.search(r'第\s*(\d+)\s*[屆期]', exam_name)
            roc = int(year.group(1)) if year else None
            if roc is None:
                # 研訓院以「屆／期」編號；年度取自答案 PDF 的建立日期
                md = fitz.open(apdf).metadata.get('creationDate', '')
                roc = int(md[2:6]) - 1911 if re.match(r'D:\d{4}', md) else 0
            code = 'TABF-' + re.sub(r'[^\w]', '', exam_name)[:24]
            papers.append({
                'examCode': code, 'paperCode': re.sub(r'[^\w]', '', subj)[:28], 'examSlug': 'finance', 'examSlugs': [],
                'examTitle': exam_name, 'rocYear': roc, 'categories': [x['exam']], 'subjectName': subj[:200],
                'sourceQuestionUrl': f['url'], 'sourceAnswerUrl': ans_f[0]['url'],
                'sourceNote': f"台灣金融研訓院網站公布之歷屆試題（{x['page']}）。",
                'answerNote': '', 'officialQuestionCount': r['total'],
                'excluded': excl, 'questions': qs, 'essays': [],
            })
            report.append({'exam': exam_name, 'subject': subj, 'total': r['total'], 'ok': len(qs), 'year': roc})
    with gzip.open(f'{out_dir}/papers_TABF-license.json.gz', 'wt', encoding='utf-8') as fp:
        json.dump([p for p in papers if p['questions']], fp, ensure_ascii=False, separators=(',', ':'))
    json.dump(report, open(os.path.join(base, 'build_report.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f"試卷 {sum(1 for p in papers if p['questions'])}；選擇題 {sum(r.get('ok', 0) for r in report)}/{sum(r.get('total', 0) for r in report)}；略過 {sum(1 for r in report if 'skip' in r)}")


if __name__ == '__main__':
    main()
