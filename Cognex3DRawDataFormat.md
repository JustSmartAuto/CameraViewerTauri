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
| 0x00 | 4 | `0x50FACE11` | 魔数，全文件唯一 |
| 0x04 | 4 | `41` | 格式版本 |
| 0x08 | 8 | 0 | 保留 |
| 0x10 | 4 | `0x00010000` | 标志/子版本 |
| 0x14 | 4 | `0x87020000` | 未确认（跨件不变） |
| 0x18 | 8 | `0xDEC16784 0x00BF8D24` | 扫描器 GUID/序列 ID 片段 |
| 0x20 | 16 | 全 0 | 保留 |
| 0x30 | 16 | 见上文 | 疑似工位配置参数（含 0x3A=58、0xA0EC=41196） |
| 0x40 | 4 | `0xC19670D0` | float32 标定参数（-18.08） |
| 0x44 | 4 | `0xC1BE345C` | float32 标定参数（-0.1761） |

> **注**：旧版 v1 报告把 0x30-0x3F 的偏移和数值解读错误（`0x31`、`0x3B` 等不存在的偏移 + 错误值），现已修正为逐字节的 LE 小端解析。

### 2.2 标定前缀（0x48 – 约 0x6245）

工位级不变参考数据（多件比对完全相同，25 KB 量级）。内含：

- **Block A**（0x48 起）：标定 float32 三元组序列（米级小量，如 `(-1.80e-3, 0, -0.18), (-1.78e-3, 0, -0.18), …`），共 ~1,967 点，线性递减。
- **Pair-stream**（0x5C7C 起）：225 对 8 字节记录，6 段编码剖面 x 坐标（毫米级）。解码公式见第 3 节。
- **Block B/C**：其他标定 float32 流，含 1.0、5.0、50、570 等参数值。

### 2.3 扫描记录区（约 0x6245 – 0x85FAD8）

- 定长 **15,360 字节**（0x3C00）每条。
- OK 样本含 **572 条**，NG 样本（2026-10-23）含 **415 条** → 条数由视野/配置决定。
- 内部为编码测量值，opaque binary blob（ILSpy 确认 managed 层不解析内部结构，直接传给 native `tako-vc140-22_1.dll` 做渲染）。
- 记录间以周期性变化区 (~12.7 KB) + 填充区 (~2.6 KB) 交替。填充区为恒定小量（对应 NG 件焊缝缺失时"无有效返回"）。

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

---

*分析方法：Python struct 逐字节解析 + 多文件 diff + 熵/块重复/XOR/解压统计 + ILSpy 8.2.0 反编译 Cognex managed DLL（25.3.0 与 26.1.0）+ REA 6.3.0 + Ghidra 12.1.4*
