"""端對端測試：註冊、設定目標、作答、錯題本、讀書計畫、管理員 CSV 匯入驗證。輸出截圖。"""
import re, sys, os
from playwright.sync_api import sync_playwright, expect

BASE = 'http://localhost:5080'
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
log = []

def shot(page, name, full=True):
    page.screenshot(path=f'{OUT}/{name}.png', full_page=full)

with sync_playwright() as p:
    b = p.chromium.launch()
    ctx = b.new_context(viewport={'width': 1280, 'height': 860}, locale='zh-TW')
    page = ctx.new_page()
    errors = []
    page.on('pageerror', lambda e: errors.append(str(e)))
    page.on('dialog', lambda d: d.accept())

    page.goto(BASE + '/')
    shot(page, '01-landing')

    # 註冊
    page.goto(BASE + '/Account/Register')
    page.fill('#DisplayName', '測試考生')
    page.fill('#Email', 'tester@examprep.test')
    page.fill('#Password', 'tester1234')
    page.fill('#ConfirmPassword', 'tester1234')
    page.click('button[type=submit]')
    page.wait_for_url(BASE + '/')
    log.append('註冊並登入：OK')

    # 行事曆：設定兩個目標
    page.goto(BASE + '/Calendar')
    buttons = page.locator('button:has-text("設為目標")')
    n = buttons.count()
    buttons.first.click(); page.wait_for_load_state()
    page.locator('button:has-text("設為目標")').nth(1).click(); page.wait_for_load_state()
    assert page.locator('button:has-text("我的目標")').count() == 2
    log.append(f'行事曆可設目標（{n} 個未來考期）：OK')
    shot(page, '02-calendar')

    # 考試資訊頁，加入讀書計畫
    page.goto(BASE + '/Exams/chudeng')
    shot(page, '03-exam-detail')
    page.locator('form:has(input[value="一般行政"]) button').first.click()
    page.wait_for_load_state()
    assert '已加入 4 個科目' in page.content(), page.content()[:500]
    log.append('一鍵加入初等一般行政 4 科：OK')

    # 題庫：整卷作答 115 初等 行政學大意
    page.goto(BASE + '/Practice?exam=chudeng&year=115&q=行政學大意')
    shot(page, '04-practice-index')
    page.locator('button:has-text("作答")').first.click()
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    qs = page.locator('section.q')
    total = qs.count()
    # 前 10 題都選 A，其餘不作答
    for i in range(10):
        qs.nth(i).locator('.opt label').first.click()
    expect(page.locator('#answered-count')).to_have_text(f'10 / {total}')
    shot(page, '05-take', full=False)
    page.click('button:has-text("交卷批改")')
    page.wait_for_url(re.compile(r'/Practice/Result/\d+'))
    txt = page.locator('.paper-head').inner_text()
    m = re.search(r'答對\s*(\d+)\s*/\s*(\d+)', txt)
    correct, tot = int(m.group(1)), int(m.group(2))
    log.append(f'整卷作答與批改：{correct}/{tot}（全選 A 的對照組）')
    shot(page, '06-result')

    # 錯題本
    page.goto(BASE + '/WrongBook')
    wrong = page.locator('section.q').count()
    assert wrong == tot - correct, (wrong, tot, correct)
    log.append(f'錯題本收錄 {wrong} 題（= 總題數 − 答對數）：OK')
    page.locator('input[name=note]').first.fill('韋伯官僚制度的批評要分清楚')
    page.locator('button:has-text("存筆記")').first.click(); page.wait_for_load_state()
    shot(page, '07-wrongbook')

    # 錯題重練（熟練規則由單元測試驗證）
    page.goto(BASE + '/WrongBook')
    page.click('button:has-text("重練全部錯題")')
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    log.append(f'錯題重練出題 {page.locator("section.q").count()} 題：OK')
    # 隨機抽題
    page.goto(BASE + '/Practice')
    page.select_option('#count', '10')
    page.click('button:has-text("開始")')
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    assert page.locator('section.q').count() == 10
    log.append('依科目隨機抽 10 題：OK')

    # 郵局官方試題：整卷作答、題組文章顯示
    page.goto(BASE + '/Practice?exam=post&q=國文')
    assert page.locator('button:has-text("作答")').count() > 0, '找不到郵局試卷'
    page.locator('button:has-text("作答")').first.click()
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    np = page.locator('.q-passage').count()
    page.click('button:has-text("交卷批改")')
    page.wait_for_url(re.compile(r'/Practice/Result/\d+'))
    log.append(f'郵局國文英文卷作答與批改（含 {np} 段題組文章）：OK')
    page.goto(BASE + '/Practice?page=2')
    assert '第 2 /' in page.content()
    log.append('題庫分頁：OK')

    # 讀書計畫
    page.goto(BASE + '/Study')
    page.fill('#logMin', '90'); page.fill('#logNote', '行政學大意 考古題')
    page.click('form[action="/Study/Log"] button')
    page.wait_for_load_state()
    page.select_option('#logSubject', index=1); page.fill('#logMin', '45')
    page.click('form[action="/Study/Log"] button'); page.wait_for_load_state()
    assert '已記錄 45 分鐘' in page.content()
    log.append('讀書時間記錄：OK')
    shot(page, '08-study')

    page.goto(BASE + '/')
    shot(page, '09-dashboard')
    dash = page.locator('.dash-grid').inner_text()
    assert '135' in dash, dash
    log.append('儀表板今日分鐘數 135：OK')

    # 手機寬度
    m = b.new_context(viewport={'width': 390, 'height': 844}, locale='zh-TW', storage_state=ctx.storage_state())
    mp = m.new_page()
    for path, name in [('/', '10-mobile-dashboard'), ('/Calendar', '11-mobile-calendar')]:
        mp.goto(BASE + path)
        sw = mp.evaluate('document.documentElement.scrollWidth')
        assert sw <= 390, (path, sw)
        shot(mp, name)
    log.append('手機寬度無水平捲動：OK')

    # 一般會員不能進管理頁
    page.goto(BASE + '/Admin')
    assert '沒有權限' in page.content()
    log.append('一般會員進入管理頁被拒：OK')

    # 管理員：CSV 驗證（錯誤檔不得匯入）
    a = b.new_context(viewport={'width': 1280, 'height': 860}).new_page()
    a.goto(BASE + '/Account/Login')
    a.fill('#Email', 'admin@examprep.test'); a.fill('#Password', 'admin1234test')
    a.click('button[type=submit]'); a.wait_for_url(BASE + '/')
    a.goto(BASE + '/Admin/ImportCsv')
    bad = os.path.join(OUT, 'bad.csv')
    open(bad, 'w', encoding='utf-8-sig').write('題號,題型,題組文章,題幹,A,B,C,D,E,答案\n1,單選,,題幹,a,b,c,d,,AB\n1,單選,,,a,b,c,,,Z\n')
    a.fill('#ExamTitle', '驗證測試'); a.fill('#SubjectName', '驗證測試'); a.fill('#PaperCode', 'T1')
    a.fill('#SourceUrl', 'https://example.org/test')
    a.set_input_files('#file', bad)
    a.click('button:has-text("驗證並匯入")'); a.wait_for_load_state()
    errs = a.locator('.flash li').count()
    assert errs >= 4, a.content()[:2000]
    log.append(f'CSV 錯誤檔被拒絕並列出 {errs} 個錯誤：OK')
    shot(a, '12-admin-csv-errors')
    a.goto(BASE + '/Admin')
    shot(a, '13-admin')

    log.append(f'前端 JS 錯誤：{errors or "無"}')
    b.close()
print('\n'.join(log))
