# 備考簿 ExamPrep

國家考試與國營事業招考的備考網站：題庫練習、錯題本、讀書計畫、考試行事曆與考試資訊。
以 ASP.NET Core MVC（.NET 10）、Entity Framework Core、SQL Server、ASP.NET Core Identity 開發。

## 功能

| 功能 | 說明 |
|---|---|
| 題庫練習 | 整卷作答，或依科目跨年度隨機抽題；交卷後批改並標出正解、連到原卷 PDF |
| 錯題本 | 答錯自動收錄、累計錯誤次數；同一題連續答對 2 次標為熟練；可寫筆記、依科目重練 |
| 讀書計畫 | 每科目標時數、每日讀書紀錄、近 14 天堆疊長條圖、每日目標；可從考試科目一鍵建立 |
| 考試行事曆 | 考期倒數（台灣時區）、報名期間提示、設定目標考試、匯出 .ics 到行事曆 App |
| 考試資訊 | 初等、高普考、地方特考、中華郵政、中華電信的應試科目與考期，每筆標示來源 |
| 會員 | Identity 註冊／登入、帳號鎖定、角色授權（管理員） |
| 管理 | 維護考期、CSV 匯入非考選部試題（逐列驗證，有錯整份不匯入）、重新匯入官方試題包 |

## 資料來源與真實性

- **官方試題**：取自[考選部考畢試題查詢平臺](https://wwwq.moex.gov.tw/exam/wFrmExamQandASearch.aspx)公開的試題與標準答案 PDF。
  收錄 113–115 年初等考試（全類科），113–115 年高普考、113–114 年地方特考的一般行政、一般民政、人事行政、資訊處理類科之測驗題。
  共 143 份試卷、官方 5,985 題，收錄 5,646 題（94.3%）。
- **排除規則**：含圖表（205 題）、含上下標／數學式／特殊符號或排版（133 題）、選項不完整（1 題）一律不收錄，並在試卷頁列出題號與原因。
- **獨立驗證**：`tools/moex/verify_packs.py` 對每一題檢查題幹、選項、題組文章是否逐字出現在原卷文字中，並以另一種方法（`pdftotext -layout`）重新讀答案表比對。目前 5,646 題全數通過。
- **考期與科目**：每筆都附來源名稱與網址；標「暫定」者為官方計畫表的預定日期。尚未公告的年度不預先填日期。
- 郵局、中華電信試題不在考選部平臺上，網站提供 CSV 匯入，須填寫來源網址。

## 執行方式

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download) 與 SQL Server。

### Windows（SQL Server LocalDB，Visual Studio 內建）

```bash
cd src/ExamPrep.Web
dotnet run
```

`appsettings.json` 預設連線字串為 `(localdb)\MSSQLLocalDB`。第一次啟動會自動建立資料庫、套用遷移、匯入考試資訊與官方試題（約 10–20 秒）。

### 其他系統（Docker 版 SQL Server）

```bash
docker compose up -d          # 啟動 SQL Server 2022
cd src/ExamPrep.Web
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost,1433;Database=ExamPrep;User Id=sa;Password=<與 docker-compose.yml 相同>;TrustServerCertificate=True"
dotnet run
```

### 建立管理員

管理員帳密只從設定讀取，程式碼不含密碼：

```bash
dotnet user-secrets set "SeedAdmin:Email" "you@example.com"
dotnet user-secrets set "SeedAdmin:Password" "<至少 8 碼，含小寫與數字>"
```

重新啟動後該帳號即具管理員角色。

### 測試

```bash
dotnet test                      # xUnit：計分、CSV 驗證、錯題熟練規則、台灣時區
python tools/e2e_test.py out/    # Playwright 端對端（需網站在 http://localhost:5080 執行）
```

## 專案結構

```
src/ExamPrep.Web/
  Controllers/      Home, Account, Calendar, Exams, Practice, WrongBook, Study, Admin
  Models/Domain.cs  實體：Exam, ExamSession, QuestionPaper, Question, PracticeSession, WrongQuestion, StudySubject, StudyLog …
  Services/         PracticeService（出題、批改、錯題本規則）、SeedDataService、QuestionCsvImporter、CountdownService、TaipeiDate
  Data/             AppDbContext、AppInitializer（遷移與種子資料）、Migrations
  SeedData/         exams.json（考試資訊）、papers/*.json（官方試題包）
tests/ExamPrep.Tests/   xUnit
tools/moex/         試題下載、PDF 解析、品質驗證（Python + PyMuPDF）
```

## 試題解析流程（tools/moex）

1. `moex_search.py`：模擬查詢頁 ASP.NET WebForms postback（`__VIEWSTATE`），取得各考試的類科、科目與 PDF 連結。
2. `download_all.py`：只下載有「標準答案」的測驗式科目，同一試題代號只下載一次。
3. `parse_exam.py`：用 PyMuPDF 讀出每個字元的座標與字型：
   - 題號：左邊界的數字，且必須等於「上一題 + 1」。
   - 選項：考選部用私有字碼（U+E18C～U+E190）或小圖示標記 (A)～(E)，依座標切分；兩欄排版的選項換行依 x 座標歸位。
   - 填空底線、文字底線、表格線依「線條與文字的垂直距離」區分；表格與圖片判定為圖表並排除。
   - 「請依下文回答第 N 題至第 M 題」的文章附到對應題組。
   - 答案表依「第 N 題」欄位的 x 座標對應答案，單複選題數取自答案 PDF 的題數說明。
4. `build_packs.py`：產生 `SeedData/papers/*.json`，並做逐字比對，不一致即排除。
5. `verify_packs.py`：獨立驗證（見上）。
