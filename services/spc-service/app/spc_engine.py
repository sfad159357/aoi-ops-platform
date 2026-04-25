"""
SPC 核心計算引擎

為什麼把計算邏輯獨立出來：
- 讓 API 層（main.py）只負責「接收請求 / 回傳結果」，不混入統計邏輯。
- 方便單元測試（不需要啟動 FastAPI 就能測計算正確性）。
- 未來要換成 Pandas / SciPy 加速，也只需改這一個檔案。

解決什麼問題：
- 計量型圖表（Xbar-R、I-MR）需要正確的控制常數（A2、D3、D4、d2）。
- Ca/Cp/Cpk 需要準確的公式，特別是 Ca 的分子定義（目標值 vs 規格中心）。
- 以上邏輯都有容易搞錯的細節，集中一個地方讓 code review 更聚焦。
"""

from __future__ import annotations
import math
import numpy as np
from .models import (
    ControlLimits, ChartPoint, ProcessCapability,
    XbarRInput, IMRInput, XbarRResult, IMRResult,
)

# ─── SPC 控制常數表（依子群大小 n = 2..10）────────────────────────────────
# 來源：AIAG SPC 手冊第 4 版 Appendix VI
# A2: 用於計算 Xbar 管制線（UCL_X = X̄bar + A2 * R̄）
# D3: 用於 R 圖下管制線（n < 7 時 LCL_R = 0）
# D4: 用於 R 圖上管制線
# d2: 用於從 R̄ 估計製程標準差（σ̂ = R̄ / d2）
XBAR_R_CONSTANTS: dict[int, dict[str, float]] = {
    2:  {"A2": 1.880, "D3": 0.000, "D4": 3.267, "d2": 1.128},
    3:  {"A2": 1.023, "D3": 0.000, "D4": 2.575, "d2": 1.693},
    4:  {"A2": 0.729, "D3": 0.000, "D4": 2.282, "d2": 2.059},
    5:  {"A2": 0.577, "D3": 0.000, "D4": 2.115, "d2": 2.326},
    6:  {"A2": 0.483, "D3": 0.000, "D4": 2.004, "d2": 2.534},
    7:  {"A2": 0.419, "D3": 0.076, "D4": 1.924, "d2": 2.704},
    8:  {"A2": 0.373, "D3": 0.136, "D4": 1.864, "d2": 2.847},
    9:  {"A2": 0.337, "D3": 0.184, "D4": 1.816, "d2": 2.970},
    10: {"A2": 0.308, "D3": 0.223, "D4": 1.777, "d2": 3.078},
}

# I-MR 圖固定常數（子群大小 = 2，即相鄰兩點移動全距）
IMR_D4 = 3.267
IMR_D3 = 0.000
IMR_D2 = 1.128   # d2 for n=2


# ─── 製程能力指數計算 ────────────────────────────────────────────────────

def calculate_capability(
    values: list[float],
    usl: float,
    lsl: float,
    target: float | None = None,
    sigma: float | None = None,
) -> ProcessCapability:
    """
    計算 Ca、Cp、Cpk 三大製程能力指數。

    為什麼需要這個函式：
    - Ca、Cp、Cpk 是 SPC 最核心的 KPI，用來回答「製程夠不夠好」。
    - sigma 可由外部傳入（如 Xbar-R 圖的 R̄/d2 估計值），若未傳則使用樣本標準差。
    - 這樣設計讓 Xbar-R 和 I-MR 都能重用同一套能力計算邏輯。

    製程等級對照（業界慣例 Cpk）：
    - A+ : Cpk ≥ 1.67
    - A  : 1.33 ≤ Cpk < 1.67
    - B  : 1.00 ≤ Cpk < 1.33
    - C  : 0.67 ≤ Cpk < 1.00
    - D  : Cpk < 0.67
    """
    arr = np.array(values, dtype=float)
    mean = float(arr.mean())
    std = sigma if sigma is not None else float(arr.std(ddof=1))

    if std == 0:
        # 標準差為 0 時，能力無限大（實務上不可能，但防呆）
        std = 1e-9

    spec_center = (usl + lsl) / 2
    half_width = (usl - lsl) / 2

    # Ca：衡量製程平均值偏離目標（或規格中心）的程度
    # |mean - target| / half_width
    t = target if target is not None else spec_center
    ca = (mean - spec_center) / half_width if half_width != 0 else 0.0

    # Cp：衡量規格寬度 vs 製程變異（6σ）的比例
    cp = (usl - lsl) / (6 * std)

    # Cpk：考慮偏移後的綜合能力（越接近 Cp 代表製程越對中）
    cpu = (usl - mean) / (3 * std)
    cpl = (mean - lsl) / (3 * std)
    cpk = min(cpu, cpl)

    # 製程等級判定
    if cpk >= 1.67:
        grade = "A+"
    elif cpk >= 1.33:
        grade = "A"
    elif cpk >= 1.00:
        grade = "B"
    elif cpk >= 0.67:
        grade = "C"
    else:
        grade = "D"

    return ProcessCapability(
        ca=round(ca, 4),
        cp=round(cp, 4),
        cpk=round(cpk, 4),
        cpu=round(cpu, 4),
        cpl=round(cpl, 4),
        mean=round(mean, 6),
        std=round(std, 6),
        usl=usl,
        lsl=lsl,
        target=t if target is not None else None,
        grade=grade,
    )


