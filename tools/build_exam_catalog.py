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
    ('econ', '經濟部所屬事業機構新進職員甄試', '經濟部聯招', '國營事業', '經濟部所屬事業機構', 'StateEnterprise', 'https://www.moea.gov.tw/'),
    ('post', None, None, '國營事業', None, None, None),
    ('cht', None, None, '國營事業', None, None, None),
    ('sugar', '台灣糖業股份有限公司新進工員甄試', '台糖招考', '國營事業', '台灣糖業公司', 'StateEnterprise', 'https://www.taisugar.com.tw/'),
    ('ttl', '臺灣菸酒股份有限公司從業職員及從業評價職位人員甄試', '臺灣菸酒招考', '國營事業', '臺灣菸酒公司', 'StateEnterprise', 'https://www.ttl.com.tw/'),
    ('irrig', '灌溉管理組織新進農田水利事業人員甄試', '農田水利招考', '國營事業', '農業部農田水利署', 'StateEnterprise', 'https://www.ia.gov.tw/'),
    ('gsat', '大學學科能力測驗', '學測', '升學考試', '大學入學考試中心', 'Admission', 'https://www.ceec.edu.tw/'),
    ('ast', '大學分科測驗（指定科目考試）', '分科測驗／指考', '升學考試', '大學入學考試中心', 'Admission', 'https://www.ceec.edu.tw/'),
    ('finance', '台灣金融研訓院金融專業能力測驗（授信、外匯、理財、信託等）', '金融證照', '財稅・金融', '台灣金融研訓院', 'Certification', 'https://www.tabf.org.tw/LicenseHistoryExam.aspx'),
]
SUMMARY = {
    'econ': '台電、中油、台水、台糖等經濟部所屬事業機構聯合辦理的新進職員甄試。',
    'sugar': '台灣糖業公司自辦的新進工員甄試。',
    'ttl': '臺灣菸酒公司自辦的從業職員及從業評價職位人員甄試。',
    'irrig': '灌溉管理組織（各地管理處）辦理的新進農田水利事業人員甄試。',
    'gsat': '大學入學考試中心辦理的學科能力測驗；本站收錄大考中心網站公布的歷年試題與選擇題答案。',
    'ast': '大學入學考試中心辦理的分科測驗（111 學年度以前為指定科目考試）；本站收錄大考中心網站公布的歷年試題與選擇題答案。',
    'finance': '台灣金融研訓院辦理的金融專業能力測驗；本站收錄研訓院網站公布的歷屆試題。',
}
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
    e = by.get(slug)
    if e is None:
        e = {'slug': slug, 'name': name, 'shortName': short, 'organizer': org, 'organizerType': typ,
             'summary': SUMMARY[slug] + '這類考試的試題著作權屬主辦單位，公開版只列出試卷與原始來源連結。',
             'officialUrl': url, 'sessions': [], 'subjects': []}
        data['exams'].append(e)
        by[slug] = e
    e['group'] = group
    e['sortOrder'] = order
data['exams'].sort(key=lambda e: e['sortOrder'])
with open(PATH, 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
    f.write('\n')
print(len(data['exams']), '個考試類別')
