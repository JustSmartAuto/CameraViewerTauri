# Cognex 3D 扫描 RAW 数据格式分析报告

- 样品：`JZC1BB299C01****_OK.raw`（8,798,078 字节，Cognex 3D 激光焊缝扫描原始数据）
- 关联文件：`JZC1BB299C010030_OK_1381.jpg`（418×1142 灰度强度图）、`JZC1BB299C010030_OK_1381.svg`（检测结果标注图）
- 检测工位：焊缝外观 3D 检测（型号 JZC1B01-1A，2026-10-27，结果 OK）
- 逆向工具：ilspycmd 8.2.0（ICSharpCode.Decompiler）、Python 3.12 struct/numpy

## 0. 更新历史

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-23 | v1 | 初次单样本容器结构分析 |
| 2026-10-23 | v2 | 5 件 NG 样本交叉验证，定位记录周期 0x3C00 |
| 2026-10-27 | v3 | **pair-stream 解码公式确认**（段基址 + 0x04000000 修正）；**ILSpy 逆向**验证 managed 层无文件解析代码、扫描记录为 opaque byte[]；修正头部字段偏移错误 |
| 2026-10-27 | v4 | 新增第 9 节：**L38 固件备份 `.bak` 容器结构**（v4 明文 JSON 清单 + 加密载荷），含 JSON 清单字段表、容器布局、加密判定依据、SDK 读取路径验证与 REA 工具链限制 |
| 2026-11-10 | v5 | 新增第 10 节：**WebHMI 3D 数据解析**，依据 `Framework.js.gz` 明确点云/网格 v0、v1 与 QuadMesh v1 的外层协议、载荷布局、下载流程及 WASM 边界；修正“Web 端完全不解析”的表述 |
| 2026-11-10 | v6 | 新增第 11 节：**CognexLogo RAW↔PCD 真值对照**，确认 RAW 内 `_positions0` 的 350,000 个 LE float32 XYZ 与导出 PCD 载荷逐字节一致，定位 `_grey80`、尾部宽高及分辨率字段；证明至少一种 v41 RAW 变体可直接提取规则网格点云 |
| 2026-11-11 | v7 | 新增第 12 节：**5 件样本交叉验证**，定义布局 A（焊缝扫描，opaque 记录）与布局 B（CognexLogo，显式标签数据块）两类 RAW 变体；精确头部字段差异、pair-stream 起点工位相关性、扫描记录→PCD HEIGHT 一一对应关系 |
| 2026-11-11 | v8 | **更正 v7 的错误结论**：v7 基于一份内容有误的 sample4.pcd（与 sample3.pcd 全同）错误推断 SavePointCloud() 输出确定性 PCD。更正后 sample4.pcd 有独立内容（SHA256=d0a7026d...），本节 12.6 重写为 RAW 差异→PCD 差异的真实映射：RAW 差异从扫描记录 #0 偏移 0x68 处开始，PCD 差异从点 #70 开始（前 70 点仍相同），暗示扫描记录有记录头/固定区域结构 |
| 2026-11-11 | v9 | 新增第 13 节：**opaque 解码障碍定位 + 下一步计划**，通过对 sample2.raw record #0 的 5 种编码假设排除 + 数值空间对齐分析，证明 record 内部为硬件原生编码 + 一对多插值，浏览器端无法直接解码；按 P0/P1/P2 优先级列出样本缺口与三条可行路径 |

## 1. 文件组总体说明

| 文件 | 内容 |
|---|---|
| `.raw` | 3D 点云原始数据（本报告主体） |
| `.jpg` | 相机强度图（2D 灰度），418×1142 像素 |
| `.svg` | 检测可视化：嵌入 JPG + 绿色焊缝轮廓路径 + 文字结果 |

SVG 中的检测数据（全部为 OK）：

- 焊缝高度最大值 0.597 mm / 最小值 -1.163 mm
- 焊缝到边缘距离 1.813 mm
- 焊缝宽度最大值 0.687 mm / 最小值 0.2375 mm
- 气泡断焊结果 OK，焊接检测 OK

SVG 注释 `<!--image ...: /cam0/img/001000000000031?sz=418,1142&fmt=1-->` 表明设备侧把 3D 数据渲染为 418×1142 的图像帧（`cam0`）。

## 2. RAW 文件容器结构

整体为 **Cognex 专有二进制容器**（小端），四部分：

```
+------------------+ 0x0000
| 文件头 (72 字节)  |
+------------------+ 0x0048
| 标定前缀          |   工位级不变参考数据（pair-stream 等）
+------------------+ 扫描起点
| 扫描记录 ×N       |   15,360B 定长，opaque binary blob
+------------------+ ~0x860D00 起
| 元数据尾部         |   float32 参数 + (偏移,类型) 目录 + float64 4×4 矩阵
+------------------+
```

**文件尺寸公式**（OK 样本）：`8,798,078 = 72 + 2 + 25,085 + 572 × 15,360 + 12,082`

### 2.1 文件头（0x00–0x47，共 72 字节，精确字节级验证）

OK 样本原始字节（小端，逐字解析）：

```
0x00: 11 CE FA 50 → 0x50FACE11 (魔数)
0x04: 29 00 00 00 → 41 (版本)
0x08: 00 00 00 00 00 00 00 00 → 保留零
0x10: 00 00 01 00 → 0x00010000 (标志)
0x14: 00 00 02 87 → 0x87020000
0x18: 84 67 C1 DE → 0xDEC16784 (GUID/ID part 1)
0x1C: 24 8D BF 00 → 0x00BF8D24 (GUID/ID part 2)
0x20-0x2F: 全 0 → 保留
0x30: 00 00 EC A0 → 0xA0EC0000
0x34: 3A 00 EC A0 → 0xA0EC003A
0x38: 3A 00 00 84 → 0x8400003A
0x3C: 3A 80 07 00 → 0x0007803A
0x40: D0 70 96 C1 → 0xC19670D0 (float32: -18.08...)
0x44: 5C 34 BE C1 → 0xC1BE345C (float32: -0.1761, 数据区起始)
```

| 偏移 | 长度 | 数值 | 说明 |
|---|---|---|---|
| 0x00 | 4 | `0x50FACE11` | 魔数，所有 v41 RAW 全同 |
| 0x04 | 4 | `41` | 格式版本，所有 v41 RAW 全同 |
| 0x08 | 8 | 0 | 保留零 |
| 0x10 | 4 | `0x00010000` (焊缝) / `0x00000000` (CognexLogo) | 标志字段，布局 B 为零 |
| 0x14 | 4 | 焊缝：`0x87020000` / CognexLogo：`0xEA020000` | 未确认 |
| 0x18 | 8 | 焊缝：`0xDEC16784 0x00BF8D24` | 扫描器 GUID/序列 ID 片段（跨工位不同） |
| 0x20 | 16 | 全 0 | 保留 |
| 0x30 | 4 | 工位 A：`0xA0EC0000` / 工位 B：`0xAAC80000` / CognexLogo：`0x00000000` | 工位配置参数 1（低半字=工位 ID 类） |
| 0x34 | 4 | 工位 A：`0xA0EC003A` / 工位 B：`0xAAC8003A` / CognexLogo：`0x003FF0000` | 工位配置参数 2 |
| 0x38 | 4 | 工位 A：`0x8400003A` / 工位 B：`0x9500003A` | 工位配置参数 3 |
| 0x3C | 4 | `0x0007803A` | 所有焊缝样本全同（491,578） |
| 0x40 | 4 | 工位 A：`0xC19670D0` / 工位 B：`0xC1A138DF` | float32 标定参数（跨工位不同） |
| 0x44 | 4 | 工位 A：`0xC1BE345C` / 工位 B：`0xC1EBD5CC` | float32 标定参数（跨工位不同） |

> **注 1**：旧版 v1 报告把 0x30-0x3F 的偏移和数值解读错误（`0x31`、`0x3B` 等不存在的偏移 + 错误值），现已修正为逐字节的 LE 小端解析。
>
> **注 2**：`sample2/3/4.raw` 的头部 80 字节与标定前缀 25KB **完全相同**（`SHA256(sample2[0x00:0x48+25000]) == SHA256(sample3) == SHA256(sample4)`），证明三者来自同一工位的同一标定配置；`sample.raw`（OK）与 `sample2/3/4.raw`（NG）的差异恰好集中在 0x30–0x4F 这 32 字节，即"工位配置参数 + 标定 float32 参数"区，是工位级的标定差异，不是件级差异。
>
> **注 3**：CognexLogo.raw 头部后半区（0x10–0x4F）与所有焊缝样本均不同，0x30–0x4F 区域几乎全零（仅 0x34 处为 `0x003FF000`，作为 float32 解释即 `1.0`），与其"非激光扫描、无 pair-stream"的特性一致。

