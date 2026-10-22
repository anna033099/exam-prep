"""從考選部政府資料開放資料集更新考期（初等、高普考、地方特考）。

資料來源（考選部提供、政府資料開放平臺登錄，每年更新）：
- 162767 公務人員高普初升官（資）等歷年考試辦理期日
- 166612 公務人員特種考試歷年各項考試辦理期日（地方特考在這份）

欄位：年度;考試簡稱;報名開始日期;報名結束日期;考試開始日期;考試結束日期;第一試放榜日期;…

只寫入官方資料裡有的年度，並以官方資料取代同名的「暫定」考期；官方還沒列入的年度
（例如明年的考試計畫）維持原本標示來源的暫定資料。欄位或日期格式跟預期不同時整批不寫入，
以免把錯誤日期放上網站。

用法：python tools/update_dates.py [--dump 資料夾] [--dry-run]
"""
import argparse, csv, io, json, os, re, sys, time
from datetime import date

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXAMS = os.path.join(ROOT, 'src', 'ExamPrep.Web', 'SeedData', 'exams.json')
FILE_URL = 'https://wwwc.moex.gov.tw/main/content/wHandMenuFile.ashx?file_id={}'
SOURCES = [
    {'file_id': 4484, 'dataset': 162767, 'name': '公務人員高普初升官（資）等歷年考試辦理期日'},
    {'file_id': 4509, 'dataset': 166612, 'name': '公務人員特種考試歷年各項考試辦理期日'},
]
REQUIRED = ['年度', '考試簡稱', '報名開始日期', '報名結束日期', '考試開始日期', '考試結束日期']


class FormatError(Exception):
    pass


def fetch(file_id):
    import requests
    last = None
    for attempt in range(4):
        try:
            r = requests.get(FILE_URL.format(file_id), timeout=60,
                             headers={'User-Agent': 'ExamPrep/1.0 (+study site; monthly update)'})
            r.raise_for_status()
            return r.content
        except Exception as e:  # 網路暫時錯誤時重試
            last = e
            print(f'下載 file_id={file_id} 失敗，重試：{e}', flush=True)
            time.sleep(10 * (attempt + 1))
    raise RuntimeError(f'無法下載 file_id={file_id}：{last}')


def decode(raw):
    for enc in ('utf-8-sig', 'cp950'):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            pass
    raise FormatError('無法辨識檔案編碼')


def read_rows(text):
    first = text.splitlines()[0] if text.strip() else ''
    delim = ';' if first.count(';') > first.count(',') else ','
    rows = list(csv.reader(io.StringIO(text), delimiter=delim))
    header = [h.strip() for h in rows[0]]
    missing = [c for c in REQUIRED if c not in header]
    if missing:
        raise FormatError(f'缺少欄位 {missing}，實際欄位 {header}')
    return [dict(zip(header, [c.strip() for c in r])) for r in rows[1:] if any(c.strip() for c in r)]


def parse_date(s):
    """支援 115/01/02、115.1.2、1150102（民國）與 2026/01/02、20260102（西元）。空白回傳 None。"""
    s = s.strip()
    if not s or s in ('-', '－', '無'):
        return None
    m = re.fullmatch(r'(\d{2,4})[/.\-年](\d{1,2})[/.\-月](\d{1,2})日?', s)
    if m:
        y, mo, d = map(int, m.groups())
    elif re.fullmatch(r'\d{7,8}', s):
        y, mo, d = int(s[:-4]), int(s[-4:-2]), int(s[-2:])
    else:
        raise FormatError(f'無法解析日期「{s}」')
    if y < 1911:
        y += 1911
    return date(y, mo, d)


def roc_year(s):
    m = re.search(r'\d{2,4}', s)
    if not m:
        raise FormatError(f'無法解析年度「{s}」')
    y = int(m.group())
    return y - 1911 if y > 1911 else y


def classify(name):
    """回傳 [(網站考試代號, 考期標題字尾)]；不相關的考試回傳空清單。"""
    n = name.replace(' ', '')
    if '升官' in n or '升資' in n or '身心障礙' in n or '原住民' in n:
        return []
    if '初等' in n:
        return [('chudeng', '公務人員初等考試')]
    if '地方' in n and '特種' in n or n.startswith('地方特考') or n in ('地特', '地方特考'):
        return [('local', '地方特考')]
    if '高等考試三級' in n and '普通' in n or n in ('高普考', '高考三級暨普考'):
        return [('gaopu', '高普考')]
    if '高等考試三級' in n or n in ('高考三級', '高考'):
        return [('gaopu', '高等考試三級考試')]
    if n.startswith('普通考試') or n in ('普考',):
        return [('gaopu', '普通考試')]
    return []


