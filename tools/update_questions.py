"""每月自動更新題庫（GitHub Actions 呼叫，也可手動執行）。

1. 考選部：查詢今年與去年的考試清單，找出初等、高普考、地方特考中尚未收錄的考試代碼，
   只下載、解析這些新考試；逐題比對原卷文字並以第二種方法重讀答案，任何一題不一致就整批不寫入。
2. 中華郵政：探測台灣金融研訓院的甄試網站，找出新的考古題 PDF，解析、驗證後併入 papers_post.json。

通過驗證的新題庫包寫進 src/ExamPrep.Web/SeedData/papers/；網站啟動時只會「新增」尚未匯入的試卷。
結果摘要寫到 update_summary.md（GitHub Actions 會顯示在執行頁面）。

用法：python tools/update_questions.py [--work 暫存資料夾] [--skip-post]
"""
import argparse, glob, gzip, hashlib, json, os, re, shutil, subprocess, sys, tempfile, time

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
    """回傳 {代碼: [西元年, 考試名稱]}（考選部該年度全部考試）。"""
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
                if o.get('value'):
                    out[o['value']] = [year, o.text.strip()]
            return out
        except Exception as e:  # 網路或頁面暫時錯誤時重試
            print('查詢失敗，重試', e, flush=True)
            time.sleep(10 * (attempt + 1))
    raise RuntimeError(f'無法取得 {year} 年考試清單')


codes_path = os.path.join(TOOLS, 'moex', 'codes.json')
codes = json.load(open(codes_path, encoding='utf-8'))
have = {re.sub(r'\.json(\.gz)?$', '', os.path.basename(f))[len('papers_'):] for f in glob.glob(os.path.join(SEED, 'papers_*.json*'))}
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
    run([sys.executable, os.path.join(TOOLS, 'moex', 'download_full.py'), '--workers', '3'], mw, env)
    b = run([sys.executable, os.path.join(TOOLS, 'moex', 'build_full.py'), 'packs', '--workers', '2'], mw, env)
    if b.returncode != 0:
        failed = True
        summary.append(f'- ❌ 解析失敗，本次不寫入：`{b.stderr.strip()[-500:]}`')
    else:
        # build_full 已逐題比對原卷文字、答案用兩種方法交叉核對，不一致的題目排除並註明原因
        summary.append(f'- 解析結果：{b.stdout.strip().splitlines()[-2] if b.stdout.strip() else ""}')
        for f in sorted(glob.glob(os.path.join(mw, 'packs', 'papers_*.json.gz'))):
            papers = json.load(gzip.open(f, 'rt', encoding='utf-8'))
            n = sum(len(p['questions']) for p in papers)
            e = sum(len(p.get('essays', [])) for p in papers)
            code = os.path.basename(f)[len('papers_'):-len('.json.gz')]
            if n + e == 0:
                continue  # 尚未公布試題，下個月再試
            shutil.copy(f, SEED)
            codes[code] = new[code]
            summary.append(f'- ✅ {new[code][1]}：{len(papers)} 份試卷、選擇題 {n} 題、申論題 {e} 題')
        json.dump(dict(sorted(codes.items())), open(codes_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

# ---------------- 台灣金融研訓院金融證照 ----------------
summary.append('')
summary.append('## 金融證照（台灣金融研訓院歷屆試題）')
tw = os.path.join(work, 'tabf_license')
os.makedirs(tw, exist_ok=True)
d = run([sys.executable, os.path.join(TOOLS, 'generic', 'download_tabf_license.py')], tw)
if d.returncode != 0:
    summary.append('- ⚠️ 研訓院網站暫時無法連線，本次略過')
else:
    b = run([sys.executable, os.path.join(TOOLS, 'generic', 'build_tabf_license.py'), 'manifest.json', 'packs'], tw)
    f = os.path.join(tw, 'packs', 'papers_TABF-license.json.gz')
    if b.returncode != 0 or not os.path.exists(f):
        failed = True
        summary.append('- ❌ 解析失敗，本次不寫入')
    else:
        # 研訓院只保留最近幾屆；和既有題庫合併（同代碼的試卷不重複），舊屆次不刪除
        dst = os.path.join(SEED, 'papers_TABF-license.json.gz')
        old = json.load(gzip.open(dst, 'rt', encoding='utf-8')) if os.path.exists(dst) else []
        keys = {(p['examCode'], p['paperCode']) for p in old}
        fresh = [p for p in json.load(gzip.open(f, 'rt', encoding='utf-8')) if (p['examCode'], p['paperCode']) not in keys]
        if fresh:
            with gzip.open(dst, 'wt', encoding='utf-8') as fp:
                json.dump(old + fresh, fp, ensure_ascii=False, separators=(',', ':'))
        summary.append(f'- 新增 {len(fresh)} 份試卷' if fresh else '- 沒有新的屆次')

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

# 最後檢查：造字轉換與排除無法確定的題目（sanitize_packs.py）
r = run([sys.executable, os.path.join(TOOLS, 'sanitize_packs.py'), SEED], ROOT)
summary.append('')
summary.append(f"## 最後檢查\n- {r.stdout.strip().splitlines()[-1] if r.stdout.strip() else '完成'}")
if r.returncode != 0:
    failed = True

open(os.path.join(ROOT, 'update_summary.md'), 'w', encoding='utf-8').write('\n'.join(summary) + '\n')
print('\n'.join(summary))
sys.exit(1 if failed else 0)