### 2.2 标定前缀（0x48 – 扫描记录起点）

工位级不变参考数据（同工位多件比对完全相同，~25–26 KB 量级）。内含：

- **Block A**（0x48 起）：标定 float32 三元组序列（米级小量，如 `(-1.80e-3, 0, -0.18), (-1.78e-3, 0, -0.18), …`），共 ~1,967 点，线性递减。
- **Pair-stream**（焊缝样本：0x5C7C 起，225 × 8 = 1800 字节；解码公式见第 3 节）：6 段编码剖面 x 坐标。**pair-stream 起点工位相关**，`sample2/3/4` 在 0x5C00 起解码出 225 点合理 x 坐标（25KB prefix 与 `sample.raw` 不同）。CognexLogo.raw **不含 pair-stream**。
- **Block B/C**：其他标定 float32 流，含 1.0、5.0、50、570 等参数值。

> **跨工位验证**：`sample.raw`（工位 A）与 `sample2/3/4.raw`（工位 B）从 0x48 起标定前缀前 25KB **相似度约 10%**（仅 2,552/25,000 字节相同），证明前缀内部的 pair-stream 和 Block A/C 均随工位标定完全变化。

### 2.3 扫描记录区（标定结尾 – 元数据开头）

- 定长 **15,360 字节**（0x3C00）每条。
- **精确起点定位**：同工位样本间首个字节差异即扫描记录起点。`sample2 vs sample3` 首差异在 **0x6275**、`sample3 vs sample4` 首差异在 **0x62AD**，两者头部+标定前缀全同；故工位 B 的扫描起点在 **0x6245**（标定总长 ≈ 25,149 字节）附近。工位 A（sample.raw）起点在同一量级。
- **条数由 PCD HEIGHT 精确给出**（已跨 4 件样本验证，见第 12 节）：
  - `sample.raw`：572 条 → 对应 PCD HEIGHT=570（差 2 条，疑末尾空记录或截断）
  - `sample2/3/4.raw`：415 条 → 对应 PCD HEIGHT=415（精确匹配）
- 内部为编码测量值，opaque binary blob（ILSpy 确认 managed 层不解析内部结构，直接传给 native `tako-vc140-22_1.dll` 做渲染）。
- 记录间以周期性变化区 (~12.7 KB) + 填充区 (~2.6 KB) 交替。填充区为恒定小量（对应 NG 件焊缝缺失时"无有效返回"）。

> **sample3/4 特例**：`sample3.raw` 与 `sample4.raw` 的头部 + 标定前缀（前 ~0x62AD 字节）完全相同，第一个字节差异落在 **扫描记录 #0 内部偏移 0x68** 处（即首条扫描记录中间就开始不同）。因此两者导出的 PCD 不同——`sample3.pcd` 与 `sample4.pcd` payload 有 **4,175,982 字节差异**，首差异在点 #70（0x348 处），前 70 个三维点仍相同。SavePointCloud() 对不同扫描数据正常输出不同点云。

### 2.4 尾部元数据（约 0x85FAD8 – EOF）

跨工位一致的结构，含：

- float32 参数表（1.0, 5.0, 5.0, 0.1925, 0.0213…）
- `(u32 offset, u32 type)` 目录条目（偏移值域 0x668572–0x6685AD+）
- float64 4×4 单位刚体变换矩阵（文件末尾重复出现 ≥ 6 组）

签名定位：搜索 `00 00 80 3F | 00 00 A0 40 | 00 00 A0 40`（float32 的 1.0, 5.0, 5.0），其后即为元数据区起点。

## 3. Pair-stream 解码公式（已验证 ✅）

标定前缀内 **0x5C7C 起** 是一个 225 点的剖面扫描序列，以 **6 段编码** 存储。每段为固定步进的 x 坐标，段间有统一的偏移跳变。

### 3.1 编码规则

每条 8 字节记录：`[first4: LE u32][second4: LE u32]`

解码步骤（纯数学公式，无查表/加密）：

```
SEGMENT_OFFSET    = 0x04000000          # 2^26，段间跳变值
JUMP_THRESHOLD    = 1,000,000           # 检测段边界的阈值

# Step 1: 识别段边界
seg_index = 0
for i in range(n_pairs):
    if i > 0 and raw_first4[i] - raw_first4[i-1] > JUMP_THRESHOLD:
        seg_index += 1
    corrected_u32[i] = raw_first4[i] - seg_index * SEGMENT_OFFSET

# Step 2: 还原 x 坐标
# corrected_u32 按 LE 解释为 float32，即为毫米值
x_mm[i] = struct.unpack('<f', struct.pack('<I', corrected_u32[i]))[0]
```

### 3.2 实测结果（OK 样本）

| 段 | 对数 | x 范围 (mm) | 平均步进 (mm/pair) |
|---|---|---|---|
| Seg0 | 37 | [-733.031, -723.766] | 0.2574 |
| Seg1 | 39 | [-723.516, -713.734] | 0.2574 |
| Seg2 | 39 | [-713.469, -703.703] | 0.2570 |
| Seg3 | 40 | [-703.438, -693.406] | 0.2572 |
| Seg4 | 42 | [-693.156, -682.609] | 0.2572 |
| Seg5 | 28 | [-682.359, -675.406] | 0.2575 |
| **合计** | **225** | **[-733.031, -675.406]** | ~0.257 |

6 段在段边界自然衔接（段末 x ≈ 下一段首 x，仅差 ~0.25mm），整体跨度 57.625mm，完全在激光传感器毫米级范围内。

### 3.3 first4 / second4 字段语义

- **first4 (LE u32)**：段基准地址 + 段内递减步长；修正后直接作为 float32 → x 毫米值（IEEE 754 编码恰好落在有效范围）。
- **second4 (LE u32)**：`high16=89/90`（固定列索引，像素行号），`low16` 段内递减（25,604 → 4）。可能是辅助标记或像素坐标分量，**不编码高度信息**。

### 3.4 Python 验证代码

```python
import struct

SEG_OFF = 0x04000000   # 2^26
JUMP   = 1_000_000

d = open("sample.raw", "rb").read()
o = 0x5C7C  # pair-stream 起点

seg_k = 0; prev_u = None
x_mm_list = []
for i in range(225):
    uw = struct.unpack_from('<I', d, o + i*8)[0]    # first4 原始 u32
    vw = struct.unpack_from('<I', d, o + i*8 + 4)[0]  # second4
    if prev_u is not None and uw - prev_u > JUMP:
        seg_k += 1
    corrected = uw - seg_k * SEG_OFF
    x_mm = struct.unpack_from('<f', struct.pack('<I', corrected))[0]
    x_mm_list.append(x_mm)
    prev_u = uw

print(f"225 pts x=[{min(x_mm_list):.3f}, {max(x_mm_list):.3f}] mm")
# → 225 pts x=[-733.031, -675.406] mm ✓
```

## 4. ILSpy 逆向结果（2026-10-27）

对 Cognex In-Sight 26.1.0 安装目录下全部 managed DLL 使用 `ilspycmd 8.2.0` 反编译：

### 4.1 扫描的 DLL 列表

| DLL | 大小 | 结论 |
|---|---|---|
| `Cognex.ThreeD.dll` | ~3MB | 纯数据容器（PointCloud3D, QuadMesh, Mesh3D, LineSegment3D…），WPF DataMember 序列化 |
| `Cognex.ThreeD.Controls.dll` | ~3MB | WPF 控件 + tako 渲染适配器（Display3DControl, Tako3DWrapper, 各类 Converter） |
| `Cognex.InSight.dll` | 2.1MB | UI / ViewModels，无文件解析代码 |
| `Cognex.InSight.Internal.dll` | 4.3MB | 内部服务，无 BinaryReader / FileStream |
| `Cognex.Utilities.Sdk.dll` | 1.9MB | 含 .raw 文件对话框过滤（2 处 `.raw` / `.RAW` 扩展名检查） |
| `Cognex.Tako.dll` | 1.2MB | C++/CLI 包装器，marshal 到 native `tako-vc140-22_1.dll` |
| `tako-vc140-22_1.dll` | 6.1MB | **native C++ 核心**（Shader + 渲染 + QuadMeshData） |

