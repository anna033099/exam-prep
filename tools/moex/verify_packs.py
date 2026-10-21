"""獨立驗證：(1) 每題題幹與選項文字是否逐字出現在原始 PDF；(2) 答案是否與以另一種方法讀出的答案表一致。"""
import json, re, glob, subprocess, collections
import pymupdf as fitz

man = {(m['exam_code'], m['q_url'].replace('https://wwwq.moex.gov.tw/exam/', '')): m for m in json.load(open('manifest.json', encoding='utf-8'))}
def norm(s):
    s = s.replace('[u]', '').replace('[/u]', '').replace('＿＿＿', '')
    return re.sub(r'[\s-]', '', s)

def answers_layout(pdf):
    t = subprocess.run(['pdftotext', '-layout', pdf, '-'], capture_output=True, text=True).stdout.splitlines()
    out = {}
    for i, line in enumerate(t):
        nums = re.findall(r'第(\d+)題', line)
        if nums and i + 1 < len(t) and t[i + 1].startswith('答案'):
            vals = t[i + 1][2:].split()
            for n, v in zip(nums, vals):
                out[int(n)] = v
    return out

stats = collections.Counter(); bad_text = []; bad_ans = []
for f in sorted(glob.glob('packs/papers_*.json')):
    for p in json.load(open(f, encoding='utf-8')):
        m = man[(p['examCode'], p['sourceQuestionUrl'].replace('https://wwwq.moex.gov.tw/exam/', ''))]
        full = norm(''.join(b[4] for pg in fitz.open(m['q_pdf']) for b in pg.get_text('blocks') if not re.search('代號：|頁次：', b[4])).replace('\uf02d', '－').replace('\uf02b', '＋').replace('\uf03d', '＝'))
        full = re.sub(r'代號：[0-9\-]*?頁次：\d+－\d+', '', full)
        ans = answers_layout(m['a_pdf'])
        for q in p['questions']:
            stats['q'] += 1
            parts = [q['stem']] + list(q['options'].values()) + ([q['passage']] if q['passage'] else [])
            missing = [x for x in parts if x and norm(x) not in full]
            if missing:
                stats['text_mismatch'] += 1
                bad_text.append((p['paperCode'], q['no'], missing[0][:60]))
            if ans.get(q['no']) != q['answer']:
                stats['ans_mismatch'] += 1
                bad_ans.append((p['paperCode'], q['no'], q['answer'], ans.get(q['no'])))
print(stats)
print('text samples', bad_text[:15])
print('answer samples', bad_ans[:15])
