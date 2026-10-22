"""把 tools/moex/taxonomy.py 的考試類別寫進 SeedData/exams.json。

既有考試（初等、高普考、地特、郵局、中華電信）保留原本的考期與科目資料，只補上群組與排序；
新類別只寫名稱、主辦機關、群組與官方連結，不寫考期（考期只放有官方來源的資料）。
"""
import json, os, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'tools', 'moex'))
from taxonomy import CATEGORIES

PATH = os.path.join(ROOT, 'src', 'ExamPrep.Web', 'SeedData', 'exams.json')
MOEX_Q = 'https://wwwq.moex.gov.tw/exam/wFrmExamQandASearch.aspx'
# 非考選部類別（國營事業、證照、升學、其他招考）：(slug, 名稱, 簡稱, 群組, 主辦, 類型, 官方網址)
EXTRA = [
    ('post', None, None, '國營事業', None, None, None),
    ('cht', None, None, '國營事業', None, None, None),
]
data = json.load(open(PATH, encoding='utf-8'))
by = {e['slug']: e for e in data['exams']}
order = 0
for slug, name, short, group in CATEGORIES:
    order += 1
    e = by.get(slug)
    if e is None:
        e = {'slug': slug, 'name': name, 'shortName': short, 'organizer': '考選部', 'organizerType': 'NationalExam',
             'summary': f'考選部辦理的{name}。本站收錄考選部考畢試題查詢平臺公開的歷屆試題：選擇題附官方標準答案、可以作答計分；申論題可以閱讀、寫筆記與討論。',
             'officialUrl': MOEX_Q, 'sessions': [], 'subjects': []}
        data['exams'].append(e)
        by[slug] = e
    e['group'] = group
    e['sortOrder'] = order
for slug, name, short, group, org, typ, url in EXTRA:
    order += 1
    e = by[slug]
    e['group'] = group
    e['sortOrder'] = order
data['exams'].sort(key=lambda e: e['sortOrder'])
with open(PATH, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
    f.write('\n')
print(len(data['exams']), '個考試類別')
