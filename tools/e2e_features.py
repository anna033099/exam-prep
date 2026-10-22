"""新功能端對端測試：逐題對答、計時模擬考、自訂練習、收藏與筆記、討論與勘誤、搜尋、排行榜、
每日推薦、原卷 ZIP 下載、公開模式。用法：python e2e_features.py <截圖資料夾> [public]"""
import re, sys, os, io, zipfile, time
from playwright.sync_api import sync_playwright, expect

BASE = os.environ.get('E2E_BASE', 'http://localhost:5080')
# 模擬反向代理（Hugging Face）送來的 HTTPS 標頭
HEADERS = {'X-Forwarded-Proto': 'https'} if os.environ.get('E2E_FORWARDED') else {}
OUT = sys.argv[1]
PUBLIC = len(sys.argv) > 2 and sys.argv[2] == 'public'
os.makedirs(OUT, exist_ok=True)
log = []


def login(page, email, pw):
    page.goto(BASE + '/Account/Login')
    page.fill('#Email', email); page.fill('#Password', pw)
    page.click('form[action^="/Account/Login"] button[type=submit]')
    page.wait_for_url(BASE + '/')


def register(page, name, email, pw):
    page.goto(BASE + '/Account/Register')
    page.fill('#DisplayName', name); page.fill('#Email', email)
    page.fill('#Password', pw); page.fill('#ConfirmPassword', pw)
    page.click('form[action^="/Account/Register"] button[type=submit]')
    page.wait_for_url(BASE + '/')


