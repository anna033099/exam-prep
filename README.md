# 備考簿 ExamPrep

國家考試、國營事業招考、金融證照與升學考試的備考網站：題庫練習、錯題本、讀書計畫、考試行事曆與考試資訊。
以 ASP.NET Core MVC（.NET 10）、Entity Framework Core、SQL Server、ASP.NET Core Identity 開發。

- **展示版（GitHub Pages）**：https://anna033099.github.io/exam-prep/ ——Blazor WebAssembly 版，在瀏覽器裡瀏覽考選部題庫、逐題作答、錯題本（存在瀏覽器）。
- **完整版**：在自己電腦執行（見下方「執行方式」），有會員、間隔複習、模擬考、討論、AI 詳解等全部功能。

## 功能

| 功能 | 說明 |
|---|---|
| 題庫 | 58 個考試類別依群組顯示（綜合國家考試、司法・調查・涉外、警察・消防・海巡、財稅・金融、關務・地政・不動產、交通事業、專技・證照、醫事人員、升官等・轉任・其他、國營事業、升學考試）；同一份試卷可同時屬於多個類別（例如同一場考試多種特考共用的國文） |
| 刷題模式 | 整卷作答、逐題即時對答、計時模擬考（時間到自動交卷）、依科目隨機抽題、自訂範圍（考試／年度區間／多科目／循序或隨機／只出沒做過、錯題或收藏） |
| 申論題 | 收錄原題文字（逐字比對原卷），可閱讀、寫筆記、討論、產生「AI 參考解答」；沒有官方答案，不進入刷題計分 |
| 單題頁 | 看答案、收藏、個人筆記、討論留言（可檢舉、3 次自動隱藏）、勘誤回報、AI 詳解 |
| AI 詳解 | 需要時才呼叫 Claude API 產生並快取；提示中給定官方答案、要求不得推翻；頁面明確標示「AI 生成・可能有誤」；每人每日上限；未設定金鑰時功能隱藏 |
| 錯題本與間隔複習 | 答錯自動收錄，依 1、3、7、14、30 天間隔安排複習；連續答對 2 次標為熟練；筆記與單題頁共用 |
| 每日任務 | 每日刷題目標、連續刷題天數；「今日推薦」先排到期複習，再從正確率最低的科目挑沒做過的題目 |
| 排行榜 | 本週作答題數與正確率；預設不公開，使用者自行選擇是否顯示暱稱 |
| 搜尋與下載 | 題目全文搜尋（題幹、選項、題組文章；數十萬題用計算欄位＋二進位排序規則加速，結果快取供翻頁）；同科目多年度考選部原卷一鍵打包 ZIP（附來源說明） |
| 讀書計畫 | 每科目標時數、每日讀書紀錄、近 14 天堆疊長條圖、每日目標；可從考試科目一鍵建立 |
| 考試行事曆 | 考期倒數（台灣時區）、報名期間提示、設定目標考試、匯出 .ics 到行事曆 App |
| 會員 | Identity 註冊／登入、帳號鎖定、角色授權（管理員）；登入金鑰存於資料庫，重新部署不會登出 |
| 管理 | 勘誤佇列、留言管理、維護考期、CSV 匯入、重新匯入官方試題包 |
| 公開模式 | `Site:PublicMode=true` 時，非考選部辦理的考試（國營事業、金融證照、升學考試；試題著作權屬主辦單位）只列試卷與原始來源，不提供內文 |
| 自動更新 | GitHub Actions 每月檢查考選部、台灣金融研訓院與考期開放資料，新題目逐題驗證通過才寫入，並重新發布展示版 |

## 資料來源與真實性

