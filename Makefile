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
DOMAIN_PROFILE ?= pcb
SIM_SCENARIO ?= normal

export DOMAIN_PROFILE
export SIM_SCENARIO

.PHONY: up down restart seed smoke logs ps profile-pcb profile-semiconductor scenario-normal scenario-drift scenario-spike scenario-misjudge help

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
	@echo ">>   postgres       : localhost:5432         (postgres/postgres)"

down:  ## 關掉所有容器（保留 volume）
	$(COMPOSE) down

restart:  ## 完全 down + up（不清 volume）
	$(COMPOSE) down
	$(COMPOSE) up -d

seed:  ## 砍掉 volume 重新跑 init script，重建一份乾淨的 DB
	$(COMPOSE) down
	docker volume rm aoiops_aoiops_postgres_data 2>/dev/null || true
	$(COMPOSE) up -d db
	@echo ">> 等待 postgres healthcheck 通過..."
	@for i in $$(seq 1 30); do \
	  status=$$(docker inspect --format '{{.State.Health.Status}}' aoiops-db-1 2>/dev/null || echo missing); \
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
