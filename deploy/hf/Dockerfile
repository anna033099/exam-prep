# Hugging Face Docker Space：網站與 SQL Server 在同一個容器裡。
# 免費 Space 的磁碟重啟就會清空，所以啟動時從 Storage Bucket 還原備份、執行期間定時備份回去。
# 若設定了 ConnectionStrings__Default（例如 Azure SQL），就不啟動內建資料庫。

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ExamPrep.Web/ExamPrep.Web.csproj src/ExamPrep.Web/
RUN dotnet restore src/ExamPrep.Web/ExamPrep.Web.csproj
COPY src/ src/
RUN dotnet publish src/ExamPrep.Web/ExamPrep.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS aspnet

FROM mcr.microsoft.com/mssql/server:2022-latest
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 python3-pip python3-venv ca-certificates \
 && python3 -m venv /opt/hf && /opt/hf/bin/pip install --no-cache-dir "huggingface_hub[cli]==2.2.0" \
 && rm -rf /var/lib/apt/lists/*
COPY --from=aspnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet

# Hugging Face 以 UID 1000 執行容器
RUN useradd -m -u 1000 user \
 && mkdir -p /var/opt/mssql /home/user/backup \
 && chown -R 1000:1000 /var/opt/mssql /home/user
COPY --from=build --chown=1000:1000 /app /home/user/app
COPY --chown=1000:1000 deploy/hf/start.sh /home/user/start.sh
RUN chmod +x /home/user/start.sh

USER user
ENV HOME=/home/user \
    PATH=/opt/hf/bin:/opt/mssql-tools18/bin:$PATH \
    ACCEPT_EULA=Y \
    MSSQL_PID=Express \
    ASPNETCORE_URLS=http://0.0.0.0:7860 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Site__PublicMode=true \
    Site__BehindProxy=true \
    Site__FrameAncestors__0=https://huggingface.co \
    BACKUP_INTERVAL_MIN=15
WORKDIR /home/user/app
EXPOSE 7860
CMD ["/home/user/start.sh"]