# ─── Xbar-R 圖計算 ──────────────────────────────────────────────────────

def calc_xbar_r(inp: XbarRInput) -> XbarRResult:
    """
    計算 Xbar-R 管制圖的各統計量與管制線。

    為什麼選 Xbar-R：
    - 最常見的計量型管制圖，適合子群大小 2–10。
    - 用 R̄/d2 估計製程標準差，比直接用樣本 std 更不易受短期波動影響。

    演算法：
    1. 切分子群（每 n 個為一組）
    2. 計算各子群平均（Xbar）與全距（R）
    3. 計算總平均 X̄bar 與平均全距 R̄
    4. 套用 A2/D3/D4 常數求管制線
    5. 估計 σ = R̄ / d2 → 用於八大規則 zone 判斷 & 製程能力
    """
    from .rules import detect_rules

    n = inp.subgroup_size
    consts = XBAR_R_CONSTANTS[n]
    A2, D3, D4, d2 = consts["A2"], consts["D3"], consts["D4"], consts["d2"]

    values = inp.values
    num_subgroups = len(values) // n
    if num_subgroups < 5:
        raise ValueError("子群數量不足（至少需要 5 組）")

    # 切成子群矩陣
    subgroups = [values[i * n:(i + 1) * n] for i in range(num_subgroups)]

    xbars = [float(np.mean(sg)) for sg in subgroups]
    ranges = [float(np.max(sg) - np.min(sg)) for sg in subgroups]

    grand_mean = float(np.mean(xbars))
    r_bar = float(np.mean(ranges))

    # 估計製程標準差（用於 zone 判斷）
    sigma_x = r_bar / d2 / math.sqrt(n)   # Xbar 圖的 σ（= σ_process / √n）
    sigma_process = r_bar / d2             # 製程本身的 σ（用於能力計算）

    # Xbar 管制線
    ucl_x = grand_mean + A2 * r_bar
    lcl_x = grand_mean - A2 * r_bar

    # R 管制線
    ucl_r = D4 * r_bar
    lcl_r = D3 * r_bar

    xbar_limits = ControlLimits(
        ucl=round(ucl_x, 6),
        cl=round(grand_mean, 6),
        lcl=round(lcl_x, 6),
        sigma1=round(grand_mean + sigma_x, 6),
        sigma2=round(grand_mean + 2 * sigma_x, 6),
    )
    r_limits = ControlLimits(
        ucl=round(ucl_r, 6),
        cl=round(r_bar, 6),
        lcl=round(lcl_r, 6),
    )

    # 八大規則偵測（僅對 Xbar 序列做，R 圖通常只看 Rule 1）
    xbar_violations = detect_rules(xbars, grand_mean, sigma_x)
    r_violations = detect_rules(ranges, r_bar, r_bar / d2, rules=[1])  # R 圖只看超限

    all_violations = xbar_violations + r_violations
    # 去重（相同 rule_id 的違規合併 points）
    all_violations = _merge_violations(all_violations)

    # 標記各點違規
    xbar_points = _tag_points(xbars, xbar_violations)
    r_points = _tag_points(ranges, r_violations)

    # 製程能力（需要規格限）
    cap = None
    if inp.usl is not None and inp.lsl is not None:
        cap = calculate_capability(
            values, inp.usl, inp.lsl, inp.target, sigma=sigma_process
        )

    return XbarRResult(
        xbar_points=xbar_points,
        r_points=r_points,
        xbar_limits=xbar_limits,
        r_limits=r_limits,
        violations=all_violations,
        capability=cap,
        subgroup_size=n,
        total_points=num_subgroups,
    )


