# 為什麼用 python:3.12-slim：
# - slim 版本比 full 版本小很多（沒有編譯工具），對 FastAPI + numpy 已足夠。
# - 3.12 是目前最新穩定版，有較好的效能改善。
FROM python:3.12-slim

WORKDIR /app

# 先複製 requirements 單獨安裝，利用 Docker 快取層（只要 requirements 不變，pip install 就不重跑）
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# 再複製應用程式碼
COPY app/ ./app/

# 為什麼用 uvicorn + 0.0.0.0：
# - 容器內預設只綁 127.0.0.1，外部（host）無法存取；綁 0.0.0.0 才能讓 docker port mapping 生效。
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001", "--reload"]
