"""產生網站 SeedData/exams.json。

所有日期與科目都只取自可追溯來源（考選部試題平臺、各機關簡章或引用官方計畫表的報導），
每筆都附來源名稱與網址；尚未公告的年度不建立日期。
"""
import json, sys

MOEX_Q = 'https://wwwq.moex.gov.tw/exam/wFrmExamQandASearch.aspx'
PLAN_116 = ('高鋒公職整理之考選部《116年考試期日計畫表》（暫定）', 'https://www.kaozentaichung.com.tw/national-exam-calendar/')
NEWS_116_CHUDENG = ('1111人力銀行：116年初等考試暫定16類科（引考選部）', 'https://www.1111.com.tw/news/jobns/167484')


def moex_subjects(code, cats, track_name):
    idx = json.load(open(f'i{code}.json', encoding='utf-8'))
    out = []
    for cat in cats:
        for x in idx:
            if x['category'] == cat:
                out.append({'track': track_name(cat), 'subjectName': x['subject'],
                            'format': '含測驗題' if x['a'] else '申論',
                            'sourceName': f'考選部考畢試題查詢平臺（{code[:3]}年試題）', 'sourceUrl': MOEX_Q})
    return out


chudeng_cats = sorted({x['category'] for x in json.load(open('i115010.json', encoding='utf-8'))},
                      key=lambda c: [x['category'] for x in json.load(open('i115010.json', encoding='utf-8'))].index(c))

