"""依索引下載考選部試題與標準答案 PDF（只抓有標準答案的測驗式科目）。"""
import json, os, re, time, sys
import requests

BASE = 'https://wwwq.moex.gov.tw/exam/'
KEEP = ['一般行政', '一般民政', '人事行政', '資訊處理']
EXAMS = ['115010', '114010', '113010', '115080', '114080', '113080', '114190', '113200']
os.makedirs('pdf', exist_ok=True)
s = requests.Session(); s.headers['User-Agent'] = 'Mozilla/5.0 (portfolio study tool)'
manifest = []
for code in EXAMS:
    items = json.load(open(f'i{code}.json', encoding='utf-8'))
    seen = {}
    for it in items:
        if not it['a']:
            continue
        cat = it['category'] or ''
        if not code.endswith('010') and not any(k in cat for k in KEEP):
            continue
        sq = re.search(r'&s=(\d+)', it['q'].replace('&amp;', '&')).group(1)
        c = re.search(r'&c=(\d+)', it['q'].replace('&amp;', '&')).group(1)
        key = (code, sq)
        entry = {'exam_code': code, 'category': cat, 'subject': it['subject'], 'c': c, 's': sq}
        if key in seen:
            seen[key]['categories'].append(cat)
            continue
        entry['categories'] = [cat]
        qf, af = f'pdf/Q_{code}_{c}_{sq}.pdf', f'pdf/S_{code}_{c}_{sq}.pdf'
        for url, fn in [(it['q'], qf), (it['a'], af)]:
            if not os.path.exists(fn) or os.path.getsize(fn) < 1000:
                r = s.get(BASE + url.replace('&amp;', '&'), timeout=90)
                open(fn, 'wb').write(r.content)
                time.sleep(0.4)
        entry['q_pdf'], entry['a_pdf'] = qf, af
        entry['q_url'], entry['a_url'] = BASE + it['q'].replace('&amp;', '&'), BASE + it['a'].replace('&amp;', '&')
        seen[key] = entry
        manifest.append(entry)
    print(code, len(seen), flush=True)
json.dump(manifest, open('manifest.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('total papers', len(manifest))
