"""考選部試題的考試分類。

考選部同一個「考試代碼」常把好幾種考試合在一起辦（例如關務、身障、國軍轉任同一場），
每份試卷的「類科」欄位前綴才寫出是哪一種考試（如「關務特考三等考試_財稅行政」）。
這裡依類科前綴（必要時再看考試名稱）判斷試卷屬於網站上的哪一個考試類別。

分類參考 gov.examshore.com、examly.tw 等題庫網站的類別，名稱以考選部正式名稱為準。
"""
import re

# (slug, 名稱, 簡稱, 群組) — 順序就是網站上的顯示順序
CATEGORIES = [
    ('gaopu', '公務人員高等考試三級考試暨普通考試', '高普考', '綜合國家考試'),
    ('local', '特種考試地方政府公務人員考試', '地方特考', '綜合國家考試'),
    ('chudeng', '公務人員初等考試', '初等考試', '綜合國家考試'),
    ('gaopu12', '公務人員高等考試一級暨二級考試', '高考一二級', '綜合國家考試'),
    ('judicial', '公務人員特種考試司法人員考試', '司法特考', '司法・調查・涉外'),
    ('judge', '公務人員特種考試司法官考試', '司法官', '司法・調查・涉外'),
    ('lawyer', '專門職業及技術人員高等考試律師考試', '律師', '司法・調查・涉外'),
    ('mjib', '公務人員特種考試法務部調查局調查人員考試', '調查局特考', '司法・調查・涉外'),
    ('foreign', '公務人員特種考試外交領事人員及外交行政人員考試', '外交特考', '司法・調查・涉外'),
    ('immigration', '公務人員特種考試移民行政人員考試', '移民特考', '司法・調查・涉外'),
    ('nsb', '公務人員特種考試國家安全情報人員考試', '國安局特考', '司法・調查・涉外'),
    ('intltrade', '公務人員特種考試國際經濟商務人員考試', '國際經濟商務', '司法・調查・涉外'),
    ('milaw', '國防法務官考試（含軍法官考試）', '國防法務官', '司法・調查・涉外'),
    ('police', '公務人員特種考試警察人員考試', '警察特考', '警察・消防・海巡'),
    ('gpolice', '公務人員特種考試一般警察人員考試', '一般警察特考', '警察・消防・海巡'),
    ('coast', '公務人員特種考試海岸巡防人員考試', '海巡特考', '警察・消防・海巡'),
    ('fire', '專門職業及技術人員考試消防設備人員', '消防設備人員', '警察・消防・海巡'),
    ('tax', '公務人員特種考試稅務人員考試', '稅務特考', '財稅・金融'),
    ('insurance', '專門職業及技術人員普通考試保險代理人、保險經紀人及保險公證人', '保險代理經紀公證人', '財稅・金融'),
    ('accountant', '專門職業及技術人員高等考試會計師考試', '會計師', '財稅・金融'),
    ('bookkeeper', '專門職業及技術人員普通考試記帳士考試', '記帳士', '財稅・金融'),
    ('customs', '公務人員特種考試關務人員考試', '關務特考', '關務・地政・不動產'),
    ('landagent', '專門職業及技術人員普通考試地政士考試', '地政士', '關務・地政・不動產'),
    ('realestate', '專門職業及技術人員普通考試不動產經紀人考試', '不動產經紀人', '關務・地政・不動產'),
    ('appraiser', '專門職業及技術人員高等考試不動產估價師考試', '不動產估價師', '關務・地政・不動產'),
    ('cbroker', '專門職業及技術人員普通考試專責報關人員考試', '專責報關人員', '關務・地政・不動產'),
    ('railway', '特種考試交通事業鐵路人員考試', '鐵路特考', '交通事業'),
    ('aviation', '公務人員特種考試民航人員考試', '民航特考', '交通事業'),
    ('railpromo', '交通事業鐵路人員升資考試', '鐵路人員升資', '交通事業'),
    ('portpromo', '交通事業港務人員升資考試', '港務人員升資', '交通事業'),
    ('highwaypromo', '交通事業公路人員升資考試', '公路人員升資', '交通事業'),
    ('patent', '專門職業及技術人員高等考試專利師考試', '專利師', '專技・證照'),
    ('guide', '專門職業及技術人員普通考試導遊人員、領隊人員考試', '導遊領隊', '專技・證照'),
    ('optometry', '專門職業及技術人員考試驗光人員', '驗光人員', '專技・證照'),
    ('socialwork', '專門職業及技術人員高等考試社會工作師考試', '社會工作師', '專技・證照'),
    ('foodtech', '專門職業及技術人員高等考試食品技師考試', '食品技師', '專技・證照'),
    ('engineer', '專門職業及技術人員高等考試技師考試（食品技師以外）', '技師', '專技・證照'),
    ('architect', '專門職業及技術人員高等考試建築師考試', '建築師', '專技・證照'),
    ('pro_other', '其他專技人員考試（驗船師、引水人、不動產估價師以外等）', '其他專技', '專技・證照'),
    ('medical', '專門職業及技術人員高等暨普通考試醫事人員', '醫事人員', '醫事人員'),
    ('promo', '公務人員升官等考試', '公務人員升官等', '升官等・轉任・其他'),
    ('customspromo', '關務人員升官等考試', '關務升官等', '升官等・轉任・其他'),
    ('policepromo', '警察人員升官等考試', '警察升官等', '升官等・轉任・其他'),
    ('postpromo', '交通事業郵政人員升資考試', '郵政人員升資', '升官等・轉任・其他'),
    ('indigenous', '公務人員特種考試原住民族考試', '原住民特考', '升官等・轉任・其他'),
    ('disability', '公務人員特種考試身心障礙人員考試', '身心障礙特考', '升官等・轉任・其他'),
    ('veteran', '特種考試退除役軍人轉任公務人員考試', '退除役特考', '升官等・轉任・其他'),
    ('military', '國軍上校以上軍官轉任公務人員考試', '國軍上校以上轉任', '升官等・轉任・其他'),
    ('special_other', '其他公務人員特種考試', '其他特考', '升官等・轉任・其他'),
]
SLUGS = {c[0] for c in CATEGORIES}

