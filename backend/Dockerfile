##
## AOI Ops Platform backend (ASP.NET Core 8)
##
## 為什麼需要 Dockerfile（不再用 dotnet run + volume mount）：
## - 目前在 `mcr.microsoft.com/dotnet/sdk` 容器內用 `dotnet run` 會出現「host 卡住、Kestrel 不 listen」的假死狀態，
##   但 background worker（Kafka/RabbitMQ）仍會輸出 log，造成 API 全掛且沒有 500 回應。
## - 改成標準 publish → runtime 執行，可讓啟動行為與企業部署一致，也能把除錯範圍縮到「程式本身」而非 dev tooling。
##

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先拷貝 csproj 以利用 docker layer cache
COPY backend/src/Domain/AOIOpsPlatform.Domain.csproj backend/src/Domain/
COPY backend/src/Application/AOIOpsPlatform.Application.csproj backend/src/Application/
COPY backend/src/Infrastructure/AOIOpsPlatform.Infrastructure.csproj backend/src/Infrastructure/
COPY backend/src/Api/AOIOpsPlatform.Api.csproj backend/src/Api/

RUN dotnet restore backend/src/Api/AOIOpsPlatform.Api.csproj

# 再拷貝其餘原始碼
COPY backend/ backend/
COPY shared/ shared/

# 為什麼不再用 --no-restore：
# - 本專案會隨著開發新增/調整 NuGet 套件（例如加入 SqlServer provider），
#   在某些 Docker build cache/restore layer 的狀態下，publish 階段可能出現「已 restore 但 publish 找不到套件」
#   的 NETSDK1064（常見於 analyzers 套件）。
# - 這裡改成在 publish 讓 dotnet 自行確保 restore 完整，換取可重現性與更穩定的容器建置。
RUN dotnet publish backend/src/Api/AOIOpsPlatform.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# 為什麼加 curl：
# - docker-compose 的 backend healthcheck 需要用 HTTP probe；
# - aspnet runtime image 預設不含 curl/wget，容易造成「服務其實正常但 health 永遠 fail」。
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# 為什麼要明確設定 URLs：
# - 讓容器對外提供 HTTP（配合 compose 8080:8080）
ENV ASPNETCORE_URLS=http://+:8080

# Domain profile 預設路徑（容器內）
ENV Domain__ProfilesDirectory=/app/shared/domain-profiles

COPY --from=build /app/publish .
COPY --from=build /src/shared /app/shared

ENTRYPOINT ["dotnet", "AOIOpsPlatform.Api.dll"]

