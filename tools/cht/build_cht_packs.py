"""中華電信 101–103 年遴選試題（台灣金融研訓院辦理時公告之原檔，經 1111 人力銀行轉載）轉成網站試題包。

只收版式與研訓院原檔一致（抬頭為「中華電信股份有限公司…遴選試題」、題號前以【n】標示答案）的檔案；
每題再與原卷逐字比對、並以 pdftotext 獨立重讀答案，任何不一致即排除。
"""
import json, re, sys, os, hashlib, subprocess, collections
import os as _os, sys as _sys
_sys.path.insert(0, _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))
from textnorm import canon
sys.path.insert(0, os.path.dirname(__file__))
from parse_tabf import build
import pymupdf as fitz

OUT = sys.argv[1]
files = json.load(open(sys.argv[2] if len(sys.argv) > 2 else 'files.json', encoding='utf-8'))
MIRROR_PAGE = 'https://www.1111job.com.tw/news.asp?ano=3146'


def norm(s):
    s = canon(s or '')
    s = (s or '').replace('[u]', '').replace('[/u]', '').replace('＿＿＿', '')
    return re.sub(r'[\s\x80-\x9f_-]', '', s)


def full_text(pdf):
    t = ''
    for mode in ([], ['-layout']):
        t += norm(subprocess.run(['pdftotext', *mode, pdf, '-'], capture_output=True, text=True).stdout) + '\n'
    return t + norm(''.join(b[4] for pg in fitz.open(pdf) for b in pg.get_text('blocks')))


def answer_key(pdf):
    txt = subprocess.run(['pdftotext', pdf, '-'], capture_output=True, text=True).stdout
    return {int(n): 'ABCD'[int(a) - 1] for a, n in re.findall(r'【(?:改\s*)?([1-4])】\s*(\d+)\s*[\.．]?', txt)}


papers, by_hash, stats = [], {}, collections.Counter()
for fn, meta in sorted(files.items()):
    year = int(re.match(r'(\d+)', meta['ctx']).group(1))
    if year not in (101, 102, 103):
        continue
    head = fitz.open(fn)[0].get_text()
    if not re.search(r'中華電信股份有限公司.{0,24}遴選.{0,4}試題', head.replace('\n', '')):
        stats['skip_header'] += 1
        continue
    res = build(fn)
    if not res['questions']:
        stats['skip_empty'] += 1
        continue
    full, key = full_text(fn), answer_key(fn)
    keep = []
    for q in res['questions']:
        parts = [q['stem'], *q['options'].values(), q['passage'] or '']
        if not all(norm(x) in full for x in parts if x):
            res['excluded'].append({'no': q['no'], 'reason': '含上下標、數學式或特殊排版，純文字無法正確呈現'})
        elif key.get(q['no']) != q['answer']:
            res['excluded'].append({'no': q['no'], 'reason': '答案無法以第二種方式核對一致'})
        else:
            keep.append(q)
    om = re.search(r'(?:本試卷之?試題皆為單選選擇題|四選一單選選擇題|共)\s*(\d+)\s*題', head.replace('\n', ''))
    official = int(om.group(1)) if om else res['total']
    got = {q['no'] for q in res['questions']} | {e['no'] for e in res['excluded']}
    for n in range(1, official + 1):
        if n not in got:
            res['excluded'].append({'no': n, 'reason': '含圖表或特殊排版，解析時無法完整取得'})
    res['excluded'] = [e for e in res['excluded'] if e['no'] <= official]
    keep = [q for q in keep if q['no'] <= official]
    res['total'] = official
    res['excluded'].sort(key=lambda e: e['no'])
    flat = head.replace('\n', ' ')
    m = re.search(r'(普通科目|專業科目)\s*[（(]?\d?[)）]?\s*[：:]\s*(.+?)\s*(◎|＊|\*|請填寫|注意)', flat)
    subject = re.sub(r'\s+', '', m.group(2)) if m else meta['text']
    subject = '、'.join(x for x in re.split(r'\(\d\)|、', subject) if x)
    cm = re.search(r'遴選類別(?:【代碼】)?\s*[：:](.+?)(普通科目|專業科目)', flat)
    cats = []
    if cm:
        for part in re.split(r'\s{2,}|(?<=】)\s*', cm.group(1)):
            part = re.sub(r'【[^】]*】', '', part).strip(' 、')
            if part and part not in cats:
                cats.append(part)
    h = hashlib.sha1(json.dumps([(q['stem'], q['options']) for q in keep], ensure_ascii=False).encode()).hexdigest()
    if h in by_hash:
        p = by_hash[h]
        p['categories'] += [c for c in cats if c not in p['categories']]
        stats['dup'] += 1
        continue
    fid = os.path.basename(fn).replace('.pdf', '').replace('2016South_File', 'F')
    p = {
        'examCode': f'CHT-{year}', 'paperCode': fid, 'examSlug': 'cht',
        'examTitle': '中華電信從業人員（基層專員）遴選', 'rocYear': year,
        'categories': cats or [meta['text']], 'subjectName': subject,
        'sourceQuestionUrl': meta['url'], 'sourceAnswerUrl': meta['url'],
        'sourceNote': '原公告單位：財團法人台灣金融研訓院（當年度試務受託單位）；本站取自 1111 人力銀行轉載之原檔：' + MIRROR_PAGE,
        'answerNote': '答案標示於試題題號前【】內（原卷以 1～4 表示，本站以 A～D 顯示）。',
        'officialQuestionCount': res['total'], 'excluded': res['excluded'],
        'questions': [{k: v for k, v in q.items() if k != 'corrected'} for q in keep],
    }
    by_hash[h] = p
    papers.append(p)
    stats['papers'] += 1
    stats['official'] += res['total']
    stats['kept'] += len(keep)
os.makedirs(OUT, exist_ok=True)
json.dump(papers, open(os.path.join(OUT, 'papers_cht.json'), 'w', encoding='utf-8'), ensure_ascii=False)
print(dict(stats))
for p in papers:
    print(p['rocYear'], p['subjectName'][:24], len(p['questions']), '/', p['officialQuestionCount'], '|', '、'.join(p['categories'])[:60])