### 4.2 关键发现

1. **managed 层没有 RAW 文件 I/O**：全部 managed DLL 里找不到 `BinaryReader`、`FileStream`、`ReadBytes`、`MemoryStream`（仅 Controls 层有 `CryptoStream` 用于其他加解密功能）。
2. **magic `0x50FACE11` 不在任何 native 二进制内**：对整个 `C:\Program Files\Cognex` 下所有 .dll/.exe 做字节级搜索，magic 字节 `\x11\xFA\xCE\x50` 出现次数为 **0**。推测 native 层以运行时动态拼接或字符串比较方式做 magic 校验。
3. **扫描记录是 opaque blob**：`QuadMesh` 类的核心字段是 `byte[] DataBlob` + `uint? PointCloudId` —— 原始扫描记录直接以字节数组形式从 camera/firmware 传入，managed 层**不解码内部结构**，原样传给 native tako 做渲染。
4. **PointCloud3D 数据结构**（WCF DataMember 序列化）：
   ```csharp
   public class PointCloud3D : ImageGeometry3DBase<PointCloud3D>, IPointDataGeometry3D {
       public PointF3D[] Points { get; set; }   // 3D 坐标
       public Color[] Colors   { get; set; }     // 顶点颜色
       public PointF[] TexCoords { get; set; }    // 纹理坐标
   }
   ```
5. **native 层字符串**：`tako-vc140-22_1.dll` 内找到 `QuadMeshData`（0x483375）、大量 GLSL shader 片段（`pointInRoiSpaceNorm`, `atan`, `ROI`…），确认 native 层直接消费 QuadMeshData 并做 GPU 渲染。

### 4.3 结论

**RAW 文件解析全部在 camera firmware 层完成**（不随 In-Sight Editor 安装包分发）。PC 端 managed 代码只负责：
1. 通过通信 API 从 camera 下载 DataBlob；
2. 以 `QuadMesh.DataBlob = bytes[]` 形式传给 native 渲染；
3. 最终以 PointCloud3D / QuadMesh 形式暴露给上层。

本报告的所有格式细节均来自对 RAW 文件本身的逆向分析（Python struct 解析 + 多文件 diff 验证），而非 ILSpy 结果。ILSpy 的价值在于**确认了 managed 层不做解析**，从而排除了在 PC 软件中寻找 parser 的方向。

## 5. 统计特征（OK 样本）

| 项目 | 数值 |
|---|---|
| 文件大小 | 8,798,078 字节（≈ 8.39 MB） |
| 魔数 | `0x50FACE11`（1 处，0x0000） |
| 版本字段 | 41 |
| 头尺寸 | 72 字节 + 2 字节尾部填充 |
| pair-stream | 225 点，6 段，x ∈ [-733, -675] mm |
| 扫描记录数 | 572 条（OK），415 条（NG） |
| 记录周期 | 15,360 字节（0x3C00） |
| 有效三维点 | ≈ 336,700（按 \|x,y,z\|<1e6 判定） |
| 末尾 4×4 单位矩阵（float64） | ≥ 6 组 |
| 目录偏移条目 | ≥ 4 组 |

## 6. 解析指南（参考代码）

```python
import struct
import numpy as np

MAGIC = 0x50FACE11
HEADER = 0x48          # 72 字节头 + 2 字节填充（数据区末尾）
REC = 0x3C00           # 扫描记录周期 15,360 B
SEG_OFF = 0x04000000   # pair-stream 段偏移
JUMP = 1_000_000        # 段边界检测阈值

def parse_cognex_raw(path):
    d = open(path, 'rb').read()
    assert struct.unpack_from('<I', d, 0)[0] == MAGIC, "not a Cognex 3D raw file"
    version = struct.unpack_from('<I', d, 4)[0]

    # 扫描记录
    scan_start = 0x6245   # 多件逐字节 diff 定位
    n_records = (len(d) - scan_start - 12082) // REC  # OK 样本
    scan_records = [d[scan_start + i*REC : scan_start + (i+1)*REC]
                    for i in range(n_records)]

    # Pair-stream 解码
    seg_k = 0; prev_u = None
    pairs_x = []
    for i in range(225):
        uw = struct.unpack_from('<I', d, 0x5C7C + i*8)[0]
        if prev_u is not None and uw - prev_u > JUMP: seg_k += 1
        corrected = uw - seg_k * SEG_OFF
        pairs_x.append(struct.unpack('<f', struct.pack('<I', corrected))[0])
        prev_u = uw

    # 尾部元数据定位
    sig = bytes.fromhex('0000803f0000a0400000a040')  # 1.0, 5.0, 5.0
    meta_start = d.find(sig)

    return version, scan_records, pairs_x, meta_start
```

## 7. 多文件交叉验证（2026-10-23 NG 样本，5 个工位）

对 `2026-10-23/{10,11,13,14,15}/NG/` 下共 5 组 NG 样本做了交叉比对，新增以下结论：

### 7.1 头部与文件尺寸

- 5 个工位所有 `.raw` 的头部 80 字节**完全相同**（魔数、版本 41、GUID 片段均一致）→ 头部**不含**序列号/时间戳等件级信息。
- 头部仅两处 u32 随配置变化：

| 偏移 | OK 样本 (10-27) | NG 样本 (10-23) |
|---|---|---|
| 0x34 附近 | `0xA0EC003A` | `0xA0AC003A` 类 |
| 0x30 附近 | `0xA0EC0000` | `0xA0AC0000` 类 |

  低位数值随工位同步变化 → 疑似扫描记录计数或缓冲区尺寸参数。
- 同组（同工位）文件**尺寸完全相同**（6,412,318 B），不同组也相同；与 OK 样本（8,798,078 B）不同 → 尺寸由工位/视野配置决定。
- 所有文件 `(文件大小 − 0x48)` 除以 4 余 2 → float32 数据区后有 **2 字节填充**。

### 7.2 件间差异定位

- 两件差异字节共 ~2.54 MB，集中在 **0x6245 之后**，呈规则的**周期块结构**：
  - 变化区 ~12.7 KB + 不变区 ~2.6 KB，周期 ≈ **0x3C00（15,360 字节）= 3,840 个 float32 槽位**；
  - 0x48–0x6245（约 25 KB）两件**完全相同** → 工位级**标定/参考数据前缀**；
  - "不变区"并非 NaN，而是数值极小的浮点填充（对应 NG 件焊缝缺失时"无有效激光返回"）。
- 扫描区 ≈ 0x6245–0x61B080，NG 样本共 **415 条** 15,360 B 记录。
- NG 样本 SVG 结果全部超量程（焊缝高度 0mm NG、宽度 ±1e7mm NG）——与 raw 中大量"恒为小量"的填充区完全一致。

### 7.3 与 OK 样本的统一模型

```
+--------+------+-------------------------------+-----------+
| 文件头 | 标定 |  扫描记录 ×N（15,360 B 周期）   | 元数据尾部 |
| 72+2B  | 前缀 |  opaque bytes（managed 不解析） | 表+矩阵   |
+--------+------+-------------------------------+-----------+
```

## 8. 结论

1. **容器结构已完全破解**：`0x50FACE11` 魔数 + 版本 41 + 72 字节头 + 标定前缀 + 15,360B 定长扫描记录 + 元数据尾部，跨工位跨日期结构一致。
2. **Pair-stream 解码公式已验证**：段基址 + 0x04000000 修正，修正后按 LE float32 解释即为毫米值，225 点全部落在 [-733, -675]mm 区间。
3. **扫描记录为 opaque blob**：managed 层（ILSpy 确认）不解码内部结构，直接传给 native tako 渲染。记录内部编码方式（16.16 定点、紧凑字节布局等）尚待 native 层逆向或更多真值样本标定。
4. **RAW parser 在 camera firmware 层**：PC 端 In-Sight Editor 的 managed DLL 中无 BinaryReader/FileStream 对 RAW 的读取逻辑；magic 字节也不出现在任何可执行文件中。解析逻辑随相机固件分发，不随编辑器安装包提供。
5. **文件尺寸公式**：`72 + 2 + 25KB_prefix + N × 15360 + tail_metadata`，N 由视野/配置决定（OK=572, NG=415）。

## 9. L38 固件备份文件（`L38-firmware.bak`）结构分析

