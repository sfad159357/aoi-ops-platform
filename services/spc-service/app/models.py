"""
SPC 資料模型（Pydantic）

為什麼把模型獨立一個檔：
- 讓 main.py / engine / charts 都能 import 同一份型別定義，避免重複定義造成欄位不一致。
- Pydantic v2 的 BaseModel 可以自動驗證輸入、產生 OpenAPI schema，方便前端串接。
"""

from __future__ import annotations
from typing import Literal
from pydantic import BaseModel, Field


# ─── 輸入模型 ──────────────────────────────────────────────────────────

class XbarRInput(BaseModel):
    """
    Xbar-R 圖輸入：多組子群資料（計量型）
    subgroup_size: 每個子群的量測數量（2–10）
    values: 所有量測值，按子群順序排列（長度需是 subgroup_size 的倍數）
    """
    subgroup_size: int = Field(ge=2, le=10, description="子群大小（2–10）")
    values: list[float] = Field(min_length=10, description="所有量測值（依子群順序）")
    usl: float | None = Field(default=None, description="規格上限（用於計算製程能力）")
    lsl: float | None = Field(default=None, description="規格下限（用於計算製程能力）")
    target: float | None = Field(default=None, description="目標值（用於計算 Ca）")


class IMRInput(BaseModel):
    """
    I-MR 圖（Individuals + Moving Range）輸入：單一量測值序列（計量型）
    適用於子群大小 = 1 的情況（例如每次只取一個量測值）
    """
    values: list[float] = Field(min_length=10, description="單一量測值序列")
    usl: float | None = Field(default=None, description="規格上限")
    lsl: float | None = Field(default=None, description="規格下限")
    target: float | None = Field(default=None, description="目標值")


class PChartInput(BaseModel):
    """
    P 圖輸入：每組樣本中不良品的比例（計數型）
    values: 每組不良品數量
    sample_sizes: 每組樣本大小（若固定可傳單一整數）
    """
    defective_counts: list[int] = Field(min_length=10, description="每組不良品數量")
    sample_sizes: list[int] | int = Field(description="每組樣本大小（固定值或各組值）")


class NpChartInput(BaseModel):
    """
    Np 圖輸入：每組樣本中不良品的絕對數量（計數型，樣本大小固定）
    """
    defective_counts: list[int] = Field(min_length=10, description="每組不良品數量")
    sample_size: int = Field(ge=1, description="固定樣本大小")


class CChartInput(BaseModel):
    """
    C 圖輸入：每個樣本的缺陷數（計數型，樣本大小固定）
    """
    defect_counts: list[int] = Field(min_length=10, description="每組缺陷數量")


class UChartInput(BaseModel):
    """
    U 圖輸入：每單位的缺陷數（計數型，樣本大小可變）
    """
    defect_counts: list[int] = Field(min_length=10, description="每組缺陷數量")
    sample_sizes: list[int] | int = Field(description="每組樣本大小（固定值或各組值）")


class CapabilityInput(BaseModel):
    """
    製程能力指數輸入（Ca、Cp、Cpk）
    可單獨使用，也可搭配 Xbar-R / I-MR 圖的結果
    """
    values: list[float] = Field(min_length=5, description="量測值序列")
    usl: float = Field(description="規格上限")
    lsl: float = Field(description="規格下限")
    target: float | None = Field(default=None, description="目標值（預設為規格中心）")


# ─── 共用輸出子結構 ──────────────────────────────────────────────────────

class ControlLimits(BaseModel):
    """
    管制線三條（UCL / CL / LCL）
    用於前端繪製參考線
    """
    ucl: float
    cl: float
    lcl: float
    sigma1: float | None = None   # ±1σ（用於八大規則 Zone C 判斷）
    sigma2: float | None = None   # ±2σ（用於八大規則 Zone B 判斷）


class RuleViolation(BaseModel):
    """
    八大規則單一違規記錄
    rule_id: 規則編號（1–8）
    rule_name: 中文說明
    points: 觸發的資料點索引（0-based）
    severity: 嚴重程度（red / yellow / green）
    """
    rule_id: int
    rule_name: str
    points: list[int]
    severity: Literal["red", "yellow", "green"]


class ChartPoint(BaseModel):
    """
    圖表單一資料點
    index: 序號
    value: 量測/統計值
    violation_rules: 此點觸發的規則清單（空 list 表示正常）
    """
    index: int
    value: float
    violation_rules: list[int] = []


class ProcessCapability(BaseModel):
    """
    製程能力指數結果
    """
    ca: float | None = Field(default=None, description="製程準確度（需要 target）")
    cp: float = Field(description="製程精密度")
    cpk: float = Field(description="製程綜合能力")
    cpu: float = Field(description="上側製程能力")
    cpl: float = Field(description="下側製程能力")
    mean: float
    std: float
    usl: float
    lsl: float
    target: float | None = None
    grade: str = Field(description="製程等級：A+/A/B/C/D")


# ─── API 回應模型 ────────────────────────────────────────────────────────

class XbarRResult(BaseModel):
    """Xbar-R 圖完整分析結果"""
    xbar_points: list[ChartPoint]
    r_points: list[ChartPoint]
    xbar_limits: ControlLimits
    r_limits: ControlLimits
    violations: list[RuleViolation]
    capability: ProcessCapability | None = None
    subgroup_size: int
    total_points: int


class IMRResult(BaseModel):
    """I-MR 圖完整分析結果"""
    x_points: list[ChartPoint]
    mr_points: list[ChartPoint]
    x_limits: ControlLimits
    mr_limits: ControlLimits
    violations: list[RuleViolation]
    capability: ProcessCapability | None = None
    total_points: int


class AttributeChartResult(BaseModel):
    """計數型圖表通用結果（P / Np / C / U 圖）"""
    chart_type: Literal["p", "np", "c", "u"]
    points: list[ChartPoint]
    limits: ControlLimits
    violations: list[RuleViolation]
    total_points: int


class DemoDataResponse(BaseModel):
    """Demo 資料回應（前端展示用，不需要真實製程資料）"""
    chart_type: str
    description: str
    data: dict