MEDICAL = ['醫師', '牙醫', '中醫', '藥師', '護理', '護士', '營養', '醫事檢驗', '醫事放射', '物理治療', '職能治療', '呼吸治療',
           '心理師', '語言治療', '聽力', '牙體技術', '助產', '獸醫', '法醫', '公共衛生師', '醫學', '藥劑生', '醫事人員']

# 依類科字串判斷（由具體到一般）
RULES = [
    ('postpromo', ['郵政']),
    ('railpromo', ['鐵路人員員級晉', '鐵路人員佐級晉', '鐵路人員士級晉', '鐵路員級晉', '鐵路佐級晉', '鐵路士級晉']),
    ('portpromo', ['港務']),
    ('highwaypromo', ['公路']),
    ('customspromo', ['關務人員升官等', '關務升官等', '關務薦任', '關務簡任']),
    ('policepromo', ['警察人員升官等', '警正', '警佐', '警監']),
    ('promo', ['升官等', '公務薦任', '公務簡任', '薦任升官等', '簡任升官等']),
    ('military', ['上校', '少將', '中將', '國軍轉任', '軍官轉任']),
    ('veteran', ['退除役']),
    ('disability', ['身障', '身心障礙']),
    ('indigenous', ['原住民']),
    ('customs', ['關務']),
    ('tax', ['稅務']),
    ('coast', ['海巡', '海岸巡防']),
    ('immigration', ['移民']),
    ('nsb', ['國安', '國家安全情報', '國家情報', '國安情報']),
    ('mjib', ['調查']),
    ('judge', ['司法官']),
    ('lawyer', ['律師']),
    ('milaw', ['國防法務官', '軍法官']),
    ('judicial', ['司法']),
    ('gpolice', ['一般警察']),
    ('police', ['警察']),
    ('foreign', ['外交領事', '外交行政']),
    ('intltrade', ['國際經濟商務', '國際商務']),
    ('aviation', ['民航']),
    ('railway', ['鐵路']),
    ('fire', ['消防設備']),
    ('insurance', ['保險代理', '保險經紀', '保險公證', '代理經紀公證']),
    ('guide', ['導遊', '領隊']),
    ('optometry', ['驗光']),
    ('socialwork', ['社會工作', '社工師']),
    ('foodtech', ['食品技師']),
    ('patent', ['專利師']),
    ('special_other', ['專利商標審查']),
    ('landagent', ['地政士']),
    ('realestate', ['不動產經紀']),
    ('appraiser', ['不動產估價']),
    ('cbroker', ['報關']),
    ('bookkeeper', ['記帳士']),
    ('accountant', ['會計師']),
    ('architect', ['建築師']),
    ('medical', MEDICAL),
    ('engineer', ['技師']),
    ('pro_other', ['驗船師', '引水人', '專技']),
]

# 類科字串看不出來時，依考試名稱（只含單一種考試時）判斷
TITLE_RULES = [
    ('chudeng', ['初等考試']),
    ('gaopu12', ['高等考試一級', '高考一級', '高考二級']),
    ('gaopu', ['高等考試三級考試暨普通考試', '高考三級', '高等暨普通考試']),
    ('local', ['地方政府公務人員', '地方特考', '離島地區']),
]


GENERIC = {'pro_other', 'engineer'}