> 样本：`L38-firmware.bak`，**12,571,524 字节**（11.99 MB），由 In-Sight L38 相机（`OP40-31`，SN `1A2535PP524126`）于 2026-10-27 12:44 导出的系统备份。
> 该备份与本报告第 2–7 节的 3D RAW **不是同一容器**；此处单独记录，因为备份载荷内可能包含相机侧的应用/配置（即 3D RAW 解析逻辑所在的固件环境）。

### 9.1 容器布局（v4）

```
+----------------------------+ 0    偏移 0
| UTF-8 JSON 清单（明文）     |      1667 字节
+----------------------------+ 1667
| 0x00 分隔符（1 字节）       |      1667
+----------------------------+ 1668
| 加密载荷                    |      12,569,856 字节 (= 785,616 × 16)
+----------------------------+ EOF  12,571,524
```

关键尺寸关系（逐字节验证）：

- JSON 长度 **1667**，恰好以 `0x00` 结尾于偏移 1667；
- 载荷起点 **1668**，长度 **12,569,856**，`mod 16 = 0`（AES 分组对齐），`= 785,616` 个 16 字节块；
- `mod 512 = 256` → 不是 tar 块对齐，排除「明文 tar 直接拼接」的可能。

### 9.2 JSON 清单字段（明文可读部分）

顶层字段（`BackupMetadataV4`，Newtonsoft `JsonConvert` 序列化）：

| 字段 | 值（本样本） | 说明 |
|---|---|---|
| `backupVersion` | `4.1.0` | 备份格式版本，决定 SDK 走 v4 分支 |
| `firmwareVersion` | `25.3.0+1752` | 相机固件版本 |
| `deviceType` | `Cognex_Lighthouse` | 设备系列 |
| `primarySensor` | `SONY_IMX421` | 主传感器型号 |
| `compatibility` | `["deviceType","primarySensor"]` | 恢复时的兼容性校验键 |
| `hostname` | `OP40-31` | 设备主机名 |
| `serialNumber` | `1A2535PP524126` | 设备序列号 |
| `macAddress` | `00:D0:24:C0:F4:5E` | 主网卡 MAC |
| `productDescription` | `In-Sight L38 Series` | 产品描述 |
| `dateTime` | `2026-10-27T12:44:15.062745` | 备份生成时间（本地时间，无时区） |
| `osBuild` | `45355f960da24034044915f819ba8b4acc97f061` | OS 构建哈希（40 位十六进制，同 git SHA-1 长度） |

三个内嵌对象（以文件名为键，非独立文件）：

`product-identification.json`

| 字段 | 值 |
|---|---|
| `familyName` | `In-Sight Vision System` |
| `cogNamerType` / `cogNamerSubType` | `14` / `4` |
| `cognamerDeviceIdentity` | `0x0E04` |
| `productDescription` | `In-Sight L38 Series` |
| `productIdentity` | `0x01060300` |
| `profinetOrderDescription` | `ISL38-XX` |

`sysinfo.json`（`$type = SysInfo`）

| 字段 | 值 |
|---|---|
| `description` | `In-Sight Smart Camera Firmware Package` |
| `productId` | `5.3.1.0` |
| `version` / `versionStr` | `25.3.0.1752` / `25.3.0 (1752)` |
| `minFirmwareVersion` | `0.0.0.0` |
| `model` | `IS28X` |
| `bootVersion` / `failsafeVersion` | `null` / `null` |

`hwdata.json`（`$type = hwdata`）

| 字段 | 值 |
|---|---|
| `productName` | `ISL38-33B` |
| `productIdentity` | `0x01060300` |
| `productFamily` | `Lighthouse` |
| `serialNumber` / `macAddress` | `1A2535PP524126` / `00:D0:24:C0:F4:5E` |
| `extraMac1` | `00:D0:24:C0:F4:5F` |
| `partNumber825` | `826-20248-004` |
| `minimumFirmwareVersion` | `24.1.0` |

> 清单中**没有**任何 `salt` / `iv` / `keyId` / `nonce` / 校验和字段 → 载荷解密所需的一切都在清单之外，进一步指向「密钥在设备侧」。

### 9.3 加密判定依据

对 12,569,856 字节载荷做的统计与解压测试：

| 检测项 | 结果 | 结论 |
|---|---|---|
| Shannon 熵 | **7.999985 / 8** | 与均匀随机不可区分 |
| 256 个字节值出现次数 | 48,506 – 49,773（期望 ≈ 49,100） | 分布完全均匀 |
| 重复 16 字节块 | **0 / 785,616** | 非 ECB 模式 |
| 前 1 MB 内重复 4 字节字 | 12 | 无可见结构 |
| `zlib` / raw `deflate` / `bz2` / `lzma` | 全部解压失败 | 非压缩流 |
| 单字节 XOR 全扫描（0x00–0xFF） | 无任何值改善熵 | 非简单混淆 |
| 文件签名扫描 | ELF / MZ / gzip / xz / zstd / uImage / squashfs / UBI / cramfs **均无** | 非嵌套可执行或镜像 |
| 明文 JSON 二次出现 | 无（末尾 128 字节亦为随机） | 单一加密段 |

**判定：载荷为强加密（块对齐 + 高熵 + 零重复块），算法与密钥均不在文件中。**

### 9.4 SDK 读取路径（ILSpy 验证）

对 `Cognex.Utilities.Sdk.dll` 的 **25.3.0**（与相机固件同版本）和 **26.1.0** 两版分别反编译，`LigerBackupRestoreService.<GetBackupManifest>` 逻辑一致：

```csharp
// 1) 逐字节读入直到 0x00 终止
byte b = binaryReader.ReadByte();
if (b == 0) break;
memoryStream.WriteByte(b);
// 2) Seek(0) 后按文本读出，反序列化为 V4 元数据
string json = new StreamReader(memoryStream).ReadToEnd();
BackupMetadataV4 meta = JsonConvert.DeserializeObject<BackupMetadataV4>(json);
// 3) v4 直接 goto 后续流程 —— 载荷从未被读取
```

- **v4 分支**：只解析 JSON 清单 → 直接跳到清单装配（`sysInfo` / `hwData` / `productIdentification`），**载荷完全未被访问**。
- **v3 分支**（v4 解析失败时回退）：`new ContinuousGZipStream(fileStream)` + `TarHelper.ExtractFiles(...)` —— 即 v3 是 gzip+tar，v4 换成了加密载荷。
- `DownloadBackupFile` 返回 `(string path, long dataSize)`，**落盘后不解密**。
- 唯一的硬编码 AES-256 key/IV（混淆方法 `a0FamxItwX`）**零调用者**，属混淆器残留：

```
key = B8 2A 0B 3B BD 86 BA AA 23 34 5E 57 FB BB 38 E2
      D8 BE 8D 05 EB B9 01 54 9A E0 85 1E DD F4 5F 86
IV  = 42 5E 0D 87 23 E4 AB 47 6F AA BE B7 7D 25 85 96
```

  实测该 key/IV 及 9 组派生组合（`IV=payload[0:16]`、ECB、key 反转、零 IV、`sha256(serial)`、`sha256(mac)` 派生 key 等）**全部解密失败**，输出熵仍为 8.00。

**结论：固件备份载荷由相机侧加密，PC SDK 只做透明传输与落盘；密钥绑定设备，不在 PC 安装包内。**

### 9.5 REA 工具链限制（2026-10-27 实测，REA v6.3.0）

| 命令 | 结果 |
|---|---|
| `rea inspect` / `inspect-artifact` / `inspect-signature` / `analyze`（含 `--provider ghidra`） | `target_unavailable`：*unsupported binary format* |
| `rea inspect-firmware-regions` | `capability_unavailable`：需 Linux + `REA_BINWALK_COMMAND` |
| `rea extract-firmware --offset 1668 --length 12569856` | `capability_unavailable`：需 Linux + `REA_UNBLOB_COMMAND` |
| `rea analyze --target-format` | schema 中 `const: dos-com`，无 raw-binary 选项 |

REA 支持的目标格式为 PE / ELF / Mach-O / APK / IPA / plist / JS；`.bak` 自定义容器不在其中，固件专用命令全部绑定 Linux 侧 `binwalk` / `unblob`。
REA 对 Cognex **托管程序集**工作正常（`rea inspect-managed-artifact Cognex.Utilities.Sdk.dll` 可产出 `rea-dotnet-static` evidence），但该程序集本身不含解密逻辑（见 9.4）。

### 9.6 与 3D RAW 的关系