def collect(dump=None, min_year=None):
    """下載並解析官方資料，回傳 {slug: [session dict]}。"""
    out = {}
    for src in SOURCES:
        raw = fetch(src['file_id'])
        if dump:
            os.makedirs(dump, exist_ok=True)
            open(os.path.join(dump, f"moex_{src['file_id']}.csv"), 'wb').write(raw)
        rows = read_rows(decode(raw))
        print(f"{src['name']}：{len(rows)} 筆", flush=True)
        for r in rows:
            for slug, suffix in classify(r['考試簡稱']):
                y = roc_year(r['年度'])
                if min_year and y < min_year:
                    continue
                start = parse_date(r['考試開始日期'])
                if start is None:
                    continue
                end = parse_date(r['考試結束日期']) or start
                rs, re_ = parse_date(r['報名開始日期']), parse_date(r['報名結束日期'])
                for a, b in ((rs, re_), (start, end)):
                    if a and b and b < a:
                        raise FormatError(f'日期前後顛倒：{r}')
                if re_ and re_ > start:
                    raise FormatError(f'報名截止晚於考試日：{r}')
                if not (y + 1911 - 1 <= start.year <= y + 1911 + 1):
                    raise FormatError(f'考試日期與年度不符：{r}')
                out.setdefault(slug, []).append({
                    'title': f'{y}年{suffix}',
                    'registrationStart': rs.isoformat() if rs else None,
                    'registrationEnd': re_.isoformat() if re_ else None,
                    'examStart': start.isoformat(),
                    'examEnd': end.isoformat(),
                    'isTentative': False,
                    'note': '',
                    'sourceName': f"考選部政府資料開放：{src['name']}",
                    'sourceUrl': f"https://data.gov.tw/dataset/{src['dataset']}",
                    '_name': r['考試簡稱'],
                })
    return out


def merge(data, official):
    """以官方考期取代同年度、同考試的資料；回傳變更說明。"""
    changes = []
    for e in data['exams']:
        new = official.get(e['slug'], [])
        if not new:
            continue
        years = {s['title'].split('年')[0] for s in new}
        names = {s['title'] for s in new}
        # 官方資料若把高普考分成兩筆，就移除合併的那筆；反之亦然。同年度的暫定資料一律以官方為準。
        kept = [s for s in e['sessions'] if not (s['title'].split('年')[0] in years and
                                                  (s['title'] in names or s.get('isTentative')))]
        old = {s['title']: s for s in e['sessions']}
        for s in new:
            s = {k: v for k, v in s.items() if not k.startswith('_') and v is not None}
            prev = old.get(s['title'])
            if prev != s:
                changes.append(f"{e['slug']}：{s['title']} 考試 {s['examStart']}～{s['examEnd']}"
                               + (f"、報名 {s.get('registrationStart')}～{s.get('registrationEnd')}" if s.get('registrationStart') else '')
                               + ('（新增）' if prev is None else '（更新）'))
            kept.append(s)
        removed = [s['title'] for s in e['sessions'] if s not in kept and s['title'] not in names]
        changes += [f"{e['slug']}：移除暫定的 {t}（同年度已有官方資料）" for t in removed]
        e['sessions'] = sorted(kept, key=lambda s: s['examStart'])
    return changes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--dump', help='把下載的原始 CSV 存到這個資料夾')
    ap.add_argument('--dry-run', action='store_true')
    ap.add_argument('--summary', help='把結果附加到這個 Markdown 檔')
    args = ap.parse_args()
    this_roc = date.today().year - 1911
    lines = ['## 考期（考選部政府資料開放）']
    try:
        official = collect(args.dump, min_year=this_roc - 1)
        data = json.load(open(EXAMS, encoding='utf-8'))
        changes = merge(data, official)
        for slug, ss in official.items():
            for s in ss:
                print(f"{slug}\t{s['_name']}\t{s['title']}\t{s.get('registrationStart')}~{s.get('registrationEnd')}\t{s['examStart']}~{s['examEnd']}")
        if changes and not args.dry_run:
            with open(EXAMS, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=1)
                f.write('\n')
        lines += [f'- {c}' for c in changes] or ['- 沒有變更']
        ok = True
    except (FormatError, RuntimeError) as e:
        lines.append(f'- **失敗，未寫入**：{e}')
        ok = False
    print('\n'.join(lines))
    if args.summary:
        with open(args.summary, 'a', encoding='utf-8') as f:
            f.write('\n'.join(lines) + '\n\n')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
