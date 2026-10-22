"""把台灣金融研訓院公告的中華郵政考古題轉成網站試題包，並逐字比對原卷。"""
import json, re, sys, os, hashlib, collections
import os as _os, sys as _sys
_sys.path.insert(0, _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__))))
from textnorm import canon
sys.path.insert(0, os.path.dirname(__file__))
from parse_tabf import build
import pymupdf as fitz

out = sys.argv[1]
rows = json.load(open('post_manifest.json', encoding='utf-8'))


def norm(s):
    s = canon(s or '')
    s = (s or '').replace('[u]', '').replace('[/u]', '').replace('＿＿＿', '')
    return re.sub(r'[\s-\x80-\x9f_]', '', s)


def poppler_text(pdf):
    import subprocess
    t = ''
    for mode in ([], ['-layout']):
        out = subprocess.run(['pdftotext', *mode, pdf, '-'], capture_output=True, text=True).stdout
        t += norm(out.replace('\uf0ae', '→')) + '\n'
    return t + '\n' + norm(''.join(b[4] for pg in fitz.open(pdf) for b in pg.get_text('blocks')).replace('\uf0ae', '→'))



def tracks_summary(t):
    names = []
    for part in re.split(r'[；;]', t):
        part = re.sub(r'\([A-Z]\d+\)|【[^】]*】', '', part).strip()
        if not part:
            continue
        part = re.sub(r'-[^-]*(郵局|地區|郵件處理中心|中心)$', '', part)
        if part not in names:
            names.append(part)
    return names


papers, by_hash, report = [], {}, collections.Counter()
for r in rows:
    if r['mcq_hint'] == 0:
        continue
    res = build(r['pdf'])
    if not res['questions']:
        continue
    doc = fitz.open(r['pdf'])
    full = poppler_text(r['pdf'])
    keep = []
    for q in res['questions']:
        parts = [q['stem'], *q['options'].values(), q['passage'] or '']
        if all(norm(x) in full for x in parts if x):
            keep.append(q)
        else:
            res['excluded'].append({'no': q['no'], 'reason': '含上下標、數學式或特殊排版，純文字無法正確呈現'})
    res['excluded'].sort(key=lambda e: e['no'])
    subject = re.sub(r'^(共同科目|專業科目)\s*[（(]?\d?[)）]?[：:]\s*', '', r['subject_raw']).strip()
    # 試卷首頁的科目可能換行，取完整科目名稱
    first = doc[0].get_text()
    m = re.search(r'第[一二三四五]節／(?:共同科目|專業科目)\s*[（(]?\d?[)）]?[：:]\s*(.+?)\n', first)
    if m:
        subject = m.group(1).strip()
    year = int(re.match(r'(\d+)年', r['exam_raw']).group(1))
    exam_name = '中華郵政' + re.sub(r'^\d+年', '', r['exam_raw'])
    h = hashlib.sha1(json.dumps([(q['stem'], q['options']) for q in keep], ensure_ascii=False).encode()).hexdigest()
    cats = tracks_summary(r['tracks'])
    if not cats:
        hm = re.search(r'類科【代碼】：(.+?)第[一二三四五]節', first.replace('\n', ''))
        if hm:
            head = re.sub(r'【[^】]*】', '', hm.group(1))
            head = re.sub(r'／\s+', '／', head)
            cats = [x.strip() for x in re.split(r'[、，,\s]+', head) if x.strip()]
    if h in by_hash:  # 同一份卷在不同網站重複公告
        p = by_hash[h]
        p['categories'] += [c for c in cats if c not in p['categories']]
        report['dup'] += 1
        continue
    fid = r['url'].rsplit('/', 1)[1].replace('.pdf', '')
    corrected = [q['no'] for q in keep if q.get('corrected')]
    note = f"第 {'、'.join(map(str, corrected))} 題為官方更正答案。" if corrected else ''
    p = {
        'examCode': f'POST-{year}', 'paperCode': fid, 'examSlug': 'post', 'examTitle': exam_name, 'rocYear': year,
        'categories': cats, 'subjectName': subject,
        'sourceQuestionUrl': r['url'], 'sourceAnswerUrl': r['url'],
        'answerNote': ('答案標示於試題題號前【】內（官方以 1～4 表示，本站以 A～D 顯示）。' + note).strip(),
        'officialQuestionCount': res['total'], 'excluded': res['excluded'],
        'questions': [{k: v for k, v in q.items() if k != 'corrected'} for q in keep],
    }
    by_hash[h] = p
    papers.append(p)
    report['papers'] += 1; report['official'] += res['total']; report['ok'] += len(keep)
os.makedirs(out, exist_ok=True)
json.dump(papers, open(f'{out}/papers_post.json', 'w', encoding='utf-8'), ensure_ascii=False)
print(dict(report), f"{report['ok'] / report['official']:.1%}")
for p in papers:
    print(p['rocYear'], p['subjectName'][:28], len(p['questions']), '/', p['officialQuestionCount'], p['categories'][:3])
