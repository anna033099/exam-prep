"""民國 81–90 年考選部試題：原卷是掃描影像（沒有文字層），本站不做文字辨識（OCR 會有錯字，
無法逐字驗證），只建立試卷清單與原卷連結，讓使用者知道有這份考古題並可直接開啟官方 PDF。

類別判斷不了（合併舉辦、類科欄位空白）時，把可能的類別都連上，不猜單一類別。
用法：MOEX_CODES=all_codes.json python build_scans.py <輸出資料夾> <最大民國年>
"""
import collections, gzip, json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from taxonomy import classify, OLD_TITLE_RULES

BASE = 'https://wwwq.moex.gov.tw/exam/'
NOTE = '原卷為掃描影像（沒有文字層）。本站不做文字辨識，以免辨識錯字造成錯誤題目，請直接開啟原卷 PDF 閱讀。'


def main():
    out_dir, max_year = sys.argv[1], int(sys.argv[2])
    os.makedirs(out_dir, exist_ok=True)
    codes = json.load(open(os.environ.get('MOEX_CODES', 'all_codes.json'), encoding='utf-8'))
    total = 0
    unresolved = collections.Counter()
    for code, (year, title) in sorted(codes.items()):
        roc = int(code[:3])
        if roc > max_year or not os.path.exists(f'i{code}.json'):
            continue
        papers = {}
        for it in json.load(open(f'i{code}.json', encoding='utf-8')):
            if not it['q']:
                continue
            q = it['q'].replace('&amp;', '&')
            s = re.search(r'&s=(\w+)', q).group(1)
            c = re.search(r'&c=(\w+)', q).group(1)
            cat = it['category'] or ''
            slug = classify(title, cat)
            slugs = [slug] if slug else [k for k, pat in OLD_TITLE_RULES if re.search(pat, title)] or ['special_other']
            if not slug:
                unresolved[title[:40]] += 1
            p = papers.get(s)
            if p is None:
                p = papers[s] = {
                    'examCode': code, 'paperCode': f'{c}-{s}', 'examSlug': slugs[0], 'examSlugs': slugs[1:],
                    'examTitle': re.sub(r'^\d+年', '', title), 'rocYear': roc, 'categories': [],
                    'subjectName': it['subject'][:200], 'sourceQuestionUrl': BASE + q,
                    'sourceAnswerUrl': BASE + it['a'].replace('&amp;', '&') if it['a'] else '',
                    'sourceNote': NOTE, 'answerNote': '', 'officialQuestionCount': 0,
                    'excluded': [], 'questions': [], 'essays': [],
                }
            if cat and cat not in p['categories']:
                p['categories'].append(cat)
            for sl in slugs:
                if sl != p['examSlug'] and sl not in p['examSlugs']:
                    p['examSlugs'].append(sl)
        if papers:
            with gzip.open(f'{out_dir}/papers_{code}.json.gz', 'wt', encoding='utf-8') as f:
                json.dump(list(papers.values()), f, ensure_ascii=False, separators=(',', ':'))
            total += len(papers)
    print(f'掃描檔試卷清單 {total} 份；類別無法確定（連到多個可能類別）的考試：{len(unresolved)} 個')


if __name__ == '__main__':
    main()
