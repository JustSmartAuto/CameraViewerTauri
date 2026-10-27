# Cognex 3D RAW File Viewer

Cognex 3D 激光轮廓传感器 `.raw` 点云文件的**离线单文件查看器**（three.js 内嵌，无需网络）。

## 使用

直接双击打开 `Cognex3DRawFileViewer.html`（Chrome / Edge 均可），将 `.raw` 文件拖入窗口即可。

- 左键旋转 / 右键平移 / 滚轮缩放，`俯视`/`复位视角` 按钮快速定位
- 解码模式：
  - `自动`（默认）：优先 float32 XYZ 三元组，点数过少时回退 16.16 定点解码
  - `float32 XYZ 三元组`：从指定偏移按 12 字节/点解析
  - `定点 16.16 扫描记录`：按 u32/65536 解析扫描记录
- 面板显示文件头信息（魔数、版本、参数、记录条数估算）
- 点云按高度蓝→红着色；超过"最大点数"时自动抽稀

## 重新构建单文件 HTML

修改 `viewer_template.html` 后执行：

```bash
python build.py
```

需先下载 three.js r147 到 `.build/`（`three.min.js` 与 `OrbitControls.js`）。

## 数据格式

见 [Cognex3DRawDataFormat.md](Cognex3DRawDataFormat.md)（魔数 `0x50FACE11`，版本 41，72 字节头 + 标定前缀 + 15,360 B 定长扫描记录 + 元数据尾部）。

## 隐私说明

- 仓库中不包含任何样本数据、GitHub Token 或本地绝对路径；
- 报告中的产品序列号与本地路径已脱敏。
