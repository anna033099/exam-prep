"""把下載的官方 PDF 批次解析成網站 SeedData/papers/*.json，並輸出品質報告。"""
import json, re, sys, os, collections
sys.path.insert(0, os.path.dirname(__file__))
from parse_exam import build
import pymupdf as fitz

TITLES = {'010': '公務人員初等考試', '080': '公務人員高等考試三級考試暨普通考試', '190': '特種考試地方政府公務人員考試、離島地區公務人員考試', '200': '特種考試地方政府公務人員考試'}
def norm(s):
    s = s.replace('[u]', '').replace('[/u]', '').replace('＿＿＿', '')
    return re.sub(r'[\s\ue000-\uf8ff]', '', s)


def poppler_text(pdf):
    import subprocess
    t = ''
    for mode in ([], ['-layout']):
        out = subprocess.run(['pdftotext', *mode, pdf, '-'], capture_output=True, text=True).stdout
        out = '\n'.join(l for l in out.splitlines() if not re.search(r'代號[:：]|頁次[:：]', l))
        t += norm(out.replace('\uf02d', '－').replace('\uf02b', '＋').replace('\uf03d', '＝')) + '\n'
    return t + '\n' + norm(''.join(b[4] for pg in fitz.open(pdf) for b in pg.get_text('blocks') if not re.search('代號：|頁次：', b[4])).replace('\uf02d', '－').replace('\uf02b', '＋').replace('\uf03d', '＝'))



CODES = json.load(open('codes_101_115.json', encoding='utf-8'))
SLUG = {'010': 'chudeng', '080': 'gaopu', '190': 'local', '200': 'local'}
out_dir = sys.argv[1]
os.makedirs(out_dir, exist_ok=True)
manifest = json.load(open('manifest.json', encoding='utf-8'))
packs = collections.defaultdict(list)
seen = {}
report = []
for m in manifest:
    first = fitz.open(m['q_pdf'])[0].get_text()
    code = re.search(r'代號：\s*([0-9A-Za-z]+)', first)
    code = code.group(1) if code else f"{m['c']}-{m['s']}"
    ans_text = ''.join(p.get_text() for p in fitz.open(m['a_pdf']))
    title = re.search(r'考試名稱：\s*(.+)', ans_text)
    title = title.group(1).strip() if title else ''
    key = (m['exam_code'], code)
    if key in seen:
        seen[key]['categories'] += [c for c in m['categories'] if c not in seen[key]['categories']]
        continue
    r = build(m['q_pdf'], m['a_pdf'])
    # 獨立比對：題幹、選項、題組文章須逐字出現在原卷文字中，否則排除
    full = poppler_text(m['q_pdf'])
    keep = []
    for q in r['questions']:
        parts = [q['stem'], *q['options'].values(), q['passage'] or '']
        if all(norm(x) in full for x in parts if x):
            keep.append(q)
        else:
            r['excluded'].append({'no': q['no'], 'reason': '含上下標、數學式或特殊排版，純文字無法正確呈現'})
    r['questions'] = keep
    r['excluded'].sort(key=lambda e: e['no'])
    roc = int(m['exam_code'][:3])
    exam_title = re.sub(r'^\d+年', '', CODES[m['exam_code']][1])
    paper = {
        'examCode': m['exam_code'], 'paperCode': code, 'examSlug': m['kind'],
        'examTitle': exam_title, 'rocYear': roc,
        'categories': list(m['categories']), 'subjectName': m['subject'],
        'sourceQuestionUrl': m['q_url'], 'sourceAnswerUrl': m['a_url'],
        'answerNote': r['answer_note'], 'officialQuestionCount': r['total'],
        'excluded': r['excluded'], 'questions': r['questions'],
    }
    seen[key] = paper
    packs[m['exam_code']].append(paper)
    report.append((m['exam_code'], code, m['subject'][:24], r['total'], len(r['questions']), len(r['excluded'])))
for code, ps in packs.items():
    ps = [p for p in ps if p['questions']]
    json.dump(ps, open(f'{out_dir}/papers_{code}.json', 'w', encoding='utf-8'), ensure_ascii=False)
tot = sum(x[3] for x in report); ok = sum(x[4] for x in report)
for x in report:
    if x[3] == 0 or x[4] / max(x[3], 1) < 0.85:
        print('LOW', x)
print(f'papers={len(report)} official={tot} imported={ok} excluded={tot-ok} ({ok/tot:.1%})')