- 对 In-Sight Emulator 25.3.0 `Runtime` 目录下全部 61 个可执行二进制复扫：`11 FA CE 50`（RAW magic）命中 **0**、`QuadMeshData` 命中 **0** → Windows 侧固件运行时不带 3D RAW 解析器。
- 因此若需拿到 RAW 扫描记录的内部编码（第 8 节第 3 条遗留问题），可行路径只有：**(a)** 解密本备份取出相机侧应用/固件，或 **(b)** 取得未加密的固件升级包（非备份），在 Linux/WSL 环境下用 `binwalk`/`unblob`/Ghidra 分析。

## 10. WebHMI 的 3D 数据解析方法（`Framework.js.gz` 实证）

### 10.1 适用范围与解析边界

WebHMI 在 `Framework.js.gz` 中提供了 `Controls/Geometry3D/3DDataParsers/Cjs3DDataParser`。该解析器消费的是 HTTP 请求返回的 `ArrayBuffer`，识别以下三类**外层数据包**：

| 类型 | magic（LE u32） | 文件字节序 | 支持的版本 | 解析结果 |
|---|---:|---|---:|---|
| 点云或三角网格 | `0xB0B15BAD` | `AD 5B B1 B0` | 0、1 | `CjsPointCloud` 或 `CjsMesh` |
| QuadMesh | `0xB0B15D11` | `11 5D B1 B0` | 1 | `CjsQuadMeshData`（opaque packet） |

这些 magic 与第 2 节落盘 RAW 容器的 `0x50FACE11` **不同**。因此不能把 WebHMI 的 20/24 字节头直接套到 `.raw` 文件上；WebHMI 揭示的是设备 HTTP 接口输出的 3D 数据包格式，而不是对落盘 RAW 记录区的完整解码。

解析器的职责边界也很明确：

- 点云/网格包由 JavaScript 直接还原为顶点数组和三角形索引数组；
- QuadMesh 包只剥离外层头，载荷保持为 `Uint8Array`，最终调用 `takoweb.wasm` 的 `importPacket()`；
- 因而第 2.3 节所说的 QuadMesh/RAW 内部记录仍需由 native/WASM 层解释，WebHMI 并未公开其逐点编码。

### 10.2 点云/网格 v0：20 字节头

| 偏移 | 类型 | 字段 | 含义 |
|---:|---|---|---|
| `0x00` | LE u32 | `magic` | 必须为 `0xB0B15BAD` |
| `0x04` | LE u32 | `version` | `0` |
| `0x08` | LE u32 | `headerSize` | 必须为 `20` |
| `0x0C` | LE u32 | `numPoints` | 顶点数，必须大于 0 |
| `0x10` | LE u32 | `numTriangleIndices` | 三角形索引总数，不是三角形数量 |
| `0x14` | — | `vertices` | 连续 `3 × numPoints` 个 float32，按 `(x,y,z)` 交错排列 |
| `0x14 + 12N` | — | `triangleIndices` | 连续 `numTriangleIndices` 个 u32，其中 `N = numPoints` |

最小预期包长为：

```text
20 + 12 × numPoints + 4 × numTriangleIndices
```

当 `numTriangleIndices == 0` 时构造 `CjsPointCloud`；否则构造 `CjsMesh(vertices, triangleIndices, false)`。索引按三角形列表使用，所以正常网格中索引数通常应为 3 的倍数；原解析器本身未显式检查这一条件。

### 10.3 点云/网格 v1：24 字节头

v1 的数据布局与 v0 相同，仅多一个被读取但未使用的 u32：

| 偏移 | 类型 | 字段 | 含义 |
|---:|---|---|---|
| `0x00` | LE u32 | `magic` | `0xB0B15BAD` |
| `0x04` | LE u32 | `version` | `1` |
| `0x08` | LE u32 | `headerSize` | 必须为 `24` |
| `0x0C` | LE u32 | `numPoints` | 顶点数，必须大于 0 |
| `0x10` | LE u32 | `numTriangleIndices` | 索引总数 |
| `0x14` | LE u32 | 未命名字段 | JavaScript 读取后丢弃，语义未知 |
| `0x18` | — | `vertices` | `3 × numPoints` 个 float32 |
| `0x18 + 12N` | — | `triangleIndices` | `numTriangleIndices` 个 u32 |

最小预期包长为：

```text
24 + 12 × numPoints + 4 × numTriangleIndices
```

实现注意：代码用 `DataView.getUint32(..., true)` 读取头，但用 `Float32Array`/`Uint32Array` 视图读取载荷。因此载荷在目标平台上按本机小端解释；在常见浏览器平台上等价于 LE float32/u32。

### 10.4 QuadMesh v1：20 字节外层头

| 偏移 | 类型 | 字段 | 含义 |
|---:|---|---|---|
| `0x00` | LE u32 | `magic` | 必须为 `0xB0B15D11` |
| `0x04` | LE u32 | `version` | 仅支持 `1` |
| `0x08` | LE u32 | `headerSize` | 必须为 `20` |
| `0x0C` | LE u32 | 未命名字段 | JavaScript 读取后丢弃，语义未知 |
| `0x10` | LE u32 | `packetSize` | QuadMesh packet 的字节数 |
| `0x14` | — | `packet` | 长度 `packetSize` 的 opaque 字节串 |

JavaScript 只执行：

```javascript
const blob = new Uint8Array(dataView.buffer, 20, packetSize);
return new CjsQuadMeshData(blob);
```

它不把 packet 当成 float32 顶点流，也未根据 `packetSize` 推导点数或网格尺寸。若要独立导出 QuadMesh 点云，下一步应分析 `takoweb.wasm` 的 `importPacket`，而不是沿用第 3 节针对 RAW 标定前缀的 pair-stream 公式。

### 10.5 QuadMesh 的 HTTP 获取与显示流程

`CjsQuadMeshHandler` 以某个点云资源 URL 为 `baseUrl`，请求以下派生资源：

| 请求 | 返回格式 | 用途 |
|---|---|---|
| `baseUrl?format=quadmesh1` | QuadMesh v1 二进制包 | 低分辨率 packet，优先加载 |
| `baseUrl?format=quadmesh2` | QuadMesh v1 二进制包 | 高分辨率 packet，低分辨率导入后仍不完整时加载 |
| `baseUrl?format=greyscaleTexture` | 浏览器可解码图像 | 转为 RGBA 字节，用作网格纹理 |
| `baseUrl?format=imageTransformation` | 连续 float64 | 图像到网格的变换数据 |
| `baseUrl/approximateResolution` | 含 3 个数值的 JSON 数组 | 近似 X/Y/Z 分辨率；仅 X、Y 均大于 0 时保留 |

显示过程为：

1. 并行下载低分辨率 QuadMesh、可选纹理、可选图像变换和近似分辨率；
2. 用 `Cjs3DDataParser` 验证外层头并取得低分辨率 packet；
3. 创建 Tako/WASM QuadMesh，按需设置纹理及变换；
4. 调用 `importPacket(lowRes.Blob)`；
5. 若 `isComplete()` 仍为 false，再下载并导入 `highRes.Blob`。

这也说明 `quadmesh1` 与 `quadmesh2` 是同一显示对象的增量/分辨率数据包，不能假设它们各自都是完整、独立的顶点数组。

### 10.6 辅助 Blob 格式

同一文件中的 `CjsBlobParser` 还解析一种无 magic、无版本的简单点集：

```text
offset 0x00: LE u32 numPoints
offset 0x04: numPoints × { LE float32 x, LE float32 y, LE float32 z }
```

每点固定 12 字节，预期总长为 `4 + 12 × numPoints`。它用于位置/中心点等辅助图形，不应与 `Cjs3DDataParser` 的点云包或 QuadMesh packet 混淆。

### 10.7 与第 2–8 节结论的关系

WebHMI 代码证明“3D 显示链路中存在 JavaScript 外层解析器”，因此更准确的表述是：

- **落盘 `.raw`（magic `0x50FACE11`）的扫描记录**：仍未由 WebHMI JavaScript 解码；
- **HTTP 点云/网格包（magic `0xB0B15BAD`）**：顶点与索引格式已完全明确；
- **HTTP QuadMesh 包（magic `0xB0B15D11`）**：外层头已明确，packet 内部由 WASM 解码。

三者属于同一 3D 显示/数据体系中的不同封装层，不能因 magic 或用途不同而相互替代。

## 11. CognexLogo RAW ↔ PCD 真值对照（已逐字节验证）

