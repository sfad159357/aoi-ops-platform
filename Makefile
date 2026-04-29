# AOI Ops Platform — Make targets
#
# 為什麼要 Makefile：把常用 docker compose / smoke 指令封裝成 4 個動詞，
# 讓新成員「下載 repo 後 30 秒能起整套系統」，避免每個人記不同指令。
#
# 使用方式：
#   make up                        # 啟動所有容器（預設 PCB profile）
#   make up DOMAIN_PROFILE=semiconductor   # 切換成半導體 profile
#   make down                      # 關掉所有容器（保留 volume）
#   make seed                      # 重建 DB（清掉 volume → 觸發 init script）
#   make smoke                     # 打幾個關鍵 API，確認骨架活著

COMPOSE      := docker compose -p aoiops -f infra/docker/docker-compose.yml
COMPOSE_DEV  := docker compose -p aoiops -f infra/docker/docker-compose.yml -f infra/docker/docker-compose.dev.yml
DOMAIN_PROFILE ?= pcb
SIM_SCENARIO ?= normal

export DOMAIN_PROFILE
export SIM_SCENARIO

.PHONY: up down restart seed smoke logs ps profile-pcb profile-semiconductor scenario-normal scenario-drift scenario-spike scenario-misjudge mssql-up mssql-down mssql-shell backend-rebuild dev-up dev-down dev-restart dev-logs help

help:  ## 顯示可用指令
	@echo "AOI Ops Platform — make targets"
	@echo ""
	@echo "  make up [DOMAIN_PROFILE=pcb|semiconductor] [SIM_SCENARIO=normal|drift|spike|misjudge]"
	@echo "  make down"
	@echo "  make restart"
	@echo "  make seed         # 重建 DB（清空 volume）"
	@echo "  make smoke        # 對 backend 打幾個關鍵 API"
	@echo "  make logs         # 跟 backend log（Ctrl-C 退出）"
	@echo "  make ps           # 看容器狀態"
	@echo "  make backend-rebuild  # 只重建 backend（避免 API 404 舊版容器）"
	@echo ""
	@echo "  make dev-up       # dev 模式：backend 用 dotnet watch（改 C# 不用 rebuild）"
	@echo "  make dev-down"
	@echo "  make dev-restart"
	@echo "  make dev-logs     # dev 模式下跟 backend log"
	@echo ""
	@echo "  make profile-pcb           # 切到 PCB profile（重啟 backend / frontend）"
	@echo "  make profile-semiconductor # 切到半導體 profile"

up:  ## 啟動所有容器
	$(COMPOSE) up -d
	@echo ""
	@echo ">> AOI Ops Platform 已啟動（DOMAIN_PROFILE=$(DOMAIN_PROFILE), SIM_SCENARIO=$(SIM_SCENARIO))"
	@echo ">>   frontend       : http://localhost:5173"
	@echo ">>   backend API    : http://localhost:8080  (swagger: /swagger)"
	@echo ">>   spc-service    : http://localhost:8001  (FastAPI 批次/歷史)"
	@echo ">>   rabbitmq mgmt  : http://localhost:15672 (guest/guest)"
	@echo ">>   influxdb UI    : http://localhost:8086"
	@echo ">>   sqlserver      : localhost:1433         (sa/Your_password123!)"

down:  ## 關掉所有容器（保留 volume）
	$(COMPOSE) down

restart:  ## 完全 down + up（不清 volume）
	$(COMPOSE) down
	$(COMPOSE) up -d

seed:  ## 重建 SQL Server（清空 volume → 重新建 DB + schema + seed）
	@# 防呆：這個 target 會「刪掉 SQL Server volume」，等同把 AOIOpsPlatform_MSSQL 整個 DB 重置（資料全消失）。
	@# 為什麼要這樣做：用乾淨 DB 讓 demo/開發環境可重現，避免每次手動清表造成不一致。
	@# 如何執行：需要顯式確認才會真的刪資料，避免手滑（make seed CONFIRM=YES）。
	@if [ "$(CONFIRM)" != "YES" ]; then \
	  echo "!! DANGER: 這會刪除 SQL Server volume：aoiops_aoiops_mssql_data（資料庫與所有資料會消失）"; \
	  echo "!! 若你確定要重置 DB，請改用：make seed CONFIRM=YES"; \
	  exit 1; \
	fi
	$(COMPOSE) down
	docker volume rm aoiops_aoiops_mssql_data 2>/dev/null || true
	$(COMPOSE) up -d mssql mssql-init
	@echo ">> 等待 sqlserver healthcheck 通過..."
	@for i in $$(seq 1 30); do \
	  status=$$(docker inspect --format '{{.State.Health.Status}}' aoiops-mssql-1 2>/dev/null || echo missing); \
	  if [ "$$status" = "healthy" ]; then echo ">> DB healthy"; break; fi; \
	  echo "    ... ($$i) status=$$status"; sleep 2; \
	done
	$(COMPOSE) up -d
	@echo ">> 已完成 seed + 啟動全部服務"