exams = [
    {
        'slug': 'chudeng', 'name': '公務人員初等考試', 'shortName': '初等考試', 'organizer': '考選部',
        'organizerType': 'NationalExam', 'sortOrder': 1, 'officialUrl': 'https://www.moex.gov.tw/',
        'summary': '國家考試中層級最基礎的公務人員考試，各科均為選擇題，錄取後以初等（委任第一職等）任用。',
        'sessions': [{
            'title': '116年公務人員初等考試', 'registrationStart': '2026-10-27', 'registrationEnd': '2026-11-05',
            'examStart': '2027-01-09', 'examEnd': '2027-01-09', 'isTentative': True,
            'note': '類科與名額暫定（暫定16類科、528名），以考試院審議通過之考試計畫及簡章為準。',
            'sourceName': NEWS_116_CHUDENG[0], 'sourceUrl': NEWS_116_CHUDENG[1]}],
        'subjects': moex_subjects('115010', chudeng_cats, lambda c: c.split('_', 1)[1]),
    },
    {
        'slug': 'gaopu', 'name': '公務人員高等考試三級考試暨普通考試', 'shortName': '高普考', 'organizer': '考選部',
        'organizerType': 'NationalExam', 'sortOrder': 2, 'officialUrl': 'https://www.moex.gov.tw/',
        'summary': '高考三級（大學以上）與普考（高中職以上）同期舉行，為公務人員主要的進用管道。普通科目為國文與法學知識與英文。',
        'sessions': [
            {'title': '116年普通考試', 'registrationStart': '2027-03-09', 'registrationEnd': '2027-03-18',
             'examStart': '2027-07-02', 'examEnd': '2027-07-03', 'isTentative': True,
             'note': '與高考三級同一報名期間。', 'sourceName': PLAN_116[0], 'sourceUrl': PLAN_116[1]},
            {'title': '116年高等考試三級考試', 'registrationStart': '2027-03-09', 'registrationEnd': '2027-03-18',
             'examStart': '2027-07-04', 'examEnd': '2027-07-06', 'isTentative': True,
             'note': '與普考同一報名期間。', 'sourceName': PLAN_116[0], 'sourceUrl': PLAN_116[1]},
        ],
        'subjects': moex_subjects('115080', ['普通考試_一般行政', '普通考試_資訊處理', '高考三級_一般行政', '高考三級_資訊處理'],
                                  lambda c: c.replace('_', '｜')),
    },
    {
        'slug': 'local', 'name': '特種考試地方政府公務人員考試', 'shortName': '地方特考', 'organizer': '考選部',
        'organizerType': 'NationalExam', 'sortOrder': 3, 'officialUrl': 'https://www.moex.gov.tw/',
        'summary': '分區報考、分區錄取、分區分發，錄取後須在報考區服務一定年限，適合想固定在特定縣市（例如高屏）工作的考生。',
        'sessions': [
            {'title': '115年地方特考', 'examStart': '2026-12-05', 'examEnd': '2026-12-07', 'isTentative': True,
             'note': '報名已截止。', 'sourceName': PLAN_116[0], 'sourceUrl': PLAN_116[1]},
            {'title': '116年地方特考', 'registrationStart': '2027-09-07', 'registrationEnd': '2027-09-16',
             'examStart': '2027-12-04', 'examEnd': '2027-12-06', 'isTentative': True,
             'note': '116年預定增設苗栗考區。', 'sourceName': PLAN_116[0], 'sourceUrl': PLAN_116[1]},
        ],
        'subjects': moex_subjects('114190', ['地方政府公務人員三等_一般行政類科', '地方政府公務人員四等_一般行政類科',
                                             '地方政府公務人員三等_資訊處理類科', '地方政府公務人員四等_資訊處理類科'],
                                  lambda c: c.replace('地方政府公務人員', '').replace('_', '｜').replace('類科', '')),
    },
    {
        'slug': 'post', 'name': '中華郵政職階人員甄試', 'shortName': '郵局招考', 'organizer': '中華郵政',
        'organizerType': 'StateEnterprise', 'sortOrder': 4, 'officialUrl': 'https://www.post.gov.tw/',
        'summary': '中華郵政公司自辦的從業人員甄試（非國家考試），試務委託台灣金融研訓院辦理，分營運職、專業職(一)、專業職(二)內勤／外勤，錄取後依分發區分發。官方於甄試網站公告近年試題與選擇題答案。',
        'sessions': [
            {'title': '115年中華郵政職階人員甄試', 'registrationStart': '2026-05-18', 'registrationEnd': '2026-06-10',
             'examStart': '2026-07-19', 'examEnd': '2026-07-19', 'isTentative': False,
             'note': '口試與體能測驗 115/9/5、9/6；116 年度尚未公告。',
             'sourceName': '考友社：115年中華郵政職階人員甄試', 'sourceUrl': 'https://www.examiner.com.tw/examInfo_detail.php?id=201'}],
        'subjects': [
            *[{'track': '專業職(二)內勤｜櫃台業務、郵務處理', 'subjectName': s, 'format': f, 'sourceName': '考友社：115年中華郵政職階人員甄試',
               'sourceUrl': 'https://www.examiner.com.tw/examInfo_detail.php?id=201'} for s, f in [
                ('國文（短文寫作、閱讀測驗）及英文', '寫作＋選擇'), ('企業管理大意及洗錢防制法大意', '選擇'), ('郵政三法大意及金融科技知識', '選擇')]],
            *[{'track': '專業職(二)外勤｜郵遞業務、運輸業務', 'subjectName': s, 'format': '選擇', 'sourceName': '考友社：115年中華郵政職階人員甄試',
               'sourceUrl': 'https://www.examiner.com.tw/examInfo_detail.php?id=201'} for s in [
                '國文及英文', '臺灣自然及人文地理', '郵政法規大意及交通安全常識']],
            *[{'track': '專業職(一)｜共同科目', 'subjectName': s, 'format': f, 'sourceName': '考友社：115年中華郵政職階人員甄試',
               'sourceUrl': 'https://www.examiner.com.tw/examInfo_detail.php?id=201'} for s, f in [
                ('國文（短文寫作、閱讀測驗）及英文', '寫作＋選擇'), ('郵政三法及金融科技知識', '選擇'), ('專業科目二科（依類科而定）', '非選擇')]],
        ],
    },
    {
        'slug': 'cht', 'name': '中華電信基層從業人員遴選', 'shortName': '中華電信', 'organizer': '中華電信',
        'organizerType': 'StateEnterprise', 'sortOrder': 5, 'officialUrl': 'https://www.cht.com.tw/',
        'summary': '中華電信自辦遴選（非國家考試），第一試筆試普通科目占 20%、專業科目占 80%，雙北、臺中、高雄設考區。101–109 年由台灣金融研訓院辦理，本站收錄 101–103 年研訓院版式的原卷（由 1111 人力銀行轉載保存）；近年改由松盟科技辦理，簡章明定「各節試卷一律回收，亦不公告試題及解答」，所以近年沒有公開試題。',
        'sessions': [
            {'title': '115年中華電信基層從業人員遴選', 'registrationStart': '2026-01-02', 'registrationEnd': '2026-02-02',
             'examStart': '2026-03-14', 'examEnd': '2026-03-14', 'isTentative': False,
             'note': '116 年度尚未公告。',
             'sourceName': '115年中華電信基層從業人員遴選簡章',
             'sourceUrl': 'https://www.ipeecs.ncu.edu.tw/wp-content/uploads/2026/01/115年中華電信基層從業人員遴選簡章.pdf'}],
        'subjects': [
            {'track': t, 'subjectName': s, 'format': '筆試', 'sourceName': '115年中華電信基層從業人員遴選簡章',
             'sourceUrl': 'https://www.ipeecs.ncu.edu.tw/wp-content/uploads/2026/01/115年中華電信基層從業人員遴選簡章.pdf'}
            for t, ss in [
                ('普通科目（各類別）', ['英文', '邏輯思維']),
                ('業務類｜行銷及企業客戶業務推廣', ['企業管理', '行銷學']),
                ('技術類｜資訊系統開發及維運', ['計算機概論', '程式設計概論']),
                ('技術類｜企業客戶技術服務', ['計算機概論', '專案管理', '行銷學']),
                ('技術類｜電信網路規劃設計及維運', ['計算機概論', '基本電學', '電腦網路與通信系統']),
                ('技術類｜電信線路建設與維運（普通科目僅考邏輯思維）', ['計算機概論', '基本電學']),
            ] for s in ss],
    },
]
json.dump({'exams': exams}, open(sys.argv[1], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print({e['slug']: (len(e['sessions']), len(e['subjects'])) for e in exams})