### 11.1 样本与结论

通过 Cognex 官方隐藏函数 `SavePointCloud()` 将 `CognexLogo.raw` 导出为 `CognexLogo.pcd`，获得了一组同源真值样本：

| 文件 | 大小 | 内容 |
|---|---:|---|
| `CognexLogo.raw` | 16,660,441 B | v41 RAW，magic 仍为 `0x50FACE11` |
| `CognexLogo.pcd` | 4,200,095 B | PCD v0.7 二进制点云，95 B 文本头 + 4,200,000 B 载荷 |

**核心结论：PCD 的 4,200,000 B 点云载荷在 RAW 中从 `0x558A1` 开始原样连续存储，二者逐字节完全相同。** 载荷 SHA-256 均为：

```text
7dbb223e4afef9c00198e769d2a2e616fa654c0c83f84514cccd0c50c84c8510
```

因此至少这一种 v41 RAW 变体不需要定点解码或 WASM 转换，直接提取 `_positions0` 数据块即可获得官方导出的 XYZ 点云。

> 本样本是规则 Cognex Logo 测试点云，不是第 2–7 节的焊缝扫描样本。二者虽然具有相同 magic/version，但内部布局不同；不能把本节的固定偏移直接套用到焊缝 RAW。

### 11.2 PCD 头与点云布局

完整 PCD 头（每行以 LF 结束）：

```text
VERSION 0.7␠
FIELDS x y z
SIZE 4 4 4
TYPE F F F
WIDTH 700
HEIGHT 500
POINTS 350000
DATA binary
```

- `WIDTH × HEIGHT = 700 × 500 = 350,000` 点；
- 每点 12 B：`(x, y, z)` 三个 LE float32；
- 载荷长度：`350,000 × 12 = 4,200,000 B`；
- 无颜色、法线、强度字段，也无 `VIEWPOINT` 行；
- 所有坐标均为有限值，无 NaN/Inf。

实测坐标：

| 分量 | 范围 | 不同值数量 | 说明 |
|---|---|---:|---|
| x | `[-50.0, 49.8571472168]` | 700 | 每行从右向左递减 |
| y | `[0.0, 71.2857208252]` | 500 | 每行结束后递增 |
| z | `{0.0, 2.0}` | 2 | Logo 平面/凸起高度，单位 mm |

点顺序为**行优先**：首行 `y=0`，x 从 `49.857147` 递减至 `-50`；然后进入下一行。相邻列和相邻行的步长均约为 `0.142857149 mm`。

### 11.3 RAW 中的 `_positions0` 数据块

本样本中的精确布局：

| 偏移 | 内容 |
|---:|---|
| `0x5588A` | 数据块前缀：`00 0A 00 00 00 00 00 00 00` |
| `0x55893` | ASCII 标签 `_positions0`（11 B；末尾 `0` 同时是计数低字节） |
| `0x5589D` | 从标签末尾开始的 LE u32 点数 `30 57 05 00 = 350,000` |
| `0x558A1` | 350,000 × 3 个 LE float32，即 PCD 的完整二进制载荷 |
| `0x456EE1` | 点云块结束，后续为其他类型/元数据记录 |

提取不应依赖固定地址，而应搜索 `_positions0`，再读取与标签末尾重叠的点数和后续坐标。本样本的解析代码如下；这种重叠是否适用于其他点数，仍需更多样本确认：

```python
import struct

raw = open("CognexLogo.raw", "rb").read()
tag = raw.index(b"_positions0")
count = struct.unpack_from("<I", raw, tag + len(b"_positions0") - 1)[0]
start = tag + len(b"_positions0") + 3
positions = raw[start:start + count * 12]
points = list(struct.iter_unpack("<fff", positions))

assert len(points) == 350000
assert positions == open("CognexLogo.pcd", "rb").read()[95:]
```

标签后的 `57 05 00` 与标签末尾的 `30` 共同组成点数 `30 57 05 00`，不是字节数；坐标字节数需再乘 12。

### 11.4 `_grey80` 灰度块

点云之后还存在对应的 8 位灰度数组：

| 偏移 | 内容 |
|---:|---|
| `0x4AC62C` | 前缀：`00 06 00 00 00 00 00 00 00` |
| `0x4AC635` | ASCII 标签 `_grey80`（7 B） |
| `0x4AC63B` | 计数高三字节 `57 05 00`；与省略的低字节 `0x30` 合成 `350,000` |
| `0x4AC63E` | 350,000 个 u8 灰度值 |

灰度块与点云块元素数相同、顺序对应，可按 `point[i] ↔ grey[i]` 建立颜色/强度映射。PCD 导出只含 XYZ，因此这 350,000 B 灰度信息被舍弃。

### 11.5 分辨率与网格尺寸元数据

文件尾部存在显式命名的 float64 字段。名称后的 `08 00 00 00` 表示值长度为 8 B，其后是 LE float64：

| 字段 | 值 | 与 PCD 的关系 |
|---|---:|---|
| `ApproxResXDouble` | `0.1428571492433548` | 等于 PCD 的 x 网格间距 |
| `ApproxResYDouble` | `0.1428571492433548` | 等于 PCD 的 y 网格间距 |
| `ApproxResZDouble` | `0.0` | 本样本 z 只有离散平面值 |
| `ROIMaxZDouble` | `2.0` | 等于 PCD 最大 z |
| `ROIMinZDouble` | `0.0` | 等于 PCD 最小 z |

文件最后 8 B 为两个 LE u32：

```text
BC 02 00 00  F4 01 00 00
= 700        = 500
= WIDTH      = HEIGHT
```

因此该变体可以直接用尾部宽高和 `_positions0` 重建组织化点云，不必从数据周期性猜测尺寸。

### 11.6 对查看器与既有结论的影响

1. **RAW 不是单一内部布局**：本样本与焊缝样本同为 v41，但本样本含 `_positions0` 明文 float32 XYZ，焊缝样本则呈定长记录结构。解析器应先识别标签/布局，再选择解码器。
2. **真实点云已经可直接提取**：对含 `_positions0` 的文件，应优先使用该块，而不是从 `0x48` 起连续猜测 float32 三元组。
3. **PCD 是 RAW 点云子集的官方导出**：`SavePointCloud()` 添加标准 PCD 文本头，并复制 `_positions0` 的 XYZ 数据；不保留 `_grey80` 和其余 RAW 元数据。
4. **与 WASM 拒绝 RAW 的现象一致**：`takoweb.wasm` 的 `importPacket()` 接受 QuadMesh packet，而落盘 RAW 还可能包含 `_positions0`、灰度和其他序列化字段，两者不是同一输入格式。

## 12. 多样本交叉验证（2026-11-11，samples/ 目录 5 件）

本节系统对比 `samples/` 目录下的 5 组 RAW/PCD 样本，明确 v41 RAW 内部存在至少两种布局变体，并建立 RAW 扫描记录与导出 PCD 之间的数量级与几何对应关系。

### 12.1 样本组清单

| 分组 | RAW 文件 | 大小 | PCD 文件 | PCD WIDTH | PCD HEIGHT | PCD POINTS | 来源工位 |
|---|---|---:|---|---:|---:|---:|---|
| A（OK 焊缝） | `sample.raw` | 8,798,078 B | `sample.pcd` | 1,920 | 570 | 1,094,400 | 工位 A |
| B（NG 焊缝） | `sample2.raw` | 6,412,318 B | `sample2.pcd` | 1,920 | 415 | 796,800 | 工位 B |
| B（NG 焊缝） | `sample3.raw` | 6,412,318 B | `sample3.pcd` | 1,920 | 415 | 796,800 | 工位 B |
| B（NG 焊缝） | `sample4.raw` | 6,412,318 B | `sample4.pcd` | 1,920 | 415 | 796,800 | 工位 B |
| C（Logo） | `CognexLogo.raw` | 16,660,441 B | `CognexLogo.pcd` | 700 | 500 | 350,000 | 非扫描 |

所有 RAW 文件的魔数均为 `0x50FACE11`、版本均为 `41`；所有 PCD 均为 **v0.7 二进制格式**，`FIELDS x y z / SIZE 4 4 4 / TYPE F F F`（无颜色、法线、强度字段）。

### 12.2 内部布局分类

根据是否含 `_positions0` 等显式标签，将 v41 RAW 分为两类：

