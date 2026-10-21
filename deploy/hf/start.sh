#!/usr/bin/env bash
# 容器啟動流程：
#   1. 有外部連線字串 → 直接啟動網站。
#   2. 否則啟動容器內的 SQL Server Express；若設定了 HF_TOKEN 與 BACKUP_BUCKET，先從 Bucket 還原最新備份。
#   3. 啟動網站，並在背景每 BACKUP_INTERVAL_MIN 分鐘把資料庫備份上傳到 Bucket。
set -euo pipefail
cd /home/user/app

if [[ -n "${ConnectionStrings__Default:-}" ]]; then
  echo "[start] 使用外部資料庫"
  exec dotnet ExamPrep.Web.dll
fi

export MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-$(head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 20)Aa1!}"
export ConnectionStrings__Default="Server=localhost,1433;Database=ExamPrep;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=True"
SQL=(sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b)
BAK=/home/user/backup/ExamPrep.bak
REMOTE="hf://buckets/${BACKUP_BUCKET:-}/ExamPrep.bak"

/opt/mssql/bin/sqlservr > /home/user/sqlserver.log 2>&1 &
echo "[start] 等待 SQL Server 啟動…"
for i in $(seq 1 90); do
  if "${SQL[@]}" -Q "SELECT 1" > /dev/null 2>&1; then break; fi
  sleep 2
done
"${SQL[@]}" -Q "SELECT @@VERSION" | head -2

can_backup() { [[ -n "${HF_TOKEN:-}" && -n "${BACKUP_BUCKET:-}" ]]; }

if can_backup; then
  echo "[start] 嘗試從 ${BACKUP_BUCKET} 還原備份"
  if hf buckets cp "$REMOTE" "$BAK" > /dev/null 2>&1 && [[ -s "$BAK" ]]; then
    "${SQL[@]}" -Q "RESTORE DATABASE [ExamPrep] FROM DISK = N'$BAK' WITH REPLACE,
      MOVE 'ExamPrep' TO '/var/opt/mssql/data/ExamPrep.mdf',
      MOVE 'ExamPrep_log' TO '/var/opt/mssql/data/ExamPrep_log.ldf'" \
      && echo "[start] 已還原備份" || echo "[start] 還原失敗，以新資料庫啟動"
  else
    echo "[start] Bucket 尚無備份，以新資料庫啟動"
  fi
else
  echo "[start] 未設定 HF_TOKEN／BACKUP_BUCKET：資料只存在容器內，Space 重啟後會清空"
fi

backup_loop() {
  while true; do
    sleep "$(( ${BACKUP_INTERVAL_MIN:-15} * 60 ))"
    if "${SQL[@]}" -Q "BACKUP DATABASE [ExamPrep] TO DISK = N'$BAK' WITH COPY_ONLY, INIT, FORMAT" > /dev/null 2>&1 \
       && hf buckets cp "$BAK" "$REMOTE" > /dev/null 2>&1; then
      echo "[backup] $(date -u +%FT%TZ) 已備份到 ${BACKUP_BUCKET}"
    else
      echo "[backup] $(date -u +%FT%TZ) 備份失敗"
    fi
  done
}
if can_backup; then backup_loop & fi

exec dotnet ExamPrep.Web.dll
