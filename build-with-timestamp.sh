#!/usr/bin/env bash
set -euo pipefail

# 编译并生成单文件 exe, 复制到项目根目录并添加时间戳后缀

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

TIMESTAMP="$(date +%Y%m%d_%H%M%S)"
EXE_NAME="nomacs.exe"
OUTPUT_NAME="nomacs_${TIMESTAMP}.exe"

BUILD_DIR="bin/builds/$TIMESTAMP"

echo "=== 开始编译: dotnet publish ==="
dotnet publish -c Release -r win-x64 --nologo \
  -p:BaseOutputPath="$BUILD_DIR/"

PUBLISH_DIR="$BUILD_DIR/Release/net8.0-windows/win-x64/publish"
EXE_PATH="$PUBLISH_DIR/$EXE_NAME"

if [[ ! -f "$EXE_PATH" ]]; then
  echo "错误: 找不到编译产物 $EXE_PATH" >&2
  exit 1
fi

cp "$EXE_PATH" "./$OUTPUT_NAME"

echo "=== 完成: $OUTPUT_NAME ($(du -h "$OUTPUT_NAME" | cut -f1)) ==="