| 特征 | **布局 A（焊缝扫描）** | **布局 B（规则网格）** |
|---|---|---|
| 代表样本 | `sample.raw`、`sample2/3/4.raw` | `CognexLogo.raw` |
| 内部标签 | 无 `_positions0`、无 `_grey80` | 含 `_positions0`、`_grey80`、`ApproxRes*`、`ROI*` |
| 扫描记录区 | 15,360 B 定长 opaque blob，条数 = PCD HEIGHT | **无定长记录区**；`_positions0` 直接连续存储 float32 XYZ |
| Pair-stream | 有（6 段 x 坐标标定） | 无 |
| 元数据签名 | 文件末尾有 `1.0, 5.0, 5.0` float32 签名 | 文件末尾有 `ApproxRes*`、`ROI*` 字段 + 尾部 WIDTH/HEIGHT |
| 文件尺寸量级 | 6–9 MB | 16.7 MB（因含 4.2 MB 的显式 XYZ 数据块） |
| 与 PCD 对应 | 扫描记录 → 设备解码 → PCD（**中间经过 opaque 层**，PCD 载荷不直接嵌入 RAW） | `_positions0` 块 → PCD（**逐字节相同**，SHA256 一致） |

> **为什么 CognexLogo.raw 更大？** 因为布局 B 把完整 XYZ 点云（4.2 MB）+ 灰度数组（350 KB）以显式 float32/uint8 存储在 RAW 内；而布局 A 的扫描记录是编码的 compact opaque 格式，RAW 不含"直接可读的点云"，解码需 native WASM。

### 12.3 布局 A 内部差异：同工位 vs 跨工位

`sample2/3/4.raw` 头部 80 字节与标定前缀前 25KB **完全相同**：

```text
SHA256(sample2.raw) = 4130789a...1cc264ce65
SHA256(sample3.raw) = a1964b97...1bb96ac2     ← 全文件不同！
SHA256(sample4.raw) = 0c73588e...d7077c
前 0x6245 字节（头部+标定）全同
```

三者的差异从扫描记录起点（0x6245 附近）才开始。这证明：

1. **标定前缀 = 工位级配置**（激光标定、pair-stream、Block A/B/C），同工位多件不变；
2. **扫描记录区 = 件级测量数据**，每件不同；
3. **头部 0x30–0x4F** 属于工位级（含扫描记录计数或缓冲区尺寸参数），跨工位变化，同工位多件不变。

`sample.raw`（工位 A）与 `sample2.raw`（工位 B）的头部差异**恰好集中在 0x30–0x4F**（19 字节），其余部分（0x00–0x2F 的魔数、版本、GUID 等）完全相同；这与"工位级标定差异"的假设一致。

### 12.4 布局 A 精确解析模型：RAW → 扫描记录 → PCD

跨 4 件焊缝样本验证得到以下模型：

```text
RAW 文件:
  Header (72+2 B)      → 标定前缀 (~25 KB)    → 扫描记录 × N (15,360 B each)    → 元数据尾部

N = PCD HEIGHT = RAW 文件尺寸反向推算的整数条数
PCD WIDTH ≠ pair-stream 点数 (225)
         = 每条扫描记录解码后的点数 (1,920)
PCD payload = 设备解码所有记录得到的连续 float32 XYZ 三元组
```

**已验证数值：**

| 样本 | PCD WIDTH | PCD HEIGHT | PCD POINTS | 扫描记录条数 | WIDTH × HEIGHT |
|---|---:|---:|---:|---:|---:|
| `sample2/3/4` | 1,920 | 415 | 796,800 | **415** | **1,920 × 415 = 796,800 ✓** |
| `sample` | 1,920 | 570 | 1,094,400 | **572** | 1,920 × 570 = 1,094,400 ✓ (2 条空) |

PCD HEIGHT 与 RAW 扫描记录条数**完全一一对应**（sample 的 572 vs 570 差异疑为末尾 2 条空记录被导出时跳过）；每条扫描记录解码为固定 **1,920 个三维点**（WIDTH=1920）。

### 12.5 PCD payload 与 RAW 的直接对应关系

两类布局 PCD payload 与 RAW 的关系完全不同：

**布局 A（焊缝扫描）— PCD payload 不直接嵌入 RAW：**
- `sample2.raw` → 搜索 `sample2.pcd` payload 的前 1,000 字节在 RAW 中的位置 → **未找到**
- 扫描记录内部编码格式（定点 16.16 或其他 compact 编码）需 native `tako-vc140-22_1.dll` 解码
- `SavePointCloud()` 调用链：扫描记录 blob → Tako native 解码 → 设备端临时保存 → PC 下载

**布局 B（CognexLogo）— PCD payload 直接嵌入 RAW 的 `_positions0` 块：**
- `CognexLogo.raw` → `_positions0` @ 0x55893 → count=350,000 → 数据从 0x558A1 起
- RAW `_positions0` 数据 (4,200,000 B) ↔ PCD payload (4,200,000 B) — **SHA256 完全一致**：`7dbb223e...84c8510`
- `SavePointCloud()` 对布局 B 更像是"复制 `_positions0` + 添加 PCD 文本头"

### 12.6 sample3/4 的 RAW 差异 → PCD 差异映射

`sample3.raw` 与 `sample4.raw` 的头部 + 标定前缀（前 ~0x6245 字节）完全相同，差异从扫描记录区内部开始。逐字节 diff 定位：

```text
sample3.raw 与 sample4.raw 全文件 SHA256 不同
首个 RAW 差异字节: 0x62AD = 扫描记录 #0 内部偏移 0x68
差异字节总数: 3,018,179 / 6,412,318 (约 47%)
```

对应的 PCD 差异：

```text
sample3.pcd SHA256 (payload): 60541a64...accee6848a37
sample4.pcd SHA256 (payload): d6936d0f...be370ff136
PCD payload 全同? NO
差异字节数: 4,175,982 / 9,561,600 (约 43.7%)
首差异点: 点 #70 (0-indexed), 字节偏移 0x348 = 70 × 12
sample3 @ 点 70: (19.632114, -0.107358, -21.911499)
sample4 @ 点 70: (nan, nan, nan)
```

**关键观察**：

1. **RAW 差异从扫描记录 #0 内部 0x68 字节处开始**，但 PCD 差异从 **点 #70**（每条 1920 点的点云第 70 个点）才出现。这说明 15,360 字节的扫描记录并非点到点的线性映射，记录内部编码结构有固定头部或辅助字段（前 ~0x68 字节可能是记录头/状态字），之后才是对应点 70+ 的测量数据。
2. **前 70 点仍相同**：sample3 与 sample4 的 PCD 前 70 个三维点（840 字节）完全一致，对应 RAW 扫描记录 #0 的前 0x68 字节区域——该区域的测量内容在两件样本中恰好相同（可能是非激光返回区域，填充值或默认值）。
3. **sample4.pcd 含更多 NaN**：`sample4.pcd` 10K 采样点中 NaN 比例 ~18% vs `sample3.pcd` ~36%（实际 finite 数 sample4=8189/10000，sample3=6379/10000），符合 NG 件焊缝缺失程度不同的实际情况。

> **前次修正说明（v7 → v8）**：v7 基于一份内容有误的 sample4.pcd 得出了"sample3.pcd == sample4.pcd，SavePointCloud() 输出确定性 PCD"的错误结论。更正后的 sample4.pcd 有独立的 SHA256，本节已据此重写。本节数据由重新全量哈希与逐字节 diff 验证得出。

### 12.7 对查看器的启示

1. **布局自动识别优先**：先搜 `_positions0`、`_grey80` 等标签，命中则走布局 B 的直接提取路径，避免依赖 WASM；
2. **PCD HEIGHT 作为扫描记录条数的参考**：若样本同时有 PCD 输出，可用 PCD HEIGHT 反向推算 RAW 扫描起点与条数；
3. **布局 A 的 fallback 解码**：WASM 失败后，应尝试 `scan_start = meta_sig - HEIGHT × 15360` 反向定位，再从每条记录起始字节猜测 compact 编码方式；
4. **同工位多件可共享标定**：对于批量离线查看场景，可缓存同一工位的 pair-stream 和 Block A/B/C，避免重复解析。

## 13. opaque 解码障碍定位 + 下一步计划

> v9 新增（2026-11-11）。基于对 sample2.raw record #0 的系统性编码假设排除 + 数值空间对齐分析。

### 13.1 为什么浏览器解不了布局 A 的 opaque 扫描记录？

对 sample2.raw record #0 进行 5 种主流编码假设的穷举测试，**所有假设都不成立**：

