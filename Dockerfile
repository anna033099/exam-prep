# 一般用途映像：只有網站，資料庫用外部 SQL Server（以環境變數 ConnectionStrings__Default 設定）。
# Hugging Face Spaces 請用 deploy/hf/Dockerfile（內含 SQL Server 與自動備份）。
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ExamPrep.Web/ExamPrep.Web.csproj src/ExamPrep.Web/
RUN dotnet restore src/ExamPrep.Web/ExamPrep.Web.csproj
COPY src/ src/
RUN dotnet publish src/ExamPrep.Web/ExamPrep.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    Site__PublicMode=true \
    Site__BehindProxy=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ExamPrep.Web.dll"]
