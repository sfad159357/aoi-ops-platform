"""
SPC Service FastAPI 入口

為什麼選 FastAPI：
- 自動產生 OpenAPI 文件（/docs），前端工程師不用猜 API 格式。
- Pydantic 輸入驗證讓錯誤訊息清楚，新手排查 400 問題更快。
- 非同步原生支援，之後要接 PostgreSQL async driver 或長計算任務都方便。

解決什麼問題：
- C# 後端是主體，Python 服務負責「統計計算密集」的工作（SPC 計算）。
- 拆成獨立微服務讓兩邊都能獨立部署 / 擴展，不需要在 .NET 裡引入 numpy。

Port：8001（避免與 C# 後端的 8080 衝突）
"""

from __future__ import annotations

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from .models import (
    XbarRInput, IMRInput, PChartInput, NpChartInput, CChartInput, UChartInput,
    CapabilityInput, XbarRResult, IMRResult, AttributeChartResult,
    ProcessCapability, DemoDataResponse, ChartPoint,
)
from .spc_engine import (
    calc_xbar_r, calc_imr, calc_p_chart, calc_np_chart,
    calc_c_chart, calc_u_chart, calculate_capability,
)
from .rules import detect_rules
from .demo_data import (
    gen_xbar_r_demo, gen_imr_demo, gen_p_chart_demo,
    gen_c_chart_demo, gen_capability_demo,
)

# ─── App 建立 ────────────────────────────────────────────────────────────
app = FastAPI(
    title="AOI Ops — SPC Service",
    description=(
        "統計製程管制（SPC）計算服務。\n\n"
        "提供計量型（Xbar-R, I-MR）與計數型（P, Np, C, U）管制圖計算，\n"
        "以及製程能力指數（Ca, Cp, Cpk）與八大規則偵測。\n\n"
        "**Demo 端點**：`GET /api/spc/demo/{chart_type}` 不需傳資料即可展示。"
    ),
    version="1.0.0",
)

# ─── CORS 設定 ───────────────────────────────────────────────────────────
# 為什麼要 CORS：React 前端（localhost:5173）打這個 Python 服務（localhost:8001）
# 會被瀏覽器同源政策擋住；FastAPI 加這個 middleware 就能開放指定來源。
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],   # MVP 開發用；正式環境應改成前端的具體網域
    allow_methods=["*"],
    allow_headers=["*"],
)


# ─── 健康檢查 ─────────────────────────────────────────────────────────────

@app.get("/api/spc/health", tags=["Health"])
def health() -> dict:
    """確認 SPC Service 是否存活"""
    return {"status": "ok", "service": "spc-service"}


# ─── 計量型管制圖 ────────────────────────────────────────────────────────

@app.post("/api/spc/xbar-r", response_model=XbarRResult, tags=["計量型圖表"])
def xbar_r_chart(inp: XbarRInput) -> XbarRResult:
    """
    Xbar-R 管制圖分析。

    輸入多組量測值（子群大小 2–10），回傳：
    - 各子群平均（Xbar）與全距（R）的資料點
    - 管制線（UCL/CL/LCL 以及 ±1σ、±2σ 帶）
    - 八大規則違規清單
    - 製程能力指數（若有傳入 USL/LSL）

    **典型應用**：連續型製程的尺寸、重量、電阻值監控
    """
    try:
        return calc_xbar_r(inp)
    except ValueError as e:
        raise HTTPException(status_code=422, detail=str(e)) from e


@app.post("/api/spc/imr", response_model=IMRResult, tags=["計量型圖表"])
def imr_chart(inp: IMRInput) -> IMRResult:
    """
    I-MR 管制圖分析（Individual + Moving Range）。

    適用於子群大小 = 1 的情況（每次只取一個量測值）。

    **典型應用**：批次製程的薄膜厚度、蝕刻速率等低頻量測
    """
    try:
        return calc_imr(inp)
    except ValueError as e:
        raise HTTPException(status_code=422, detail=str(e)) from e


# ─── 計數型管制圖 ────────────────────────────────────────────────────────

@app.post("/api/spc/p-chart", response_model=AttributeChartResult, tags=["計數型圖表"])
def p_chart(inp: PChartInput) -> AttributeChartResult:
    """
    P 圖（不良品比例管制圖）。

    **典型應用**：AOI 外觀不良率、良率監控（樣本大小可變）
    """
    from .rules import detect_rules as _dr
    sizes = inp.sample_sizes if isinstance(inp.sample_sizes, list) else [inp.sample_sizes] * len(inp.defective_counts)
    p_vals, limits = calc_p_chart(inp.defective_counts, inp.sample_sizes)
    violations = _dr(p_vals, limits.cl, (limits.ucl - limits.cl) / 3)
    pts = [ChartPoint(index=i, value=v) for i, v in enumerate(p_vals)]
    return AttributeChartResult(chart_type="p", points=pts, limits=limits, violations=violations, total_points=len(pts))