# ─── I-MR 圖計算 ────────────────────────────────────────────────────────

def calc_imr(inp: IMRInput) -> IMRResult:
    """
    計算 I-MR 管制圖（Individual + Moving Range）。

    為什麼選 I-MR：
    - 適合子群大小 = 1 的情況（每次只取一個量測值）。
    - 移動全距（MR）用相鄰兩點差距估計製程變異，不需要子群。
    - 半導體製程中許多參數（如薄膜厚度批次均值）常用 I-MR。

    演算法：
    1. 計算每相鄰兩點的移動全距 MR_i = |X_i - X_{i-1}|
    2. 計算 X̄ 與 MR̄
    3. 估計 σ̂ = MR̄ / d2（d2 = 1.128 for n=2 移動全距）
    4. 計算 X 與 MR 的管制線
    """
    from .rules import detect_rules

    values = inp.values
    n = len(values)

    x_mean = float(np.mean(values))
    moving_ranges = [abs(values[i] - values[i - 1]) for i in range(1, n)]
    mr_bar = float(np.mean(moving_ranges))

    sigma_est = mr_bar / IMR_D2

    # X 圖管制線
    ucl_x = x_mean + 3 * sigma_est
    lcl_x = x_mean - 3 * sigma_est

    # MR 圖管制線
    ucl_mr = IMR_D4 * mr_bar
    lcl_mr = IMR_D3 * mr_bar  # = 0

    x_limits = ControlLimits(
        ucl=round(ucl_x, 6),
        cl=round(x_mean, 6),
        lcl=round(lcl_x, 6),
        sigma1=round(x_mean + sigma_est, 6),
        sigma2=round(x_mean + 2 * sigma_est, 6),
    )
    mr_limits = ControlLimits(
        ucl=round(ucl_mr, 6),
        cl=round(mr_bar, 6),
        lcl=round(lcl_mr, 6),
    )

    x_violations = detect_rules(list(values), x_mean, sigma_est)
    mr_violations = detect_rules(moving_ranges, mr_bar, mr_bar / IMR_D2, rules=[1])

    all_violations = _merge_violations(x_violations + mr_violations)

    x_points = _tag_points(list(values), x_violations)
    mr_points = _tag_points(moving_ranges, mr_violations)

    cap = None
    if inp.usl is not None and inp.lsl is not None:
        cap = calculate_capability(
            list(values), inp.usl, inp.lsl, inp.target, sigma=sigma_est
        )

    return IMRResult(
        x_points=x_points,
        mr_points=mr_points,
        x_limits=x_limits,
        mr_limits=mr_limits,
        violations=all_violations,
        capability=cap,
        total_points=n,
    )


# ─── 計數型圖表計算 ──────────────────────────────────────────────────────

def calc_p_chart(
    defective_counts: list[int],
    sample_sizes: list[int] | int,
) -> tuple[list[float], ControlLimits]:
    """
    P 圖：不良品比例管制圖。

    為什麼用 P 圖：
    - 適合「計數型資料 + 可變樣本大小」的情況。
    - 例如：每批次抽驗數量不固定，但要監控不良率是否穩定。

    管制線計算：
    - p̄ = Σ(defective_counts) / Σ(sample_sizes)
    - UCL_i = p̄ + 3 * √(p̄(1-p̄)/n_i)  （每組管制線可能不同）
    - 這裡回傳平均管制線（用平均 n 計算，前端展示較簡單）
    """
    n_arr = (
        np.array(sample_sizes, dtype=float)
        if isinstance(sample_sizes, list)
        else np.full(len(defective_counts), sample_sizes, dtype=float)
    )
    d_arr = np.array(defective_counts, dtype=float)
    p_arr = d_arr / n_arr

    p_bar = float(d_arr.sum() / n_arr.sum())
    n_avg = float(n_arr.mean())
    sigma_p = math.sqrt(p_bar * (1 - p_bar) / n_avg)

    ucl = min(1.0, p_bar + 3 * sigma_p)
    lcl = max(0.0, p_bar - 3 * sigma_p)

    limits = ControlLimits(
        ucl=round(ucl, 6),
        cl=round(p_bar, 6),
        lcl=round(lcl, 6),
        sigma1=round(p_bar + sigma_p, 6),
        sigma2=round(p_bar + 2 * sigma_p, 6),
    )
    return list(p_arr.round(6)), limits


