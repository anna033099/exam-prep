"""依索引下載考選部試題與標準答案 PDF（只抓有標準答案的測驗式科目）。"""
import json, os, re, time, sys
import requests

BASE = 'https://wwwq.moex.gov.tw/exam/'
KEEP = ['一般行政', '一般民政', '人事行政', '資訊處理']
EXAMS = sorted(json.load(open('codes_101_115.json', encoding='utf-8')), reverse=True)
KIND = {c: ('chudeng' if '初等' in t else 'gaopu' if '高等考試三級' in t else 'local') for c, (y, t) in json.load(open('codes_101_115.json', encoding='utf-8')).items()}
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
        if KIND[code] != 'chudeng' and not any(k in cat for k in KEEP):
            continue
        sq = re.search(r'&s=(\d+)', it['q'].replace('&amp;', '&')).group(1)
        c = re.search(r'&c=(\d+)', it['q'].replace('&amp;', '&')).group(1)
        key = (code, sq)
        entry = {'kind': KIND[code], 'exam_code': code, 'category': cat, 'subject': it['subject'], 'c': c, 's': sq}
        if key in seen:
            seen[key]['categories'].append(cat)
            continue
        entry['categories'] = [cat]
        qf, af = f'pdf/Q_{code}_{c}_{sq}.pdf', f'pdf/S_{code}_{c}_{sq}.pdf'
        for url, fn in [(it['q'], qf), (it['a'], af)]:
            if not os.path.exists(fn) or os.path.getsize(fn) < 1000:
                for attempt in range(5):
                    try:
                        r = s.get(BASE + url.replace('&amp;', '&'), timeout=90)
                        if r.ok and r.content[:4] == b'%PDF':
                            open(fn, 'wb').write(r.content)
                            break
                    except requests.RequestException:
                        pass
                    time.sleep(3 * (attempt + 1))
                time.sleep(0.3)
        entry['q_pdf'], entry['a_pdf'] = qf, af
        entry['q_url'], entry['a_url'] = BASE + it['q'].replace('&amp;', '&'), BASE + it['a'].replace('&amp;', '&')
        seen[key] = entry
        manifest.append(entry)
    print(code, len(seen), flush=True)
json.dump(manifest, open('manifest.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('total papers', len(manifest))