| 來源 | 年度 | 試卷 | 原卷選擇題 | 收錄選擇題 | 收錄申論題 |
|---|---|---|---|---|---|
| [考選部考畢試題查詢平臺](https://wwwq.moex.gov.tw/exam/wFrmExamQandASearch.aspx)：全部考試、全部類科 | 91–115 | 40,182 | 424,787 | 382,793 | 126,548 |
| 同上：原卷為掃描影像（只列試卷與原卷連結） | 81–90 | 7,433 | — | — | — |
| 中華郵政：委託[台灣金融研訓院](https://svc.tabf.org.tw/115post02//Paper/Year)公告之考古題 | 111–114 | 37 | 1,632 | 1,595 | — |
| 中華郵政：[5138 考神網](https://www.5138.com.tw/exam.htm)轉載之原卷 | 107–110 | 43 | 1,725 | 1,640 | 2 |
| 經濟部所屬事業機構聯合甄試（5138 考神網轉載之原卷） | 108–114 | 300 | 6,224 | 4,876 | 784 |
| 臺灣菸酒、台糖、農田水利（5138 考神網轉載之原卷） | 108–113 | 170 | 5,927 | 5,024 | 1 |
| 中華電信遴選（研訓院辦理時之原卷，[1111 人力銀行轉載](https://www.1111job.com.tw/news.asp?ano=3146)） | 101–103 | 41 | 2,380 | 2,070 | — |
| [台灣金融研訓院歷屆試題](https://www.tabf.org.tw/LicenseHistoryExam.aspx)（授信、外匯、理財、信託、內控內稽等） | 最近 2–3 屆 | 78 | 4,312 | 3,466 | — |
| [大學入學考試中心](https://www.ceec.edu.tw/)：學測、分科測驗／指考 | 93–115 | 276 | 10,335 | 6,691 | 17 |
| **合計** | | **48,560** | **457,322** | **408,155（89.2%）** | **127,352** |

- **逐字驗證**：每一題的題幹、選項、題組文章都必須逐字出現在原卷文字中（PyMuPDF 與 poppler `pdftotext` 兩種讀法），不一致即排除。
- **答案雙重讀取**：標準答案用兩種方法讀取（字元座標、`pdftotext -layout`），結果不一致的題目排除；目前 0 題不一致。
- **排除規則**：含圖表、上下標、數學式、特殊符號、選項不完整、字型對應錯誤（亂碼），或無法逐字還原的題目一律不收錄，並在試卷頁列出題號與原因。含圖表的申論題照收但標示「請對照原卷」。
- **民國 81–90 年**：考選部平臺的原卷是掃描影像，沒有文字層。不做文字辨識（辨識錯字無法逐字驗證），只列試卷與原卷連結。
- **轉載來源**：5138 考神網（robots.txt 允許存取）與 1111 人力銀行轉載的檔案，只收第一頁抬頭為主辦單位原卷格式者，每份試卷頁標示轉載來源。
- **未收錄的類別**：台電僱員、中油雇員、捷運、銀行招考、農漁會、教師甄試、技術士等，目前沒有找到可以合法取得的公開原卷（有些網站 robots.txt 禁止抓取、有些官方網站擋國外連線），所以不收錄。
- **考期與科目**：每筆都附來源名稱與網址；標「暫定」者為官方計畫表的預定日期。尚未公告的年度不預先填日期。

## 執行方式

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download) 與 SQL Server。

### Windows（Visual Studio，最簡單）

1. 安裝 [Visual Studio 2026 Community](https://visualstudio.microsoft.com/)，勾選「ASP.NET 與網頁程式開發」工作負載（內含 .NET 10 SDK 與 SQL Server LocalDB）。
2. 下載本專案（GitHub → Code → Download ZIP 或 `git clone`），用 Visual Studio 開啟 `ExamPrep.slnx`。
3. 啟動專案設為 `ExamPrep.Web`，按 F5。

或在命令列：

```bash
cd src/ExamPrep.Web
dotnet run
```

`appsettings.json` 預設連線字串為 `(localdb)\MSSQLLocalDB`。第一次啟動會自動建立資料庫、套用遷移、匯入考試資訊與全部試題（約 48,000 份試卷、53 萬題，用 SqlBulkCopy 寫入，需數分鐘），之後啟動只檢查有沒有新試題。

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
python tools/e2e_test.py out/          # Playwright 端對端：基本流程
python tools/e2e_features.py out/      # Playwright 端對端：刷題模式、單題頁、搜尋、排行榜、ZIP、勘誤
python tools/e2e_features.py out/ public   # 以 Site__PublicMode=true 啟動網站時，另外檢查公開模式
```

展示版（Blazor WebAssembly）在本機預覽：

```bash
dotnet publish src/ExamPrep.Showcase -c Release -o out/showcase
python tools/showcase/build_data.py src/ExamPrep.Web/SeedData out/showcase/wwwroot/data
python -m http.server -d out/showcase/wwwroot 8080
```

## 部署與自動更新

- **GitHub Pages 展示版**：`.github/workflows/pages.yml` 把題庫轉成靜態 JSON（只放考選部試題），連同 Blazor WebAssembly 網站發布到 GitHub Pages。第一次使用前需在 repo → Settings → Pages 把 Source 設為「GitHub Actions」。
- **完整版**：需要能執行 ASP.NET Core 與 SQL Server 的主機。根目錄 `Dockerfile` 只含網站，資料庫以 `ConnectionStrings__Default` 指向外部 SQL Server（例如 Azure SQL）。`deploy/hf/` 是 Hugging Face Docker Space 版本（內含 SQL Server Express 與備份還原）；2026 年 7 月起 Hugging Face 免費帳號不能建立 Docker Space，該工作流程改為只能手動執行。
- **每月更新題庫**（`.github/workflows/update-questions.yml`，每月 5 日）：`tools/update_questions.py` 找出考選部今年與去年尚未收錄的考試（全部考試、全部類科），下載、解析、逐題驗證後寫入；同時更新研訓院郵局考古題與金融證照歷屆試題。有變更就提交並重新發布展示版。
- **每月更新考期**（`.github/workflows/update-dates.yml`，每月 6 日）：`tools/update_dates.py` 讀考選部政府資料開放的「歷年考試辦理期日」CSV，更新初等、高普考、地方特考考期。考選部主站目前擋國外連線（GitHub 主機連不到），連不到時略過並警告、考期維持原狀；可改用在台灣網路的自架執行器，或用 `--from` 讀手動下載的 CSV。

## 專案結構

```
src/ExamPrep.Web/
  Controllers/      Home, Account, Calendar, Exams, Practice, WrongBook, Study, Admin
  Models/Domain.cs  實體：Exam, ExamSession, QuestionPaper, Question, PracticeSession, WrongQuestion, StudySubject, StudyLog …
  Services/         PracticeService（出題、批改、錯題本規則）、SeedDataService、QuestionCsvImporter、CountdownService、TaipeiDate
  Data/             AppDbContext、AppInitializer（遷移與種子資料）、Migrations
  SeedData/         exams.json（考試類別與考期）、papers/*.json.gz（試題包）
src/ExamPrep.Showcase/  Blazor WebAssembly 展示版（GitHub Pages）
tests/ExamPrep.Tests/   xUnit
tools/moex/         考選部試題：查詢、下載、PDF 解析（選擇題與申論題）、考試分類（taxonomy.py）、驗證
tools/tabf/、tools/cht/  中華郵政、中華電信：研訓院格式 PDF 解析（A3 雙欄）、驗證
tools/generic/      文字型選擇題通用解析器；經濟部聯招等國營事業、金融證照、大考中心的下載與建置
tools/showcase/     展示版靜態資料產生器
tools/textnorm.py   相容表意字、康熙部首轉標準字（PDF 字型常把「利、行、列」對應到外觀相同的相容字）
```

## 試題解析流程

### 考選部（tools/moex）

1. `moex_search.py`：模擬查詢頁 ASP.NET WebForms postback（`__VIEWSTATE`），取得各考試的類科、科目與 PDF 連結。
2. `download_full.py`：下載全部考試、全部類科的試題 PDF（有測驗式標準答案的一併下載）；`taxonomy.py` 依類科前綴與考試名稱判斷考試類別，判斷不了時讀試卷第一頁的「等別」。
3. `parse_exam.py`：用 PyMuPDF 讀出每個字元的座標與字型：
   - 題號：左邊界的數字，且必須等於「上一題 + 1」。
   - 選項：考選部用私有字碼（U+E18C～U+E190）或小圖示標記 (A)～(E)，依座標切分；兩欄排版的選項換行依 x 座標歸位。
   - 填空底線、文字底線、表格線依「線條與文字的垂直距離」區分；表格與圖片判定為圖表並排除。
   - 「請依下文回答第 N 題至第 M 題」的文章附到對應題組。
   - 答案表依「第 N 題」欄位的 x 座標對應答案，單複選題數取自答案 PDF 的題數說明。
   - 申論題（`parse_essays`）：取「一、二、三…」各題，保留小題與原卷分段；考選部造字圈號 ①–④ 轉成標準字。
   - 醫事人員等試卷的選項是一般文字「A.」：整份試卷沒有選項符號時才啟用文字選項判讀。
   - 民國 90 年代混合式試卷「乙、測驗部分」的題號是造字（U+E0C6 起依序 1、2、3…），只在剛好等於下一題號時採用。
4. `build_full.py`：產生 `SeedData/papers/*.json.gz`，逐題比對原卷文字、答案以第二種方法交叉核對，不一致即排除並記錄原因；單份試卷解析失敗不影響其他試卷。
5. 答案 PDF 有「第 N 題」、「題號 01 02 …」、「題序 01 02 …」三種格式，各有對應規則。

### 中華郵政（tools/tabf）

1. `tabf_probe.py`：探測台灣金融研訓院各年度試務網站的考古題頁，取得試卷 PDF 清單。
2. `parse_tabf.py`：A3 橫式雙欄，先切左右欄再依座標讀；選項標記為 Wingdings ①～④；答案取自題號前的【n】（含官方更正的【改n】）；
   「閱讀測驗」「克漏字」「第N篇」「【第16-20題為題組】」等段落下的文字作為題組文章，其他位置出現的游離文字視為表格內容並排除該題。
3. `build_post_packs.py`：只處理選擇題科目，逐字比對後輸出；同一份卷在不同年度網站重複公告者合併。
4. `verify_post.py`：以 pdftotext 重新讀取【n】答案比對，並做逐字比對。
