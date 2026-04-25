"""
SPC Demo 資料產生器

為什麼需要 Demo 資料：
- 前端展示時不一定有真實製程數據，需要可重現的假資料讓圖表能直接展示。
- Demo 資料刻意加入已知的違規點（如趨勢、偏移），讓前端能展示八大規則的高亮效果。
- random seed 固定，保證每次 GET /api/spc/demo/* 結果一致，方便截圖與 demo。

解決什麼問題：
- 新手或 PM demo 時不需要先準備 CSV 或寫 POST body，直接 GET 就有漂亮圖表。
"""

from __future__ import annotations
import math
import random


def _seeded_rng(seed: int = 42) -> random.Random:
    """固定 seed 的隨機產生器，讓 demo 資料可重現"""
    return random.Random(seed)


def gen_xbar_r_demo() -> dict:
    """
    產生 Xbar-R 圖 Demo 資料。

    資料情境：
    - 前 15 組：製程穩定，在規格內正常波動
    - 第 16–22 組：製程向上漂移（模擬規則 3 趨勢）
    - 第 23 組：超出上管制線（模擬規則 1 超限）
    - 後 7 組：製程回歸穩定

    規格設定：USL=105, LSL=95, Target=100
    """
    rng = _seeded_rng(42)
    subgroup_size = 5
    usl, lsl, target = 105.0, 95.0, 100.0

    values: list[float] = []

    # 前 15 組穩定（μ=100, σ=1）
    for _ in range(15):
        for _ in range(subgroup_size):
            values.append(round(100 + rng.gauss(0, 1), 3))

    # 第 16–22 組：均值逐步上升（+0.3/組，模擬漂移）
    drift = 0
    for _ in range(7):
        drift += 0.4
        for _ in range(subgroup_size):
            values.append(round(100 + drift + rng.gauss(0, 1), 3))

    # 第 23 組：異常高值（超出 UCL）
    for _ in range(subgroup_size):
        values.append(round(104.5 + rng.gauss(0, 0.3), 3))

    # 後 7 組：回歸穩定
    for _ in range(7):
        for _ in range(subgroup_size):
            values.append(round(100 + rng.gauss(0, 1), 3))

    return {
        "subgroup_size": subgroup_size,
        "values": values,
        "usl": usl,
        "lsl": lsl,
        "target": target,
    }


def gen_imr_demo() -> dict:
    """
    產生 I-MR 圖 Demo 資料。

    資料情境：
    - 前 20 點：穩定（μ=50, σ=2）
    - 第 21–27 點：連 7 點在均值上側（模擬規則 2）
    - 第 28 點：超出上管制線（模擬規則 1）
    - 後 12 點：回穩

    規格設定：USL=58, LSL=42, Target=50
    """
    rng = _seeded_rng(101)
    usl, lsl, target = 58.0, 42.0, 50.0

    values: list[float] = []

    # 前 20 點穩定
    for _ in range(20):
        values.append(round(50 + rng.gauss(0, 2), 3))

    # 第 21–27 點：連 7 點偏高（均值 = 54，仍在管制線內）
    for _ in range(7):
        values.append(round(54 + rng.gauss(0, 1), 3))

    # 第 28 點：超限
    values.append(round(58.5 + rng.gauss(0, 0.2), 3))

    # 後 12 點回穩
    for _ in range(12):
        values.append(round(50 + rng.gauss(0, 2), 3))

    return {
        "values": values,
        "usl": usl,
        "lsl": lsl,
        "target": target,
    }


def gen_p_chart_demo() -> dict:
    """
    產生 P 圖 Demo 資料。

    資料情境：
    - 共 25 批次，固定樣本大小 200
    - 前 15 批次：不良率穩定約 0.03（3%）
    - 第 16–20 批次：不良率上升到約 0.08（模擬製程惡化）
    - 第 21 批次：超限（不良率約 12%）
    - 後 4 批次：回穩

    適用場景：PCB 板面外觀不良率監控
    """
    rng = _seeded_rng(77)
    sample_size = 200

    defective_counts: list[int] = []

    for _ in range(15):
        p = 0.03 + rng.gauss(0, 0.005)
        defective_counts.append(max(0, round(sample_size * p)))

    for _ in range(5):
        p = 0.06 + rng.uniform(0, 0.03)
        defective_counts.append(max(0, round(sample_size * p)))

    # 超限點
    defective_counts.append(round(sample_size * 0.13))

    for _ in range(4):
        p = 0.03 + rng.gauss(0, 0.005)
        defective_counts.append(max(0, round(sample_size * p)))

    return {
        "defective_counts": defective_counts,
        "sample_sizes": sample_size,
    }


def gen_c_chart_demo() -> dict:
    """
    產生 C 圖 Demo 資料。

    資料情境：
    - 共 25 個樣本（每個晶圓片）
    - 前 18 個：穩定，平均 4 個缺陷
    - 第 19–23 個：連 5 點偏高（模擬規則 6，4/5 超±1σ）
    - 第 24 個：超出上管制線（規則 1）

    適用場景：晶圓表面缺陷數監控
    """
    rng = _seeded_rng(55)
    import statistics

    defect_counts: list[int] = []

    for _ in range(18):
        # 泊松分布模擬（λ=4）
        count = sum(1 for _ in range(1000) if rng.random() < 0.004)
        defect_counts.append(max(0, count))

    # 偏高段
    for _ in range(5):
        count = sum(1 for _ in range(1000) if rng.random() < 0.007)
        defect_counts.append(max(0, count))

    # 超限
    defect_counts.append(18)

    # 補足到 25
    for _ in range(1):
        count = sum(1 for _ in range(1000) if rng.random() < 0.004)
        defect_counts.append(max(0, count))

    return {
        "defect_counts": defect_counts,
    }


def gen_capability_demo() -> dict:
    """
    產生製程能力指數 Demo 資料。

    模擬一個 Cpk 約 1.2（B 級）的製程，
    均值略偏離中心（Ca > 0），讓 Cpk < Cp。
    """
    rng = _seeded_rng(33)
    usl, lsl, target = 10.5, 9.5, 10.0

    # μ=10.1（偏右），σ=0.15
    values = [round(10.1 + rng.gauss(0, 0.15), 4) for _ in range(50)]

    return {
        "values": values,
        "usl": usl,
        "lsl": lsl,
        "target": target,
    }