def calc_np_chart(
    defective_counts: list[int],
    sample_size: int,
) -> tuple[list[int], ControlLimits]:
    """
    Np 圖：不良品絕對數管制圖（固定樣本大小）。

    與 P 圖的差異：
    - P 圖畫的是「比例」；Np 圖畫的是「數量」，更直覺。
    - 必須固定樣本大小，否則要改用 P 圖。
    """
    n = sample_size
    d_arr = np.array(defective_counts, dtype=float)
    np_bar = float(d_arr.mean())
    p_bar = np_bar / n

    sigma_np = math.sqrt(n * p_bar * (1 - p_bar))
    ucl = np_bar + 3 * sigma_np
    lcl = max(0.0, np_bar - 3 * sigma_np)

    limits = ControlLimits(
        ucl=round(ucl, 6),
        cl=round(np_bar, 6),
        lcl=round(lcl, 6),
        sigma1=round(np_bar + sigma_np, 6),
        sigma2=round(np_bar + 2 * sigma_np, 6),
    )
    return list(d_arr.astype(int)), limits


def calc_c_chart(defect_counts: list[int]) -> tuple[list[int], ControlLimits]:
    """
    C 圖：單位缺陷數管制圖（固定樣本大小）。

    適用情境：
    - 每個樣本都是相同面積 / 相同長度（如晶圓片缺陷數）。
    - 利用泊松分布假設：μ = c̄，σ = √c̄。
    """
    c_arr = np.array(defect_counts, dtype=float)
    c_bar = float(c_arr.mean())
    sigma_c = math.sqrt(c_bar)

    ucl = c_bar + 3 * sigma_c
    lcl = max(0.0, c_bar - 3 * sigma_c)

    limits = ControlLimits(
        ucl=round(ucl, 6),
        cl=round(c_bar, 6),
        lcl=round(lcl, 6),
        sigma1=round(c_bar + sigma_c, 6),
        sigma2=round(c_bar + 2 * sigma_c, 6),
    )
    return list(c_arr.astype(int)), limits


def calc_u_chart(
    defect_counts: list[int],
    sample_sizes: list[int] | int,
) -> tuple[list[float], ControlLimits]:
    """
    U 圖：每單位缺陷數管制圖（樣本大小可變）。

    與 C 圖的差異：
    - C 圖固定樣本大小；U 圖允許各組樣本大小不同。
    - u_i = c_i / n_i（每單位缺陷數）
    - ū = Σc_i / Σn_i
    - UCL_i = ū + 3 * √(ū/n_i)
    """
    n_arr = (
        np.array(sample_sizes, dtype=float)
        if isinstance(sample_sizes, list)
        else np.full(len(defect_counts), sample_sizes, dtype=float)
    )
    c_arr = np.array(defect_counts, dtype=float)
    u_arr = c_arr / n_arr
    u_bar = float(c_arr.sum() / n_arr.sum())
    n_avg = float(n_arr.mean())

    sigma_u = math.sqrt(u_bar / n_avg)
    ucl = u_bar + 3 * sigma_u
    lcl = max(0.0, u_bar - 3 * sigma_u)

    limits = ControlLimits(
        ucl=round(ucl, 6),
        cl=round(u_bar, 6),
        lcl=round(lcl, 6),
        sigma1=round(u_bar + sigma_u, 6),
        sigma2=round(u_bar + 2 * sigma_u, 6),
    )
    return list(u_arr.round(6)), limits


# ─── 內部工具函式 ────────────────────────────────────────────────────────

def _tag_points(values: list[float], violations: list) -> list[ChartPoint]:
    """
    把違規資訊對應回各資料點，讓前端可以依違規規則上色。
    """
    # 建立「點 → 違規規則清單」的 dict
    point_rules: dict[int, list[int]] = {}
    for v in violations:
        for pt in v.points:
            point_rules.setdefault(pt, []).append(v.rule_id)

    return [
        ChartPoint(
            index=i,
            value=round(val, 6),
            violation_rules=point_rules.get(i, []),
        )
        for i, val in enumerate(values)
    ]


def _merge_violations(violations: list) -> list:
    """
    合併相同 rule_id 的違規記錄（不同函式回傳的清單可能重複）。
    """
    merged: dict[int, object] = {}
    for v in violations:
        if v.rule_id not in merged:
            merged[v.rule_id] = v
        else:
            # 合併 points
            existing = merged[v.rule_id]
            combined = list(set(existing.points + v.points))
            merged[v.rule_id] = existing.model_copy(update={"points": sorted(combined)})
    return list(merged.values())
