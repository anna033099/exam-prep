"""增量更新中華郵政考古題清單（post_manifest.json）。

做法：探測台灣金融研訓院近兩年度的甄試網站（{年}post01～05），讀取「考古題」年度頁與各年度
歷屆頁，把尚未收錄的 PDF 下載到 pdf/，並由 PDF 抬頭讀出考試名稱與科目。之後由 build_post_packs.py
解析、verify_post.py 驗證。

用法：python update_post.py [工作資料夾]（預設為本檔所在資料夾）
"""
import html, json, os, re, sys, time
import requests
import pymupdf as fitz
from bs4 import BeautifulSoup

BASE = 'https://svc.tabf.org.tw'
work = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
os.chdir(work)
os.makedirs('pdf', exist_ok=True)
manifest = json.load(open('post_manifest.json', encoding='utf-8')) if os.path.exists('post_manifest.json') else []
known = {m['url'] for m in manifest}

s = requests.Session()
s.headers['User-Agent'] = 'ExamPrep/1.0 (+study site; monthly update)'


def get(url, **kw):
    for attempt in range(4):
        try:
            r = s.get(url, timeout=60, **kw)
            if r.status_code == 404:
                return None
            r.raise_for_status()
            return r
        except requests.RequestException:
            time.sleep(3 * (attempt + 1))
    return None


roc = time.localtime().tm_year - 1911
sites = [f'{y}post{n:02d}' for y in (roc - 1, roc, roc + 1) for n in range(1, 6)]
added = 0
for site in sites:
    r = get(f'{BASE}/{site}//Paper/Year')
    if r is None or '研訓院' not in html.unescape(r.text):
        continue
    years = re.findall(r'EPID=(\d+)"[^>]*>\s*(\d+)\s*年', r.text)
    print(site, 'years', years, flush=True)
    for epid, year in years:
        h = get(f'{BASE}/{site}//Paper/History', params={'EPID': epid})
        if h is None:
            continue
        soup = BeautifulSoup(h.text, 'html.parser')
        for a in soup.find_all('a', href=re.compile(r'/_File/Download/.+/HistoryPaper/.+\.pdf$')):
            url = BASE + a['href']
            if url in known:
                continue
            tracks = ''
            sib = a.next_sibling
            while sib is not None and getattr(sib, 'name', None) != 'a':
                if isinstance(sib, str):
                    tracks += sib.strip()
                sib = sib.next_sibling
            fn = 'pdf/' + url.rsplit('/', 1)[1]
            if not os.path.exists(fn):
                d = get(url)
                if d is None or d.content[:4] != b'%PDF':
                    print('  下載失敗', url)
                    continue
                open(fn, 'wb').write(d.content)
                time.sleep(0.3)
            doc = fitz.open(fn)
            head = doc[0].get_text().replace('\n', ' ')
            full = ''.join(p.get_text() for p in doc)
            em = re.search(r'中華郵政股份有限公司\s*(\d+)\s*年\s*(.+?)試題', head)
            exam_raw = f'{em.group(1)}年{em.group(2).strip()}' if em else f'{year}年職階人員甄試'
            sm = re.search(r'第[一二三四五]節／\s*((?:共同|專業)科目\s*[（(]?\d?[)）]?\s*[：:]\s*.+?)\s{2,}', head)
            subject_raw = sm.group(1).strip() if sm else a.get_text(strip=True)
            manifest.append({
                'site': site, 'epid': epid, 'year': int(year), 'label': a.get_text(strip=True), 'url': url,
                'tracks': tracks, 'pdf': fn, 'subject_raw': subject_raw,
                'mcq_hint': len(re.findall(r'【(?:改\s*)?[1-4]】', full)), 'exam_raw': exam_raw,
            })
            known.add(url)
            added += 1
            print('  新增', exam_raw, subject_raw[:30], flush=True)

json.dump(manifest, open('post_manifest.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(f'post_manifest：新增 {added} 份，共 {len(manifest)} 份')