# 早年（民國 81–100 年）的考試名稱：類科欄位多半是空的，只能看考試名稱。名稱裡只對到一種時才採用，
# 對到多種（合併舉辦）時回傳 None，交給 build_full.py 讀試卷第一頁的「等別」判斷。
OLD_TITLE_RULES = [
    ('postpromo', r'郵政人員升資'),
    ('railpromo', r'鐵路人員升資|鐵路、公路人員升資'),
    ('portpromo', r'港務人員升資'),
    ('highwaypromo', r'公路人員升資'),
    ('special_other', r'電信人員升資|電信、水運、民航人員升資|檢定考試|銓定資格'),
    ('policepromo', r'警察人員升官等'),
    ('customspromo', r'關務人員升官等'),
    ('promo', r'(?<!警察人員)(?<!關務人員)升(官)?等考試|公務人員升官等'),
    ('local', r'基層公務人員|地方政府(三、四等|公務人員)|離島地區'),
    ('gaopu12', r'高考一級|高等考試一級|高等一、二級|高考一、二級'),
    ('gaopu', r'高等暨普通考試|高考三級暨普考|高等考試三級(考試)?(暨|及)普通考試|高等三級及普考'),
    ('chudeng', r'初等考試'),
    ('disability', r'殘障|身心障礙'),
    ('indigenous', r'原住民'),
    ('veteran', r'退除役'),
    ('pro_other', r'航海人員|漁船船員|船舶電信|驗船師|引水人'),
]
SINGLE_TITLE = [
    ('chudeng', r'初等考試'),
    ('gaopu12', r'高等考試一級暨二級'),
    ('gaopu', r'高等考試三級考試暨普通考試'),
    ('local', r'地方政府公務人員考試'),
]


def _match(text, generic):
    for slug, keys in RULES:
        if (slug in GENERIC) != generic:
            continue
        if any(k in text for k in keys):
            return slug
    return None


def classify_header(text):
    """用試卷 PDF 第一頁的「等別」「類科」文字判斷（考試名稱合併多種考試、類科又看不出時使用）。"""
    m = re.search(r'等\s*別\s*[:：]\s*(.+)', text)
    head = (m.group(1) if m else '') + ' ' + (re.search(r'類\s*\(?科\)?\s*別?\s*[:：]\s*(.+)', text) or [''])[0]
    head = re.sub(r'\s+', '', head)
    if '航港' in head:
        return 'portpromo'
    if '高公局' in head:
        return 'highwaypromo'
    return _match(head, False)


def classify(title, category):
    """回傳網站考試類別 slug；判斷不了回傳 None。

    1. 類科前綴（例如「關務特考三等考試」）寫出考試種類就用它。
    2. 考試名稱只含高普考／地特／初等／高考一二級其中一種時，整場都歸該類（這些考試的類科
       名稱如「公職護理師」「財稅行政」會誤判，所以不看類科名稱）。
    3. 再看完整類科字串（前綴＋類科名稱，例如「高考_護理師」）。
    4. 都不行時，依考試名稱或「專技」字樣歸到一般類別。
    """
    category = category or ''
    prefix = category.split('_')[0]
    tnorm = re.sub(r'^第.梯次[:：]', '', title)
    hit = _match(prefix, False)
    if hit:
        return hit
    if re.fullmatch(r'(高員三級|高員級|員級|佐級)(考試)?', prefix) and '鐵路' in tnorm:
        return 'railway'
    if re.fullmatch(r'(高考三級|普考|普通考試|高考二級|高考一級)(考試)?', prefix) and re.search(r'高等考試三級|高考三級|高等三級|高考一級|高等一', tnorm):
        return 'gaopu12' if '高考一級' in prefix or '高考二級' in prefix else 'gaopu'
    if '專利商標' in prefix:
        return 'special_other'
    if '外交' in prefix:
        return 'foreign'
    kinds = [slug for slug, pat in SINGLE_TITLE if re.search(pat, tnorm)]
    others = re.sub(r'(公務人員)?(初等考試|高等考試一級暨二級考試|高等考試三級考試暨普通考試|特種考試地方政府公務人員考試|離島地區公務人員考試)', '', re.sub(r'^\d+年', '', tnorm))
    if len(kinds) == 1 and not re.search(r'[\u4e00-\u9fff]{2,}考試', others):
        return kinds[0]
    hit = _match(category, False)
    if hit:
        return hit
    if not re.search(r'[、及暨]', re.sub(r'高等考試三級考試暨普通考試|高等考試一級暨二級', '', tnorm)):
        hit = _match(tnorm, False)
        if hit:
            return hit
    hit = _match(category, True)
    if hit:
        return hit
    kinds = {slug for slug, pat in OLD_TITLE_RULES if re.search(pat, tnorm)}
    if len(kinds) == 1:
        return kinds.pop()
    if kinds:
        return None
    if re.search(r'專技|專門職業', tnorm):
        return 'pro_other'
    return None
