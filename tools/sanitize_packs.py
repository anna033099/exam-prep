"""題庫包最後檢查：把已確認對應的造字轉成標準字，其餘仍含私有區字元（Private Use Area）的題目排除。

- 考選部造字 U+E0C6 起依序是題號 1、2、3…（舊版混合式試卷），出現在題組說明「回答第～題」裡時轉成數字。
- 考選部造字 U+E129–U+E12C 是圈號 ①–④（由注意事項「①禁止使用電子計算器 ②不必抄題」的順序確認）。
- 其他私有區字元無法確定原字，含有它們的題目改列為「未收錄」並註明原因。
- 題幹或題組文章裡出現「【3】12.」這種研訓院答案標記，表示兩題被誤併成一題，同樣排除。

用法：python sanitize_packs.py <papers 資料夾或檔案…>
"""
import glob, gzip, json, os, re, sys

NUM_BASE = 0xE0C6
EUDC = {'': '①', '': '②', '': '③', '': '④'}
PUA = re.compile(r'[-]')
MERGED = re.compile(r'【(?:改\s*)?[1-4](?:,[1-4])*】\s*\d{1,3}\s*[.．]')


def fix(s):
    if not s:
        return s
    s = ''.join(EUDC.get(ch, ch) for ch in s)
    # 「回答第～題」「第～題至第～題」裡的造字題號
    return re.sub(r'(?<=第)([-])|([-])(?=\s*[~～－-]|\s*題)',
                  lambda m: str(ord(m.group(0)) - NUM_BASE + 1) if ord(m.group(0)) - NUM_BASE < 99 else m.group(0), s)


def load(path):
    return json.load(gzip.open(path, 'rt', encoding='utf-8')) if path.endswith('.gz') else json.load(open(path, encoding='utf-8'))


def save(path, data):
    if path.endswith('.gz'):
        with gzip.open(path, 'wt', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, separators=(',', ':'))
    else:
        json.dump(data, open(path, 'w', encoding='utf-8'), ensure_ascii=False)


def sanitize(path):
    data = load(path)
    changed = removed = 0
    for p in data:
        keep = []
        for q in p['questions']:
            before = json.dumps(q, ensure_ascii=False)
            q['stem'] = fix(q['stem'])
            q['passage'] = fix(q.get('passage'))
            q['options'] = {k: fix(v) for k, v in q['options'].items()}
            text = q['stem'] + ''.join(q['options'].values()) + (q['passage'] or '')
            reason = None
            if PUA.search(text):
                reason = '含特殊符號（原卷造字），純文字無法正確呈現'
            elif MERGED.search(q['stem'] + (q['passage'] or '') + ''.join(q['options'].values())):
                reason = '題目切分有誤（與下一題相連），為避免錯誤不收錄'
            if reason:
                p['excluded'].append({'no': q['no'], 'reason': reason})
                removed += 1
                continue
            changed += before != json.dumps(q, ensure_ascii=False)
            keep.append(q)
        p['questions'] = keep
        essays = []
        for e in p.get('essays', []):
            e['text'] = fix(e['text'])
            if PUA.search(e['text']):
                p['excluded'].append({'no': 1000 + e['no'], 'reason': '含特殊符號（原卷造字），純文字無法正確呈現'})
                removed += 1
                continue
            essays.append(e)
        if 'essays' in p:
            p['essays'] = essays
        p['excluded'].sort(key=lambda e: e['no'])
        if not p['sourceQuestionUrl'].startswith('http'):
            p['sourceQuestionUrl'] = p['sourceQuestionUrl'].strip().replace('%20', '', 1).strip()
            changed += 1
    if changed or removed:
        save(path, data)
    return changed, removed


def main():
    paths = []
    for a in sys.argv[1:]:
        paths += sorted(glob.glob(os.path.join(a, 'papers_*.json*'))) if os.path.isdir(a) else [a]
    tc = tr = 0
    for p in paths:
        c, r = sanitize(p)
        tc += c; tr += r
    print(f'檢查 {len(paths)} 個題庫包：轉換造字 {tc} 處，排除 {tr} 題')


if __name__ == '__main__':
    main()