with sync_playwright() as p:
    b = p.chromium.launch()
    ctx = b.new_context(viewport={'width': 1280, 'height': 900}, locale='zh-TW', accept_downloads=True, extra_http_headers=HEADERS)
    page = ctx.new_page()
    errors = []
    page.on('pageerror', lambda e: errors.append(str(e)))
    page.on('dialog', lambda d: d.accept())
    suffix = str(int(time.time()))
    register(page, '功能測試' + suffix[-3:], f'feat{suffix}@examprep.test', 'feature1234')

    if PUBLIC:
        page.goto(BASE + '/Practice?exam=post')
        assert '公開版不提供內文' in page.content()
        assert page.locator('form[action="/Practice/StartPaper"]').count() == 0
        page.locator('a.name').first.click(); page.wait_for_load_state()
        assert '公開版不提供這份試卷的題目內文' in page.content()
        page.goto(BASE + '/Search?q=郵政法')
        found = int(re.search(r'找到\s*([\d,]+)', page.inner_text('main')).group(1).replace(',', ''))
        log.append(f'公開模式：郵局試卷只有清單與來源、無作答按鈕；搜尋「郵政法」{found} 題（不含郵局內文）：OK')
        page.screenshot(path=f'{OUT}/p-public-post.png')

    # 逐題對答
    page.goto(BASE + '/Practice?exam=chudeng&year=114&q=行政學大意')
    page.locator('a.name').first.click(); page.wait_for_load_state()
    page.click('button:has-text("逐題對答")')
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    first = page.locator('section.q').first
    first.locator('.opt label').first.click()
    expect(first.locator('.js-verdict')).to_contain_text('答')
    assert first.locator('.opt.is-answer').count() == 1
    assert first.locator('input:disabled').count() == 4
    page.screenshot(path=f'{OUT}/f-instant.png', full_page=False)
    page.reload()
    assert page.locator('section.q').first.locator('input:disabled').count() == 4
    log.append('逐題對答：點選立即顯示正解、重新整理後保留：OK')
    page.click('button:has-text("結束並看結果")')
    page.wait_for_url(re.compile(r'/Practice/Result/\d+'))

    # 計時模擬考（1 分鐘，時間到自動交卷）
    page.goto(BASE + '/Practice?exam=chudeng&year=113&q=法學大意')
    page.locator('a.name').first.click(); page.wait_for_load_state()
    page.evaluate("""() => { const f=[...document.querySelectorAll('form')].find(f=>f.querySelector('input[name=mode][value=exam]'));
                         f.querySelector('input[name=minutes]').value='1'; f.submit(); }""")
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    expect(page.locator('#timer')).to_have_text(re.compile(r'00:\d\d'))
    page.wait_for_url(re.compile(r'/Practice/Result/\d+'), timeout=90000)
    log.append('計時模擬考：倒數結束自動交卷：OK')

    # 自訂練習：循序、指定科目
    page.goto(BASE + '/Practice/Custom?exam=chudeng')
    page.fill('#subject-filter', '行政學大意')
    page.locator('#subject-list .form-check:not([hidden]) input').first.check()
    page.select_option('#yf', '110'); page.select_option('#yt', '110')
    page.select_option('#order', 'sequential'); page.select_option('#count2', '10')
    page.click('button:has-text("開始練習")')
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    foots = page.locator('.q-foot').all_inner_texts()
    nums = [int(re.search(r'第 (\d+) 題', t).group(1)) for t in foots]
    assert len(nums) == 10 and nums == sorted(nums) and all('110 年' in t for t in foots), foots[:3]
    log.append(f'自訂練習（110 年行政學大意、循序 10 題）：題號 {nums[0]}–{nums[-1]} 依序：OK')
    qhref = page.locator('section.q').first.get_attribute('data-qid')

    # 單題頁：收藏、筆記、留言、勘誤
    page.goto(f'{BASE}/Q/{qhref}')
    page.click('#reveal')
    assert page.locator('.opt.is-answer').count() >= 1
    page.click('button:has-text("收藏這題")'); page.wait_for_load_state()
    assert '已收藏' in page.content()
    page.fill('textarea[name=body][maxlength="2000"]', '韋伯：層級節制、依法行政')
    page.click('button:has-text("儲存筆記")'); page.wait_for_load_state()
    assert '韋伯：層級節制' in page.content()
    page.fill('#body', '這題要注意「錯誤」的選項。')
    page.click('button:has-text("送出留言")'); page.wait_for_load_state()
    assert '這題要注意' in page.content()
    page.select_option('select[name=kind]', '2')
    page.fill('textarea[name=detail]', '測試回報：請對照原卷答案')
    page.click('button:has-text("送出回報")'); page.wait_for_load_state()
    assert '已收到回報' in page.content()
    assert '沒有啟用 AI 詳解' in page.content() or '產生 AI 詳解' in page.content()
    page.screenshot(path=f'{OUT}/f-question.png', full_page=True)
    log.append('單題頁：看答案、收藏、筆記、留言、勘誤回報：OK')

    page.goto(BASE + '/Practice/Bookmarks')
    assert page.locator('.paper-row').count() == 1
    page.goto(BASE + '/Practice/Custom')
    page.select_option('#source', '3'); page.click('button:has-text("開始練習")')
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    assert page.locator('section.q').count() == 1
    log.append('收藏清單與「只出收藏的」出題：OK')

    # 搜尋
    page.goto(BASE + '/Search?q=比例原則')
    n = int(re.search(r'找到\s*([\d,]+)', page.inner_text('main')).group(1).replace(',', ''))
    assert n > 0
    page.locator('.paper-row a.name').first.click(); page.wait_for_url(re.compile(r'/Q/\d+'))
    assert '比例原則' in page.inner_text('main')
    page.goto(BASE + '/Search?q=比例原則'); page.screenshot(path=f'{OUT}/f-search.png')
    log.append(f'題目全文搜尋「比例原則」：{n} 題，結果可開啟：OK')

    # 每日推薦
    page.goto(BASE + '/')
    page.click('button:has-text("開始今日推薦")')
    page.wait_for_url(re.compile(r'/Practice/Take/\d+'))
    cnt = page.locator('section.q').count()
    assert cnt == 20, cnt
    page.goto(BASE + '/'); page.screenshot(path=f'{OUT}/f-dashboard.png')
    log.append(f'每日推薦：出 {cnt} 題（逐題模式）：OK')

    # 排行榜
    page.goto(BASE + '/Leaderboard')
    assert '（你）' in page.content()
    page.click('button:has-text("公開我的暱稱")'); page.wait_for_load_state()
    assert '從排行榜隱藏我' in page.content()
    page.screenshot(path=f'{OUT}/f-leaderboard.png')
    anon = b.new_context(extra_http_headers=HEADERS).new_page(); anon.goto(BASE + '/Leaderboard')
    assert '功能測試' in anon.content()
    log.append('排行榜：本人可見、公開後他人可見：OK')

    # 原卷 ZIP（需要能連到考選部；沙盒內的容器可設 E2E_SKIP_ZIP=1 略過）
    if not os.environ.get('E2E_SKIP_ZIP'):
        page.goto(BASE + '/Practice?exam=chudeng&year=115&q=行政學大意')
        page.locator('a.name').first.click(); page.wait_for_load_state()
        link = page.locator('a:has-text("歷年原卷 ZIP")')
        with page.expect_download(timeout=240000) as dl:
            link.click()
        data = open(dl.value.path(), 'rb').read()
        z = zipfile.ZipFile(io.BytesIO(data))
        pdfs = [n for n in z.namelist() if n.endswith('.pdf')]
        assert len(pdfs) >= 10 and all(z.read(x)[:4] == b'%PDF' for x in pdfs[:4]) and '來源說明.txt' in z.namelist()
        log.append(f'原卷 ZIP：{len(pdfs)} 個 PDF＋來源說明，{len(data)//1024} KB：OK')

    # 管理員：勘誤處理、留言管理
    a = b.new_context(viewport={'width': 1280, 'height': 900}, extra_http_headers=HEADERS).new_page()
    login(a, 'admin@examprep.test', 'admin1234test')
    a.goto(BASE + '/Admin/Errata')
    assert '測試回報' in a.content()
    a.screenshot(path=f'{OUT}/f-admin-errata.png', full_page=True)
    a.locator('button:has-text("不成立")').first.click(); a.wait_for_load_state()
    a.goto(BASE + '/Admin/Errata?status=Rejected')
    assert '測試回報' in a.content()
    log.append('管理員勘誤佇列：處理後移到「不成立」：OK')

    log.append(f'前端 JS 錯誤：{errors or "無"}')
    b.close()
print('\n'.join(log))
