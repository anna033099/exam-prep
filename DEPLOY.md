# 部署到 Hugging Face Space（免費）

部署完成後，網址會是 `https://<你的帳號>-<space 名稱>.hf.space`，也會出現在 `https://huggingface.co/spaces/<你的帳號>/<space 名稱>`。

## 先知道的限制

| 項目 | 說明 |
|---|---|
| 硬體 | 免費 CPU Basic：2 vCPU、16 GB 記憶體、50 GB 磁碟 |
| 休眠 | 48 小時沒人造訪會休眠，下一位訪客進來時自動喚醒（約需 1～2 分鐘啟動 SQL Server 與網站） |
| 資料 | Space 的磁碟重啟就會清空。網站每 15 分鐘把資料庫備份到 Storage Bucket，啟動時自動還原；最壞情況會遺失最近 15 分鐘的資料 |
| 題目 | 公開模式預設開啟：郵局、中華電信只列試卷與原始來源，不顯示題目內文 |

## 步驟一：GitHub

1. 在 GitHub 建一個**空的** repo，例如 `exam-prep`（不要勾選 README）。
2. 把程式推上去（或告訴 Claude repo 名稱，由 Claude 推送）：
   ```bash
   git remote add origin https://github.com/<帳號>/exam-prep.git
   git push -u origin main
   ```

## 步驟二：Hugging Face

1. **建立 Space**：<https://huggingface.co/new-space>
   - Space name：`exam-prep`
   - SDK：選 **Docker** → **Blank**
   - Hardware：**CPU basic（free）**
   - Visibility：Public
2. **建立 Storage Bucket**（存放資料庫備份）：<https://huggingface.co/new-bucket>
   - 名稱：`examprep-db`，設為 **Private**
3. **建立存取權杖**：<https://huggingface.co/settings/tokens> → Create new token
   - 類型選 **Write**（若有 Fine-grained 選項，只勾上面這個 Space 與 Bucket 的寫入權限）
   - 複製權杖（`hf_` 開頭），等一下會用兩次
4. **Space 的 Secrets**：Space 頁面 → Settings → Variables and secrets → New secret

   | 名稱 | 值 | 說明 |
   |---|---|---|
   | `HF_TOKEN` | 剛才的權杖 | 讓容器讀寫備份 Bucket |
   | `BACKUP_BUCKET` | `<你的帳號>/examprep-db` | 備份位置 |
   | `SeedAdmin__Email` | 你的 Email | 第一次啟動時建立管理員 |
   | `SeedAdmin__Password` | 至少 8 碼、含小寫與數字 | 管理員密碼 |
   | `Ai__AnthropicApiKey` | （選填）Anthropic API 金鑰 | 填了才會出現「AI 詳解」；會產生 API 費用 |

## 步驟三：連接 GitHub Actions

GitHub repo → Settings → Secrets and variables → Actions：

- **Secrets** 分頁 → New repository secret：`HF_TOKEN` = 同一組 Hugging Face 權杖
- **Variables** 分頁 → New repository variable：`HF_SPACE` = `<你的帳號>/exam-prep`

然後到 Actions 分頁 → 「部署到 Hugging Face Space」→ Run workflow。之後每次推送到 main 都會自動部署。

## 步驟四：確認

1. Space 頁面會顯示 Building → Running（第一次建置約 5～10 分鐘）。
2. 開啟網站，用 `SeedAdmin__Email` 登入，右上會出現「管理」。
3. Space 的 Logs 應看到 `[start] Bucket 尚無備份，以新資料庫啟動`；15 分鐘後看到 `[backup] … 已備份`。

## 每月自動更新

`.github/workflows/update-questions.yml` 每月 5 日台灣時間 11:17 自動執行，也可以在 Actions 頁面按 Run workflow 手動執行。結果摘要會顯示在該次執行的頁面上：

- 有新題目且全部驗證通過 → 自動提交到 main → 重新部署到 Space
- 驗證有任何一題不一致 → 不寫入，該次執行標示失敗（GitHub 會寄信通知）
- 沒有新題目 → 什麼都不做

## 改用外部資料庫（選用）

如果之後想要更穩定的資料保存，可以在 Space Secrets 加上 `ConnectionStrings__Default`（例如 Azure SQL 免費方案的連線字串）。設定後容器不會啟動內建 SQL Server，也不需要 `BACKUP_BUCKET`。
