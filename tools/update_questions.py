"""每月自動更新題庫（GitHub Actions 呼叫，也可手動執行）。

1. 考選部：查詢今年與去年的考試清單，找出初等、高普考、地方特考中尚未收錄的考試代碼，
   只下載、解析這些新考試；逐題比對原卷文字並以第二種方法重讀答案，任何一題不一致就整批不寫入。
2. 中華郵政：探測台灣金融研訓院的甄試網站，找出新的考古題 PDF，解析、驗證後併入 papers_post.json。

通過驗證的新題庫包寫進 src/ExamPrep.Web/SeedData/papers/；網站啟動時只會「新增」尚未匯入的試卷。
結果摘要寫到 update_summary.md（GitHub Actions 會顯示在執行頁面）。

用法：python tools/update_questions.py [--work 暫存資料夾] [--skip-post]
"""
import argparse, glob, hashlib, json, os, shutil, subprocess, sys, tempfile, time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(ROOT, 'tools')
SEED = os.path.join(ROOT, 'src', 'ExamPrep.Web', 'SeedData', 'papers')
sys.path.insert(0, os.path.join(TOOLS, 'moex'))

ap = argparse.ArgumentParser()
ap.add_argument('--work', default=None)
ap.add_argument('--skip-post', action='store_true')
ap.add_argument('--years', default=None, help='逗號分隔的西元年，預設為今年與去年')
args = ap.parse_args()
work = args.work or tempfile.mkdtemp(prefix='examprep-update-')
os.makedirs(work, exist_ok=True)
summary = ['# 題庫自動更新結果', '']
failed = False


def run(cmd, cwd, env=None):
    print('$', ' '.join(cmd), flush=True)
    r = subprocess.run(cmd, cwd=cwd, env={**os.environ, **(env or {})}, capture_output=True, text=True)
    print(r.stdout[-4000:], r.stderr[-2000:], flush=True)
    return r


# ---------------- 考選部 ----------------
def discover(year):
    """回傳 {代碼: [西元年, 考試名稱]}，只保留初等、高普考、地方特考。"""
    import requests
    from bs4 import BeautifulSoup
    from moex_search import URL, fields
    for attempt in range(4):
        try:
            s = requests.Session(); s.headers['User-Agent'] = 'ExamPrep/1.0 (+study site; monthly update)'
            soup = BeautifulSoup(s.get(URL, timeout=60).text, 'html.parser')
            d = fields(soup)
            d.update({'ctl00$holderContent$wUctlExamYearStart$ddlExamYear': str(year),
                      'ctl00$holderContent$wUctlExamYearEnd$ddlExamYear': str(year),
                      'ctl00$holderContent$btnYear': '依考試年度設定考試簡稱'})
            soup = BeautifulSoup(s.post(URL, data=d, timeout=60).text, 'html.parser')
            out = {}
            for o in soup.find('select', {'name': 'ctl00$holderContent$ddlExamCode'}).find_all('option'):
                t = o.text.strip()
                if not o.get('value') or '專技' in t or '升官' in t or '升資' in t:
                    continue
                if '初等考試' in t or '高等考試三級考試暨普通考試' in t or ('特種考試地方政府公務人員考試' in t and t.index('特種') < 8):
                    out[o['value']] = [year, t]
            return out
        except Exception as e:  # 網路或頁面暫時錯誤時重試
            print('查詢失敗，重試', e, flush=True)
            time.sleep(10 * (attempt + 1))
    raise RuntimeError(f'無法取得 {year} 年考試清單')


codes_path = os.path.join(TOOLS, 'moex', 'codes.json')
codes = json.load(open(codes_path, encoding='utf-8'))
have = {os.path.basename(f)[len('papers_'):-len('.json')] for f in glob.glob(os.path.join(SEED, 'papers_*.json'))}
this_year = time.localtime().tm_year
years = [int(y) for y in args.years.split(',')] if args.years else [this_year - 1, this_year]
found = {}
for y in years:
    found.update(discover(y))
new = {c: v for c, v in found.items() if c not in have}
summary.append(f'## 考選部（查詢 {"、".join(map(str, years))} 年）')
summary.append(f'- 找到 {len(found)} 個相關考試，其中 {len(new)} 個尚未收錄：{"、".join(v[1] for v in new.values()) or "無"}')

