"""國營事業甄試試題（經濟部聯招、台糖、臺灣菸酒、中華郵政、農田水利）→ 網站題庫包。

來源：主辦單位公告的原卷 PDF，由 5138 考神網（首宇）整理轉載於 Google 雲端硬碟
（https://www.5138.com.tw/exam.htm，robots.txt 允許存取）。和中華電信的作法相同，
只收第一頁抬頭是主辦單位原卷格式的檔案，抬頭不符的一律不收。

選擇題：用「解答」版（題號前標有答案，如「[A] 1.」）解析；每題文字須逐字出現在原卷文字中，
答案再用 pdftotext 另讀一次，兩種讀法一致才收。申論題：用原卷「一、二、三…」各題，文字同樣比對。

用法：python build_5138.py <files.json> <輸出資料夾>
"""
import collections, gzip, json, os, re, subprocess, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from parse_text_mc import parse, parse_essays
from textnorm import canon
import pymupdf as fitz

# 類別設定：網站考試代號、原卷抬頭必須包含的字、代碼前綴
CATS = {
    'econ': ('econ', r'經濟部所屬事業機構.*甄試', 'ECON'),
    'sugar': ('sugar', r'台灣糖業|臺灣糖業', 'SUGAR'),
    'ttl': ('ttl', r'臺灣菸酒|台灣菸酒', 'TTL'),
    'post5138': ('post', r'中華郵政', 'POST5138'),
    'irrig': ('irrig', r'農田水利|灌溉管理', 'IRRIG'),
}


def norm(s):
    s = canon(s or '')
    s = s.replace('[u]', '').replace('[/u]', '').replace('＿＿＿', '')
    return re.sub(r'[\s-_]', '', s)


def poppler(pdf):
    t = ''
    for mode in ([], ['-layout']):
        t += norm(subprocess.run(['pdftotext', *mode, pdf, '-'], capture_output=True, text=True).stdout) + '\n'
    return t


def answers_layout(pdf):
    """第二種讀法：pdftotext -layout 讀「[A] 12.」這種題號前的答案。"""
    import unicodedata
    t = unicodedata.normalize('NFKC', subprocess.run(['pdftotext', '-layout', pdf, '-'], capture_output=True, text=True).stdout)
    out = {}
    for m in re.finditer(r'[\[【]\s*([A-E])\s*[\]】]\s*(\d{1,3})\s*[.．]', t):
        out.setdefault(int(m.group(2)), m.group(1))
    return out


def head_ok(pdf, pat):
    try:
        t = canon(fitz.open(pdf)[0].get_text())
    except Exception:
        return False, ''
    return bool(re.search(pat, re.sub(r'\s+', '', t))), t


def subject_key(name):
    """「科目A_企管_企業概論、法學緒論」與「解答A_企管_企業概論、法學緒論」對成同一科。"""
    n = re.sub(r'^(科目|解答|答案|試題)', '', name)
    return re.sub(r'\s+', '', n).strip('_：: ')


