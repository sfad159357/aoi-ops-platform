"""
SPC 八大規則偵測模組（Western Electric Rules）

為什麼要有這個檔：
- 八大規則的邏輯各自獨立但容易互相干擾，拆成獨立函式更容易測試和維護。
- 每條規則有不同的嚴重程度（red/yellow/green），對應 MES 的告警優先級。
- 這裡的實作直接操作索引，不依賴外部資料框架，效能好也方便移植。

解決什麼問題：
- 管制圖光靠上下限不夠，需要「趨勢型」違規偵測才能提早發現製程偏移。
- 八大規則是業界標準（AIAG/Western Electric），讓系統輸出有公信力。

規則速覽：
  規則1：1點超±3σ     → 🔴 紅色（最高，立即處理）
  規則2：連7點同側     → 🟡 黃色（高）
  規則3：連6點趨勢     → 🟡 黃色（高）
  規則4：連14點鋸齒   → 🟢 綠色（中）
  規則5：3點2個超±2σ  → 🟡 黃色（高）
  規則6：5點4個超±1σ  → 🟢 綠色（中）
  規則7：連15點在±1σ內 → 🟢 綠色（低，異常穩定）
  規則8：連8點無±1σ內  → 🟢 綠色（中）
"""

from __future__ import annotations
from .models import RuleViolation

# 規則中文說明
RULE_DESCRIPTIONS = {
    1: "1 點超出 ±3σ（UCL/LCL）",
    2: "連續 7 點在中心線同一側",
    3: "連續 6 點持續上升或下降",
    4: "連續 14 點上下交替（鋸齒狀）",
    5: "連續 3 點中有 2 點在同側 ±2σ 外",
    6: "連續 5 點中有 4 點在同側 ±1σ 外",
    7: "連續 15 點都在 ±1σ 以內（異常穩定）",
    8: "連續 8 點中沒有任何點在 ±1σ 以內",
}

RULE_SEVERITY = {
    1: "red",
    2: "yellow",
    3: "yellow",
    4: "green",
    5: "yellow",
    6: "green",
    7: "green",
    8: "green",
}


def detect_rules(
    values: list[float],
    cl: float,
    sigma: float,
    rules: list[int] | None = None,
) -> list[RuleViolation]:
    """
    對一個數值序列執行 SPC 八大規則偵測。

    參數：
    - values: 資料點序列（Xbar 序列或 I 圖的個別值）
    - cl: 中心線（通常是 X̄bar 或 X̄）
    - sigma: 1σ 的大小（用於 ±1σ、±2σ、±3σ 的 zone 劃分）
    - rules: 要跑的規則清單，預設全部（None = 1–8）

    為什麼要允許傳入 sigma：
    - 計量型圖表用製程估計 σ（R̄/d2），不是樣本標準差。
    - 前者更能反映製程本身的穩定性，不受抽樣誤差影響。
    """
    active_rules = rules if rules is not None else list(range(1, 9))
    n = len(values)
    violations: list[RuleViolation] = []

    # 建立 zone 符號序列（方便後面的規則判斷）
    # zone[i] = (+/-)1, 2, 3 代表在中線哪一側的哪個 σ 帶
    # 0 = 中心線附近（±1σ 以內）
    sides = [1 if v >= cl else -1 for v in values]  # +1=上側, -1=下側

    def _zone(v: float) -> int:
        """回傳 v 所在 σ 帶（0=±1σ 內, 1=1-2σ, 2=2-3σ, 3=超出±3σ）"""
        diff = abs(v - cl)
        if diff <= sigma:
            return 0
        elif diff <= 2 * sigma:
            return 1
        elif diff <= 3 * sigma:
            return 2
        else:
            return 3

    zones = [_zone(v) for v in values]

    # ── 規則 1：1 點超出 ±3σ ──────────────────────────────────────────
    if 1 in active_rules:
        pts = [i for i, z in enumerate(zones) if z == 3]
        if pts:
            violations.append(RuleViolation(
                rule_id=1,
                rule_name=RULE_DESCRIPTIONS[1],
                points=pts,
                severity=RULE_SEVERITY[1],
            ))

    # ── 規則 2：連續 7 點在中心線同一側 ──────────────────────────────────
    if 2 in active_rules:
        pts = _consecutive_same_side(sides, run_length=7)
        if pts:
            violations.append(RuleViolation(
                rule_id=2,
                rule_name=RULE_DESCRIPTIONS[2],
                points=pts,
                severity=RULE_SEVERITY[2],
            ))

    # ── 規則 3：連續 6 點持續上升或下降 ────────────────────────────────
    if 3 in active_rules:
        pts = _consecutive_trend(values, run_length=6)
        if pts:
            violations.append(RuleViolation(
                rule_id=3,
                rule_name=RULE_DESCRIPTIONS[3],
                points=pts,
                severity=RULE_SEVERITY[3],
            ))

    # ── 規則 4：連續 14 點上下交替 ──────────────────────────────────────
    if 4 in active_rules:
        pts = _alternating(values, run_length=14)
        if pts:
            violations.append(RuleViolation(
                rule_id=4,
                rule_name=RULE_DESCRIPTIONS[4],
                points=pts,
                severity=RULE_SEVERITY[4],
            ))

    # ── 規則 5：連續 3 點中有 2 點在同側 ±2σ 外 ────────────────────────
    if 5 in active_rules:
        pts = _k_of_m_beyond_zone(values, cl, sigma, threshold=2, window=3, zone_min=2)
        if pts:
            violations.append(RuleViolation(
                rule_id=5,
                rule_name=RULE_DESCRIPTIONS[5],
                points=pts,
                severity=RULE_SEVERITY[5],
            ))

    # ── 規則 6：連續 5 點中有 4 點在同側 ±1σ 外 ────────────────────────
    if 6 in active_rules:
        pts = _k_of_m_beyond_zone(values, cl, sigma, threshold=4, window=5, zone_min=1)
        if pts:
            violations.append(RuleViolation(
                rule_id=6,
                rule_name=RULE_DESCRIPTIONS[6],
                points=pts,
                severity=RULE_SEVERITY[6],
            ))

    # ── 規則 7：連續 15 點都在 ±1σ 內 ──────────────────────────────────
    if 7 in active_rules:
        pts = _all_within_zone(zones, run_length=15, zone_max=0)
        if pts:
            violations.append(RuleViolation(
                rule_id=7,
                rule_name=RULE_DESCRIPTIONS[7],
                points=pts,
                severity=RULE_SEVERITY[7],
            ))

    # ── 規則 8：連續 8 點中沒有任何點在 ±1σ 內 ─────────────────────────
    if 8 in active_rules:
        pts = _none_within_zone(zones, run_length=8, zone_max=0)
        if pts:
            violations.append(RuleViolation(
                rule_id=8,
                rule_name=RULE_DESCRIPTIONS[8],
                points=pts,
                severity=RULE_SEVERITY[8],
            ))

    return violations