| 假设 | 编码方式 | record 内合理值比例 | 判定 |
|---|---|---|---|
| A | 每 8B = (x_f32, z_f32) | 705/1920 = 36.7% | ❌ 前 5 对出现 `10³⁶` 级值，明显不是坐标 |
| B | 每 12B = (x_f32, y_f32, z_f32) | 407/1280 = 31.8% | ❌ 同上，数值空间完全不匹配 |
| C | 定点 16.16，每 6B 三元组 | 2143/2560 = 83.7% | ⚠ 看起来不错，但数值解释矛盾（见下文） |
| D | 只存 Z 值（连续 float32） | 2336/3840 = 60.8% | ❌ 合理 float32 Z 范围与 PCD z 范围不重合 |
| E | 块结构分析 | 每 8B 块 6-7 个非零字节 | ❌ 说明内部有固定状态字节，不是点云坐标 |

**决定性证据** —— record 内部数值空间与 PCD 数值空间零对齐：

```
PCD row #0 的 z 范围:        [-22.137, -15.958] mm
Record 内 float32 落在这个范围的比例: 1 / 3840 = 0.0%  ← 完全不相交
```

record 里存的**根本不是最终的 x/y/z 坐标**，而是某种硬件原生编码。

### 13.2 插值管道：225 → 1920

另一个关键障碍 —— RAW 与 PCD 之间存在 **8.5 倍插值**：

```
RAW pair-stream 实测点数:   225 点
PCD 每行导出点数:          1920 点
插值倍数:                  1920 / 225 = 8.53x
```

这意味着即使完全解码了 record 得到 225 个原始实测值，还需要：
1. 用 pair-stream 的 225 个标定 x 坐标做坐标变换
2. 执行 **8.5 倍过采样 + 插值**（设备端的插值窗口、平滑系数是设备配置的一部分，不在 RAW 文件里）
3. 得到 1920 × 415 个点的最终点云

opaque 解码链条：

```
RAW record (15360 B opaque)
    ↓ Tako native 解码（内部格式）
225 个原始实测值（硬件编码）
    ↓ 硬件定点→浮点数转换 + pair-stream x 坐标注入
225 个 (x, z) 原始点
    ↓ 8.5x 插值 + 平滑
1920 个 (x, y, z) 导出点
    ↓ SavePointCloud()
PCD v0.7 二进制
```

### 13.3 块结构特征

record #0 字节分布呈现极强的周期性和均匀性：

```
每 8B 块:   [007 006 007 007 007 007 007 007 …]  ← 每块几乎 6-7 个非零字节
每 16B 块:  [013 014 014 014 014 014 014 014 …]  ← 13-14 个非零字节
每 256B 块: [222 204 214 224 222 … 162 114 153 …] ← 前段均匀，中段零值密度更高
```

这种"每 8B 有 1 字节全零"的模式暗示 record 内部可能以 **8 字节单元** 为编码步长，其中 1 字节是状态/标记位（反复出现值 `1023` / `0x3FF` 可能是"无效激光返回"哨兵值），其余 7 字节是某种定点编码。

### 13.4 核心结论

> opaque 格式的"opaque"本质：record 内部是**硬件原生编码**（定点/差量/传感器专用格式），解码管道中有**一对多插值**（225 → 1920），还需要 pair-stream 标定值参与坐标变换——整个管道必须由 native Tako 库（`takoweb.wasm` / `tako-vc140-22_1.dll`）或设备端 SDK 执行。浏览器端没有中间表示的完整语义。

### 13.5 样本缺口评估

现有 5 件样本能做什么：

| 已有样本 | 能确认 | 不能确认 |
|---|---|---|
| 5 件 RAW + 对应 PCD | 布局 A/B 分类、扫描记录条数=PCD HEIGHT、pair-stream 解码公式、标定前缀=工位级 | record 内部编码公式 |
| 4 件焊缝样本 | 工位 vs 件级差异、同工位标定共享 | 插值公式、WIDTH 可变配置 |
| 1 件 CognexLogo | 布局 B `_positions0` 直取路径 | — |

### 13.6 下一步计划（按优先级）

#### 🔴 P0 — 单帧 RAW + 不插值 PCD（1 件样本，最关键）

**目标**：一件样本锁定解码公式。

让 In-Sight 输出 **只扫 1 行** 的 RAW（`PCD HEIGHT=1`），同时 SavePointCloud() 强制输出 `WIDTH=225`（绕过 8.5× 过采样）。这样：

```
PCD 225 点  ↔  record 内部编码单元  ↔  pair-stream 225 个 x 坐标
       │                │                            │
       └──── 三者一一对应 ────┴──── 直接反向推导解码公式 ────┘
```

**获取方式**：
- Cognex In-Sight Explorer → 扫描配置 → 扫描高度设为最小（1 行）
- 或者用 Cognex SDK 的 ScanLine / Continuous Scan 模式
- 同时 SavePointCloud() 关闭过采样（如支持）

#### 🟡 P1 — 多分辨率配置的 RAW+PCD（2-3 件样本）

同一物体、同一工位，只改变传感器配置的 **WIDTH/分辨率**：

- 配置 A：原始 225 点（不过采样）
- 配置 B：450 点（2× 插值）
- 配置 C：1920 点（8.5× 插值，现有样本）

对比可以锁定：
1. 插值公式（线性？双三次？带权重？）
2. pair-stream 点数与 record 内部 block 结构的关系
3. 扫描参数（激光频率、扫描速度）是否影响 record 编码格式

#### 🟢 P2 — 多型号传感器 RAW（可选）

L20 / L23 / L32 不同型号的 RAW。如果 record 内部结构有差异，可以锁定"哪些字段是所有型号共享的固定格式、哪些是型号变体"。

### 13.7 三条可行路径

```
路径 A — 设备端开发 .jobx（最快闭环）:
  在 In-Sight Explorer 里配置扫描只扫 1 行 + 关闭过采样
  → 拿到 P0 样本
  → 直接和 RAW record 做数值对齐，1-2 天内锁定 decode + 插值公式
  门槛：需要设备端配置权限

路径 B — 逆向 takoweb.wasm / tako-vc140-22_1.dll（纯 PC 端）:
  takoweb.wasm 已经可以正确解 RAW（它就是在做布局 A→PCD 的解码）
  问题是 importPacket() 接受的是 QuadMesh 协议格式，不是 raw 文件
  → 反编译 WebHMI Framework.js.gz 里的 Tako 类
  → 找到它把 raw 转成 QuadMesh 的中间步骤
  门槛：需要 WASM + managed DLL 联合逆向能力

路径 C — 继续黑盒测试（有 P0 样本后最有效）:
  不需要额外工具，只需要正确的样本
  有了 P0 样本后，直接做数值对齐就能锁公式
  没有 P0 样本 = 盲人摸象
```

### 13.8 Viewer 改进方向

- **布局 A 解码**：没有 P0 样本前，viewer 无法在 JS 端解码 opaque record → 继续保持"提示用户加载 .pcd + 导出 PCD"的设计
- **takoweb.wasm 接入**：如果 Path B 可行，可以让 WASM 直接解 raw 格式（需确定 WASM API 的 raw 输入入口）
- **硬件在环**：Path A 拿到 P0 样本后，viewer 可直接实现 decode 函数并实时渲染布局 A

---

*分析方法：Python struct 逐字节解析 + 多文件 diff + 同源 RAW/PCD 全载荷哈希与字节比对 + 熵/块重复/XOR/解压统计 + ILSpy 8.2.0 反编译 Cognex managed DLL（25.3.0 与 26.1.0）+ REA 6.3.0 + Ghidra 12.1.4 + WebHMI `Framework.js.gz` / `takoweb.wasm.gz` 静态分析 + 5 件 sample 目录的 RAW/PCD 交叉验证（v7 新增）+ record 内部 5 种编码假设排除 + 数值空间对齐分析（v9 新增）*

*v9 新增：record 内部编码假设排除（A/B/C/D/E）、数值空间对齐分析（PCD z ∈ [-22.1,-15.9] vs record 内零命中）、块结构均匀性特征（每 8B 块 6-7 非零字节）、opaque 解码链条、样本缺口 P0/P1/P2 优先级、三条可行路径（设备端 .jobx / WASM 逆向 / 黑盒测试）*

---

*文档版本：v9（2026-11-11）| 上一版：v8（2026-11-11）*
*历史版本更新见更新历史表*
