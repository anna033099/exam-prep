"""依索引（i<代碼>.json）下載考選部全部試題 PDF；有測驗式標準答案的一併下載答案 PDF。

和 download_all.py 不同，這支不限類科，申論題試卷也下載。輸出 manifest_full.json：
每份試卷一筆，含適用類科清單、網站考試類別（taxonomy.classify）與原始連結。

用法：MOEX_CODES=all_codes.json python download_full.py [代碼前綴...] [--workers 3]
"""
import json, os, re, sys, time, threading
from concurrent.futures import ThreadPoolExecutor
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from taxonomy import classify

BASE = 'https://wwwq.moex.gov.tw/exam/'
import argparse
_ap = argparse.ArgumentParser()
_ap.add_argument('prefixes', nargs='*')
_ap.add_argument('--workers', type=int, default=3)
_a = _ap.parse_args()
args, workers = _a.prefixes, _a.workers
CODES = json.load(open(os.environ.get('MOEX_CODES', 'codes.json'), encoding='utf-8'))
os.makedirs('pdf', exist_ok=True)
local = threading.local()


def session():
    if not hasattr(local, 's'):
        local.s = requests.Session()
        local.s.headers['User-Agent'] = 'Mozilla/5.0 (portfolio study tool)'
    return local.s


def fetch(url, fn):
    if os.path.exists(fn) and os.path.getsize(fn) > 1000:
        return True
    for attempt in range(5):
        try:
            r = session().get(BASE + url, timeout=90)
            if r.ok and r.content[:4] == b'%PDF':
                tmp = fn + '.part'
                open(tmp, 'wb').write(r.content)
                os.replace(tmp, fn)
                time.sleep(0.2)
                return True
        except requests.RequestException:
            pass
        time.sleep(3 * (attempt + 1))
    return False


papers = {}
for code in sorted(CODES):
    if args and not any(code.startswith(a) for a in args):
        continue
    path = f'i{code}.json'
    if not os.path.exists(path):
        continue
    title = CODES[code][1]
    for it in json.load(open(path, encoding='utf-8')):
        if not it['q']:
            continue
        q = it['q'].replace('&amp;', '&')
        a = it['a'].replace('&amp;', '&') if it['a'] else None
        s = re.search(r'&s=(\w+)', q).group(1)
        c = re.search(r'&c=(\w+)', q).group(1)
        cat = it['category'] or ''
        key = (code, s)
        slug = classify(title, cat)
        if key in papers:
            p = papers[key]
            if cat not in p['categories']:
                p['categories'].append(cat)
            if slug and slug not in p['slugs']:
                p['slugs'].append(slug)
            if a and not p['a_url']:
                p['a_url'], p['a_pdf'] = BASE + a, f'pdf/S_{code}_{c}_{s}.pdf'
            continue
        papers[key] = {
            'exam_code': code, 'exam_title': title, 'subject': it['subject'], 'c': c, 's': s,
            'categories': [cat], 'slugs': [slug] if slug else [],
            'q_url': BASE + q, 'a_url': BASE + a if a else None,
            'q_pdf': f'pdf/Q_{code}_{c}_{s}.pdf', 'a_pdf': f'pdf/S_{code}_{c}_{s}.pdf' if a else None,
        }

jobs = []
for p in papers.values():
    jobs.append((p['q_url'][len(BASE):], p['q_pdf']))
    if p['a_url']:
        jobs.append((p['a_url'][len(BASE):], p['a_pdf']))
print(f'{len(papers)} 份試卷、{len(jobs)} 個 PDF', flush=True)
done = [0]
failed = []
lock = threading.Lock()


def work(job):
    ok = fetch(*job)
    with lock:
        done[0] += 1
        if not ok:
            failed.append(job)
        if done[0] % 500 == 0:
            print(f'{done[0]}/{len(jobs)} 失敗 {len(failed)}', flush=True)


with ThreadPoolExecutor(workers) as ex:
    list(ex.map(work, jobs))
for p in papers.values():
    p['ok'] = os.path.exists(p['q_pdf']) and (not p['a_pdf'] or os.path.exists(p['a_pdf']))
out = 'manifest_full.json'
old = {(m['exam_code'], m['s']): m for m in json.load(open(out, encoding='utf-8'))} if os.path.exists(out) else {}
old.update({k: v for k, v in papers.items()})
json.dump(list(old.values()), open(out, 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
print(f'完成：{len(papers)} 份；下載失敗 {len(failed)} 個', flush=True)
for f in failed[:20]:
    print('FAIL', f)