if new:
    mw = os.path.join(work, 'moex')
    os.makedirs(mw, exist_ok=True)
    nc = os.path.join(mw, 'new_codes.json')
    json.dump(new, open(nc, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    env = {'MOEX_CODES': nc}
    run([sys.executable, os.path.join(TOOLS, 'moex', 'index_all.py')], mw, env)
    run([sys.executable, os.path.join(TOOLS, 'moex', 'download_all.py')], mw, env)
    b = run([sys.executable, os.path.join(TOOLS, 'moex', 'build_packs.py'), 'packs'], mw, env)
    v = run([sys.executable, os.path.join(TOOLS, 'moex', 'verify_packs.py'), 'packs'], mw, env)
    produced = sorted(glob.glob(os.path.join(mw, 'packs', 'papers_*.json')))
    if b.returncode != 0 or v.returncode != 0:
        failed = True
        summary.append(f'- ❌ 驗證未通過，本次不寫入。驗證輸出：`{v.stdout.strip()[:500]}`')
    else:
        total = 0
        for f in produced:
            papers = json.load(open(f, encoding='utf-8'))
            n = sum(len(p['questions']) for p in papers)
            if n == 0:
                continue  # 該考試尚未公布測驗題答案，下個月再試
            shutil.copy(f, SEED)
            code = os.path.basename(f)[len('papers_'):-len('.json')]
            codes[code] = new[code]
            total += n
            summary.append(f'- ✅ {new[code][1]}：{len(papers)} 份試卷、{n} 題（逐題驗證通過）')
        json.dump(dict(sorted(codes.items())), open(codes_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
        if total == 0:
            summary.append('- 新考試尚無可收錄的測驗題（可能尚未公布標準答案），下次再檢查。')

# ---------------- 中華郵政 ----------------
if not args.skip_post:
    summary.append('')
    summary.append('## 中華郵政（台灣金融研訓院）')
    pw = os.path.join(work, 'tabf')
    os.makedirs(os.path.join(pw, 'pdf'), exist_ok=True)
    master = os.path.join(TOOLS, 'tabf', 'post_manifest.json')
    before = json.load(open(master, encoding='utf-8'))
    shutil.copy(master, os.path.join(pw, 'post_manifest.json'))
    u = run([sys.executable, os.path.join(TOOLS, 'tabf', 'update_post.py'), pw], pw)
    after = json.load(open(os.path.join(pw, 'post_manifest.json'), encoding='utf-8'))
    known = {m['url'] for m in before}
    added = [m for m in after if m['url'] not in known]
    if u.returncode != 0:
        failed = True
        summary.append('- ❌ 探測研訓院網站失敗')
    elif not added:
        summary.append('- 沒有新的考古題')
    else:
        sub = os.path.join(pw, 'subset')
        os.makedirs(sub, exist_ok=True)
        os.symlink(os.path.join(pw, 'pdf'), os.path.join(sub, 'pdf'))
        json.dump(added, open(os.path.join(sub, 'post_manifest.json'), 'w', encoding='utf-8'), ensure_ascii=False)
        b = run([sys.executable, os.path.join(TOOLS, 'tabf', 'build_post_packs.py'), 'packs'], sub)
        v = run([sys.executable, os.path.join(TOOLS, 'tabf', 'verify_post.py')], sub)
        ok = b.returncode == 0 and v.returncode == 0 and "'ans_mismatch'" not in v.stdout and "'text_mismatch'" not in v.stdout
        if not ok:
            failed = True
            summary.append(f'- ❌ 驗證未通過，本次不寫入。`{v.stdout.strip()[:500]}`')
        else:
            seed_post = os.path.join(SEED, 'papers_post.json')
            existing = json.load(open(seed_post, encoding='utf-8'))

            def h(p):
                return hashlib.sha1(json.dumps([(q['stem'], q['options']) for q in p['questions']], ensure_ascii=False).encode()).hexdigest()
            seen = {h(p) for p in existing}
            fresh = [p for p in json.load(open(os.path.join(sub, 'packs', 'papers_post.json'), encoding='utf-8')) if p['questions'] and h(p) not in seen]
            if fresh:
                json.dump(existing + fresh, open(seed_post, 'w', encoding='utf-8'), ensure_ascii=False)
                for p in fresh:
                    summary.append(f"- ✅ {p['rocYear']} 年 {p['subjectName']}：{len(p['questions'])} 題")
            else:
                summary.append(f'- 找到 {len(added)} 份新 PDF，但沒有新的選擇題試卷（可能是申論題或重複公告）')
            shutil.copy(os.path.join(pw, 'post_manifest.json'), master)

open(os.path.join(ROOT, 'update_summary.md'), 'w', encoding='utf-8').write('\n'.join(summary) + '\n')
print('\n'.join(summary))
sys.exit(1 if failed else 0)