# ─── 各規則子函式 ────────────────────────────────────────────────────────

def _consecutive_same_side(sides: list[int], run_length: int) -> list[int]:
    """找出連續 run_length 個點都在同側的索引集合"""
    result: list[int] = []
    n = len(sides)
    for start in range(n - run_length + 1):
        window = sides[start:start + run_length]
        if all(s == 1 for s in window) or all(s == -1 for s in window):
            for i in range(start, start + run_length):
                if i not in result:
                    result.append(i)
    return result


def _consecutive_trend(values: list[float], run_length: int) -> list[int]:
    """
    找出連續 run_length 個點都呈上升或下降趨勢。
    注意：連續 6 點上升代表需要 6 個點，前後差值都是正（或都是負）。
    這裡定義「6 點趨勢」= 連續 6 個不等值，方向一致。
    """
    result: list[int] = []
    n = len(values)
    # 計算差分方向（+1上升, -1下降, 0持平）
    diffs = [1 if values[i + 1] > values[i] else (-1 if values[i + 1] < values[i] else 0)
             for i in range(n - 1)]
    # 需要 run_length - 1 個連續同向差分
    needed = run_length - 1
    for start in range(len(diffs) - needed + 1):
        window = diffs[start:start + needed]
        if 0 not in window and (all(d == 1 for d in window) or all(d == -1 for d in window)):
            for i in range(start, start + run_length):
                if i not in result:
                    result.append(i)
    return result


def _alternating(values: list[float], run_length: int) -> list[int]:
    """找出連續 run_length 個點呈鋸齒狀（每點都比前後兩點更高或更低）"""
    result: list[int] = []
    n = len(values)
    for start in range(n - run_length + 1):
        seg = values[start:start + run_length]
        is_alt = True
        for i in range(1, len(seg) - 1):
            # 每個中間點必須是局部極值
            is_local_max = seg[i] > seg[i - 1] and seg[i] > seg[i + 1]
            is_local_min = seg[i] < seg[i - 1] and seg[i] < seg[i + 1]
            if not (is_local_max or is_local_min):
                is_alt = False
                break
        if is_alt:
            for i in range(start, start + run_length):
                if i not in result:
                    result.append(i)
    return result


def _k_of_m_beyond_zone(
    values: list[float],
    cl: float,
    sigma: float,
    threshold: int,
    window: int,
    zone_min: int,
) -> list[int]:
    """
    連續 window 個點中有 threshold 個在同側 zone_min σ 以外。
    用於規則 5（3選2，±2σ）和規則 6（5選4，±1σ）。

    為什麼要求「同側」：
    - 西方電氣規則的原始定義要求同側，避免對稱性偏移被漏掉。
    """
    result: list[int] = []
    n = len(values)
    for start in range(n - window + 1):
        seg = values[start:start + window]
        # 檢查上側
        above = [i for i, v in enumerate(seg) if v > cl + zone_min * sigma]
        # 檢查下側
        below = [i for i, v in enumerate(seg) if v < cl - zone_min * sigma]
        if len(above) >= threshold or len(below) >= threshold:
            for i in range(start, start + window):
                if i not in result:
                    result.append(i)
    return result


def _all_within_zone(zones: list[int], run_length: int, zone_max: int) -> list[int]:
    """連續 run_length 個點都在 zone_max 以內（用於規則 7：連15點在±1σ內）"""
    result: list[int] = []
    n = len(zones)
    for start in range(n - run_length + 1):
        window = zones[start:start + run_length]
        if all(z <= zone_max for z in window):
            for i in range(start, start + run_length):
                if i not in result:
                    result.append(i)
    return result


def _none_within_zone(zones: list[int], run_length: int, zone_max: int) -> list[int]:
    """連續 run_length 個點都不在 zone_max 以內（用於規則 8：連8點無±1σ內）"""
    result: list[int] = []
    n = len(zones)
    for start in range(n - run_length + 1):
        window = zones[start:start + run_length]
        if all(z > zone_max for z in window):
            for i in range(start, start + run_length):
                if i not in result:
                    result.append(i)
    return result
