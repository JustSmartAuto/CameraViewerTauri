#!/bin/bash
# ============================================================
# CameraHelperJimJack 打包脚本（Git Bash 运行：./build-with-timestamp.sh）
# 流程：构建主程序 → 收集输出到 publish/app → 构建启动器（内嵌 .NET8 运行时安装包 + 主程序）
#       → 生成 dist/CameraHelperJimJack_<时间戳>.exe 单文件安装式启动器
# ============================================================
set -e
cd "$(dirname "$0")"

TIMESTAMP=$(date +%Y%m%d%H%M)
MAIN_PROJ="CameraHelper/CameraHelper.csproj"
LAUNCHER_PROJ="CameraHelper.Launcher/CameraHelper.Launcher.csproj"
PUBLISH_APP_DIR="publish/app"
LAUNCHER_EXE="CameraHelper.Launcher/bin/Release/net48/CameraHelper.Launcher.exe"
DIST_FILE="dist/CameraHelperJimJack_${TIMESTAMP}.exe"

echo "==> [1/4] 构建主程序 (net48 + AntdUI)..."
dotnet build "$MAIN_PROJ" -c Release

echo "==> [2/4] 收集主程序输出到 $PUBLISH_APP_DIR ..."
rm -rf "$PUBLISH_APP_DIR"
mkdir -p "$PUBLISH_APP_DIR"
cp -r CameraHelper/bin/Release/net48/. "$PUBLISH_APP_DIR/"

echo "==> [3/4] 构建启动器（内嵌主程序及依赖）..."
dotnet build "$LAUNCHER_PROJ" -c Release

echo "==> [4/4] 生成发布文件 $DIST_FILE ..."
mkdir -p dist
cp "$LAUNCHER_EXE" "$DIST_FILE"

SIZE=$(du -h "$DIST_FILE" | cut -f1)
echo ""
echo "打包完成: $DIST_FILE ($SIZE)"
echo "说明: 该 exe 为自包含启动器，工控机缺少 .NET8 桌面运行时时会自动静默安装；"
echo "      随后释放程序到 %LOCALAPPDATA%\\CameraHelperJimJack\\App 并启动。"
