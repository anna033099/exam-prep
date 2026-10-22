"""把 SeedData 題庫轉成 GitHub Pages 展示版（Blazor WebAssembly）讀的靜態 JSON。

只放考選部辦理的國家考試（試題依著作權法第 9 條不受保護）；國營事業、證照、升學等
主辦單位自有著作權的試題，展示版和公開版一樣只列試卷與原始連結，不放題目內文。

輸出：
  data/catalog.json          考試類別、群組、題數統計
  data/exams/<slug>.json     該類別的試卷清單
  data/papers/<代碼>.json    一個考試代碼（一場考試）的全部試卷題目
用法：python build_data.py <SeedData 資料夾> <輸出資料夾>
"""
import collections, glob, gzip, json, os, re, sys


def load(path):
    if path.endswith('.gz'):
        return json.load(gzip.open(path, 'rt', encoding='utf-8'))
    return json.load(open(path, encoding='utf-8'))


def main():
    seed, out = sys.argv[1], sys.argv[2]
    exams = load(os.path.join(seed, 'exams.json'))['exams']
    national = {e['slug'] for e in exams if e.get('organizerType', 'NationalExam') == 'NationalExam'}
    os.makedirs(os.path.join(out, 'exams'), exist_ok=True)
    os.makedirs(os.path.join(out, 'papers'), exist_ok=True)
    lists = collections.defaultdict(list)
    stats = collections.defaultdict(lambda: {'papers': 0, 'choice': 0, 'essay': 0, 'first': 9999, 'last': 0})
    totals = {'papers': 0, 'choice': 0, 'essay': 0}
    seen = set()
    files = sorted(glob.glob(os.path.join(seed, 'papers', 'papers_*.json*')))
    # 同一份試卷若同時有舊格式（.json）與新格式（.json.gz），以新格式為準
    gz = {f[:-3] for f in files if f.endswith('.gz')}
    files = [f for f in files if f.endswith('.gz') or f not in gz]
    for f in files:
        chunk = {}
        code = re.sub(r'\.json(\.gz)?$', '', os.path.basename(f))[len('papers_'):]
        for p in load(f):
            slugs = [s for s in [p['examSlug'], *p.get('examSlugs', [])] if s in national]
            if not slugs:
                continue
            pid = f"{p['examCode']}_{p['paperCode']}"
            if pid in seen:
                continue
            seen.add(pid)
            choice, essays = len(p['questions']), len(p.get('essays', []))
            row = {'id': pid, 'f': code, 'y': p['rocYear'], 't': p['examTitle'], 's': p['subjectName'],
                   'c': p['categories'][:3], 'n': choice, 'e': essays, 'o': p['officialQuestionCount']}
            for s in slugs:
                lists[s].append(row)
                st = stats[s]
                st['papers'] += 1; st['choice'] += choice; st['essay'] += essays
                st['first'] = min(st['first'], p['rocYear']); st['last'] = max(st['last'], p['rocYear'])
            totals['papers'] += 1; totals['choice'] += choice; totals['essay'] += essays
            chunk[pid] = {
                'id': pid, 'y': p['rocYear'], 't': p['examTitle'], 's': p['subjectName'], 'c': p['categories'],
                'u': p['sourceQuestionUrl'], 'a': p.get('sourceAnswerUrl') or '', 'note': p.get('sourceNote') or '',
                'an': p.get('answerNote') or '', 'o': p['officialQuestionCount'], 'x': p.get('excluded', []),
                'q': [{'n': q['no'], 'm': q['type'] == 'multi', 'p': q.get('passage'), 't': q['stem'],
                       'o': [q['options'][k] for k in sorted(q['options'])], 'a': q['answer']} for q in p['questions']],
                'es': [{'n': e['no'], 't': e['text'], 'r': e.get('remark')} for e in p.get('essays', [])],
            }
        if chunk:
            json.dump(chunk, open(os.path.join(out, 'papers', f'{code}.json'), 'w', encoding='utf-8'),
                      ensure_ascii=False, separators=(',', ':'))
    for slug, rows in lists.items():
        rows.sort(key=lambda r: (-r['y'], r['s']))
        json.dump(rows, open(os.path.join(out, 'exams', f'{slug}.json'), 'w', encoding='utf-8'),
                  ensure_ascii=False, separators=(',', ':'))
    cat = []
    for e in sorted(exams, key=lambda e: e.get('sortOrder', 0)):
        if e['slug'] not in national or e['slug'] not in stats:
            continue
        st = stats[e['slug']]
        cat.append({'slug': e['slug'], 'name': e['name'], 'short': e['shortName'], 'group': e.get('group', ''),
                    'summary': e.get('summary', ''), **st})
    json.dump({'totals': totals, 'exams': cat}, open(os.path.join(out, 'catalog.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, separators=(',', ':'))
    print(f"類別 {len(cat)}；試卷 {totals['papers']}；選擇題 {totals['choice']}；申論題 {totals['essay']}")


if __name__ == '__main__':
    main()
