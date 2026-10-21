"""獨立驗證郵局試題：以 pdftotext 重新讀取【n】題號，比對答案；並確認題目文字逐字出現在原卷。"""
import json, re, subprocess, collections, sys
import pymupdf as fitz
def norm(s):
    s = (s or '').replace('[u]', '').replace('[/u]', '').replace('＿＿＿', '')
    return re.sub(r'[\s-\x80-\x9f_]', '', s)

def poppler_text(pdf):
    import subprocess
    t = ''
    for mode in ([], ['-layout']):
        out = subprocess.run(['pdftotext', *mode, pdf, '-'], capture_output=True, text=True).stdout
        t += norm(out.replace('\uf0ae', '→')) + '\n'
    return t + '\n' + norm(''.join(b[4] for pg in fitz.open(pdf) for b in pg.get_text('blocks')).replace('\uf0ae', '→'))

st = collections.Counter(); bad = []
for p in json.load(open('packs/papers_post.json', encoding='utf-8')):
    f = 'pdf/' + p['sourceQuestionUrl'].rsplit('/', 1)[1]
    txt = subprocess.run(['pdftotext', f, '-'], capture_output=True, text=True).stdout
    key = {int(n): 'ABCD'[int(a) - 1] for a, n in re.findall(r'【(?:改\s*)?([1-4])】\s*(\d+)\s*[\.．]', txt)}
    full = poppler_text(f)
    for q in p['questions']:
        st['q'] += 1
        if key.get(q['no']) != q['answer']:
            st['ans_mismatch'] += 1; bad.append((p['paperCode'], q['no'], q['answer'], key.get(q['no'])))
        if any(x and norm(x) not in full for x in [q['stem'], *q['options'].values(), q['passage']]):
            st['text_mismatch'] += 1
print(st, bad[:10])
sys.exit(1 if st['ans_mismatch'] or st['text_mismatch'] else 0)