@app.post("/api/spc/np-chart", response_model=AttributeChartResult, tags=["計數型圖表"])
def np_chart(inp: NpChartInput) -> AttributeChartResult:
    """
    Np 圖（不良品數量管制圖，固定樣本大小）。

    **典型應用**：固定抽驗數量的外觀不良品數監控
    """
    from .rules import detect_rules as _dr
    np_vals, limits = calc_np_chart(inp.defective_counts, inp.sample_size)
    sigma = (limits.ucl - limits.cl) / 3
    violations = _dr([float(v) for v in np_vals], limits.cl, sigma)
    pts = [ChartPoint(index=i, value=float(v)) for i, v in enumerate(np_vals)]
    return AttributeChartResult(chart_type="np", points=pts, limits=limits, violations=violations, total_points=len(pts))


@app.post("/api/spc/c-chart", response_model=AttributeChartResult, tags=["計數型圖表"])
def c_chart(inp: CChartInput) -> AttributeChartResult:
    """
    C 圖（單位缺陷數管制圖）。

    **典型應用**：晶圓表面缺陷數（每片晶圓的缺陷點數）監控
    """
    from .rules import detect_rules as _dr
    c_vals, limits = calc_c_chart(inp.defect_counts)
    sigma = (limits.ucl - limits.cl) / 3
    violations = _dr([float(v) for v in c_vals], limits.cl, sigma)
    pts = [ChartPoint(index=i, value=float(v)) for i, v in enumerate(c_vals)]
    return AttributeChartResult(chart_type="c", points=pts, limits=limits, violations=violations, total_points=len(pts))


@app.post("/api/spc/u-chart", response_model=AttributeChartResult, tags=["計數型圖表"])
def u_chart(inp: UChartInput) -> AttributeChartResult:
    """
    U 圖（每單位缺陷數管制圖，樣本大小可變）。

    **典型應用**：不同大小 PCB 板的每平方公分缺陷密度監控
    """
    from .rules import detect_rules as _dr
    u_vals, limits = calc_u_chart(inp.defect_counts, inp.sample_sizes)
    sigma = (limits.ucl - limits.cl) / 3
    violations = _dr(u_vals, limits.cl, sigma)
    pts = [ChartPoint(index=i, value=v) for i, v in enumerate(u_vals)]
    return AttributeChartResult(chart_type="u", points=pts, limits=limits, violations=violations, total_points=len(pts))


# ─── 製程能力指數 ────────────────────────────────────────────────────────

@app.post("/api/spc/capability", response_model=ProcessCapability, tags=["製程能力"])
def capability(inp: CapabilityInput) -> ProcessCapability:
    """
    計算製程能力指數（Ca、Cp、Cpk）。

    獨立端點，可在不畫管制圖的情況下快速計算能力指數。
    """
    return calculate_capability(inp.values, inp.usl, inp.lsl, inp.target)


# ─── Demo 端點（前端展示用）──────────────────────────────────────────────

DEMO_GENERATORS = {
    "xbar-r":     (gen_xbar_r_demo,    "Xbar-R 圖 Demo（5-樣品子群，含漂移與超限點）"),
    "imr":        (gen_imr_demo,       "I-MR 圖 Demo（單個量測值，含連7點偏高與超限）"),
    "p-chart":    (gen_p_chart_demo,   "P 圖 Demo（不良率監控，含惡化段與超限）"),
    "c-chart":    (gen_c_chart_demo,   "C 圖 Demo（晶圓缺陷數，含偏高段與超限）"),
    "capability": (gen_capability_demo, "製程能力 Demo（Cpk ≈ 1.2，B 級，均值略偏右）"),
}


@app.get(
    "/api/spc/demo/{chart_type}",
    response_model=DemoDataResponse,
    tags=["Demo"],
)
def demo_data(chart_type: str) -> DemoDataResponse:
    """
    取得各圖表的 Demo 輸入資料（不需提供真實製程數據）。

    可用的 chart_type：`xbar-r` | `imr` | `p-chart` | `c-chart` | `capability`

    前端可以：
    1. GET /api/spc/demo/xbar-r  → 拿到 payload
    2. POST /api/spc/xbar-r 帶著這個 payload → 拿到完整分析結果

    或直接用前端內建 demo（不發 request），見 React 元件的 DEMO_DATA 常數。
    """
    if chart_type not in DEMO_GENERATORS:
        raise HTTPException(
            status_code=404,
            detail=f"chart_type '{chart_type}' 不存在。可用值：{list(DEMO_GENERATORS.keys())}",
        )
    gen_fn, description = DEMO_GENERATORS[chart_type]
    return DemoDataResponse(
        chart_type=chart_type,
        description=description,
        data=gen_fn(),
    )


@app.get("/api/spc/demo", tags=["Demo"])
def demo_list() -> dict:
    """列出所有可用的 Demo 類型與說明"""
    return {
        "available": [
            {"chart_type": k, "description": v[1]}
            for k, v in DEMO_GENERATORS.items()
        ]
    }