smoke:  ## 打幾個關鍵 API，確認 backend / domain profile / 4 大模組活著
	@echo ">> health"
	@curl -fsS http://localhost:8080/api/health/db && echo
	@echo ">> meta/profile (前 200 chars)"
	@curl -fsS http://localhost:8080/api/meta/profile | head -c 200 && echo
	@echo ">> lots"
	@curl -fsS http://localhost:8080/api/lots | head -c 200 && echo
	@echo ">> alarms (最近 5 筆)"
	@curl -fsS "http://localhost:8080/api/alarms?take=5" | head -c 300 && echo
	@echo ">> workorders (最近 5 筆)"
	@curl -fsS "http://localhost:8080/api/workorders?take=5" | head -c 300 && echo
	@echo ">> trace/panels/recent"
	@curl -fsS "http://localhost:8080/api/trace/panels/recent?take=3" | head -c 300 && echo
	@echo ">> spc-service (FastAPI)"
	@curl -fsS http://localhost:8001/ | head -c 200 && echo
	@echo ">> metrics（即時推播 in-memory snapshot）"
	@curl -fsS http://localhost:8080/api/metrics | head -c 400 && echo
	@echo ">> smoke OK"

logs:  ## 跟 backend log
	$(COMPOSE) logs -f backend

ps:  ## 看容器狀態
	$(COMPOSE) ps

# ── Dev 模式（重點：backend 不需要一直 docker build）──
# 為什麼需要 dev-up：
# - 正式 compose 的 backend 是 publish 後跑 runtime image，改 C# 需要 rebuild image 才會生效。
# - dev compose 覆寫成 dotnet watch + 掛載原始碼，讓「改 code 立刻刷新」。
dev-up:  ## dev 模式啟動（backend=dotnet watch）
	$(COMPOSE_DEV) up -d

dev-down:  ## dev 模式關閉（保留 volume）
	$(COMPOSE_DEV) down

dev-restart:  ## dev 模式重啟（不清 volume）
	$(COMPOSE_DEV) down
	$(COMPOSE_DEV) up -d

dev-logs:  ## dev 模式跟 backend log
	$(COMPOSE_DEV) logs -f backend

profile-pcb:  ## 切換成 PCB profile（重啟 backend / frontend）
	DOMAIN_PROFILE=pcb $(COMPOSE) up -d --force-recreate backend frontend
	@echo ">> 已切換到 PCB profile"

profile-semiconductor:  ## 切換成半導體 profile（重啟 backend / frontend）
	DOMAIN_PROFILE=semiconductor $(COMPOSE) up -d --force-recreate backend frontend
	@echo ">> 已切換到半導體 profile"

# W11：SIM_SCENARIO 切換 — 用來在 demo 時驗證 SPC 違規型態。
# normal   : 正常波動（基本管制圖、低違規）
# drift    : 漂移（趨勢規則 Rule2/Rule3 觸發）
# spike    : 突波（Rule1 ±3σ 觸發）
# misjudge : 雜訊放大（Rule5/6 等樣本群觸發）
scenario-normal:
	SIM_SCENARIO=normal $(COMPOSE) up -d --force-recreate ingestion
scenario-drift:
	SIM_SCENARIO=drift $(COMPOSE) up -d --force-recreate ingestion
scenario-spike:
	SIM_SCENARIO=spike $(COMPOSE) up -d --force-recreate ingestion
scenario-misjudge:
	SIM_SCENARIO=misjudge $(COMPOSE) up -d --force-recreate ingestion

# ── SQL Server（Azure SQL Edge / ARM64 原生）便利 target ──
# 為什麼獨立成 mssql-up / mssql-down 而非塞進 up / down：
# - mssql 對既有 kafka / 業務服務「沒有任何相依」，是獨立的孤島，
#   因此給選用者單獨開關即可，避免每個人都被迫多吃一份 SQL Server 記憶體。
# - mssql-shell 用來快速進 sqlcmd 排查；若密碼有改，從環境變數 MSSQL_SA_PASSWORD 讀取，
#   保留預設 fallback 與 .env.example 一致，避免新成員忘了 export 而打不進去。
mssql-up:  ## 單獨啟動 SQL Server（azure-sql-edge）+ init sidecar 建空 DB
	$(COMPOSE) up -d mssql mssql-init
	@echo ""
	@echo ">> SQL Server (azure-sql-edge) 已啟動"
	@echo ">>   host         : localhost"
	@echo ">>   port         : 1433"
	@echo ">>   user / pass  : sa / $${MSSQL_SA_PASSWORD:-Your_password123!}"
	@echo ">>   default DB   : AOIOpsPlatform_MSSQL"
	@echo ">>   進 shell      : make mssql-shell"

mssql-down:  ## 停掉 SQL Server（保留 volume aoiops_mssql_data）
	$(COMPOSE) stop mssql mssql-init

mssql-shell:  ## 進 sqlcmd shell（用 SA 帳號）
	# 為什麼不用 docker exec 進主容器：azure-sql-edge:latest 已移除內建 sqlcmd，
	# 必須另外用 mcr.microsoft.com/mssql-tools image，並掛到 compose 網路 aoiops_default
	# 才連得到 mssql service（amd64 image 走 Rosetta，互動式輸入體感無感）。
	docker run -it --rm --network aoiops_default --platform linux/amd64 \
	  mcr.microsoft.com/mssql-tools:latest \
	  /opt/mssql-tools/bin/sqlcmd -S mssql -U sa -P "$${MSSQL_SA_PASSWORD:-Your_password123!}"

backend-rebuild:  ## 只重建 aoiops backend（更新新 API/DTO，避免 404）
	# 為什麼固定 -p aoiops：避免 compose project name 漂移，誤啟動第二套容器（常見 1433 port 衝突）。
	docker compose -p aoiops -f infra/docker/docker-compose.yml up -d --build --no-deps backend