def main():
    files = json.load(open(sys.argv[1], encoding='utf-8'))
    out_dir = sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    base = os.path.dirname(os.path.abspath(sys.argv[1]))
    groups = collections.defaultdict(list)
    for f in files:
        if f['ok'] and f['cat'] in CATS:
            groups[(f['cat'], f['exam'])].append(f)
    packs = collections.defaultdict(list)
    report = []
    for (cat, exam), fs in groups.items():
        slug, pat, prefix = CATS[cat]
        year = int(re.match(r'(\d{2,3})', exam).group(1))
        by = collections.defaultdict(dict)
        for f in fs:
            role = 'answer' if re.match(r'^(解答|答案)', f['name']) else 'question'
            by[subject_key(f['name'])][role] = f
        for key, roles in by.items():
            qf, af = roles.get('question'), roles.get('answer')
            src = af or qf
            pdf = os.path.join(base, src['file'])
            ok, first = head_ok(pdf, pat)
            if not ok:
                report.append({'exam': exam, 'subject': key, 'skip': '抬頭不是主辦單位原卷格式'})
                continue
            subj = re.search(r'科\s*目\s*[:：]\s*(.+)', first)
            subj = re.sub(r'\s+', '', subj.group(1)) if subj else key
            if re.match(r'^1[.．]', subj):   # 「1.應用力學 2.材料力學」→「應用力學、材料力學」
                subj = '、'.join(x for x in re.split(r'\d[.．]', subj) if x)
            cats = re.search(r'類\s*別\s*[:：]\s*(\S+)', first)
            questions, excluded, total, essays = [], [], 0, []
            if af:
                full = poppler(pdf)
                r = parse(pdf, markers='alpha')
                ans2 = answers_layout(pdf)
                total = r['total']
                excluded = list(r['excluded'])
                for q in r['questions']:
                    parts = [q['stem'], *q['options'].values(), q['passage'] or '']
                    if not all(norm(x) in full for x in parts if x):
                        excluded.append({'no': q['no'], 'reason': '含特殊排版，文字與原卷比對不一致'})
                    elif ans2.get(q['no']) != q['answer']:
                        excluded.append({'no': q['no'], 'reason': '標準答案以兩種方法讀取結果不一致，為避免錯誤不收錄'})
                    else:
                        questions.append(q)
            if qf:
                qpdf = os.path.join(base, qf['file'])
                fullq = poppler(qpdf)
                for e in parse_essays(qpdf):
                    if not e['text'] or len(norm(e['text'])) < 6:
                        continue
                    if e['scripts']:
                        excluded.append({'no': 1000 + e['no'], 'reason': '含上下標或數學式，純文字無法正確呈現'})
                    elif norm(e['text']) not in fullq:
                        excluded.append({'no': 1000 + e['no'], 'reason': '含圖表、公式或特殊排版，文字與原卷比對不一致'})
                    else:
                        essays.append({'no': e['no'], 'text': e['text'],
                                       'remark': '本題含圖表，請對照原卷 PDF' if e['figure'] else None})
            if not questions and not essays:
                report.append({'exam': exam, 'subject': key, 'skip': '沒有可收錄的題目', 'total': total})
                continue
            excluded.sort(key=lambda e: e['no'])
            code = f'{prefix}-{year}'
            pcode = re.sub(r'[^\w\-]', '', key)[:28] or str(abs(hash(key)) % 10 ** 8)
            packs[code].append({
                'examCode': code, 'paperCode': pcode, 'examSlug': slug, 'examSlugs': [],
                'examTitle': re.sub(r'^\d+年(度)?', '', exam).replace('試題', ''), 'rocYear': year,
                'categories': [cats.group(1)] if cats else [], 'subjectName': subj[:200],
                'sourceQuestionUrl': (qf or af)['drive'], 'sourceAnswerUrl': af['drive'] if af else '',
                'sourceNote': f'主辦單位公告之原卷 PDF，取自 5138 考神網整理轉載（{src["exam_url"]}）。',
                'answerNote': '', 'officialQuestionCount': total,
                'excluded': excluded, 'questions': questions, 'essays': essays,
            })
            report.append({'exam': exam, 'subject': key, 'total': total, 'ok': len(questions), 'essays': len(essays)})
    for code, ps in packs.items():
        with gzip.open(f'{out_dir}/papers_{code}.json.gz', 'wt', encoding='utf-8') as f:
            json.dump(ps, f, ensure_ascii=False, separators=(',', ':'))
    json.dump(report, open(os.path.join(base, 'build_report.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    tot = sum(r.get('total', 0) for r in report); ok = sum(r.get('ok', 0) for r in report)
    print(f'試卷 {sum(len(p) for p in packs.values())}；選擇題 {ok}/{tot}；申論 {sum(r.get("essays", 0) for r in report)}；略過 {sum(1 for r in report if "skip" in r)}')


if __name__ == '__main__':
    main()
