#!/usr/bin/env bash
# ============================================================
# CameraViewerLens 打包脚本（Git Bash 运行：./build-with-timestamp.sh）
# 主程序为 .NET Framework 4.7.2（Windows 自带），无需安装运行时；
# 启动器仅需内嵌主程序（zip），并负责检测/静默安装 WebView2 Evergreen 运行时。
# 流程：构建主程序 → 收集到 publish/app → 压缩为 launcher/app.zip
#       → 构建启动器（net48，内嵌 app.zip）→ 生成 dist/CameraViewerLens_<时间戳>.exe
# ============================================================
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT"

TIMESTAMP=$(date +%Y%m%d%H%M)
MAIN_PROJ="src/CameraHelper.csproj"
LAUNCHER_PROJ="launcher/CameraViewerLens.Launcher.csproj"
PUBLISH_APP_DIR="publish/app"
APP_ZIP="launcher/app.zip"
LAUNCHER_EXE="launcher/bin/Release/CameraViewerLens.Launcher.exe"
OUT="CameraViewerLens_${TIMESTAMP}.exe"

echo "==> [1/4] 构建主程序 (net472, x64)..."
rm -rf src/bin src/obj
dotnet build "$MAIN_PROJ" -c Release

echo "==> [2/4] 收集主程序输出到 $PUBLISH_APP_DIR 并压缩为 app.zip ..."
rm -rf "$PUBLISH_APP_DIR"
mkdir -p "$PUBLISH_APP_DIR"
cp -r src/bin/Release/net472/. "$PUBLISH_APP_DIR/"
cp -r publish/Configs "$PUBLISH_APP_DIR/Configs"
rm -f "$APP_ZIP"
powershell -NoProfile -Command "Compress-Archive -Path '$PUBLISH_APP_DIR/*' -DestinationPath '$APP_ZIP' -CompressionLevel Optimal"

echo "==> [3/4] 构建启动器 (.NET Framework 4.8, 内嵌 app.zip)..."
MSBUILD=""
for d in "/c/Program Files/Microsoft Visual Studio/2022"/* "/c/Program Files (x86)/Microsoft Visual Studio/2019"/*; do
  c="$d/MSBuild/Current/Bin/MSBuild.exe"
  [ -f "$c" ] && MSBUILD="$c" && break
done
[ -z "$MSBUILD" ] && MSBUILD="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe"
"$MSBUILD" "$LAUNCHER_PROJ" //p:Configuration=Release //v:m //nologo

echo "==> [4/4] 生成便携式单文件 dist/$OUT ..."
mkdir -p dist
cp "$LAUNCHER_EXE" "dist/$OUT"

SIZE=$(du -h "dist/$OUT" | cut -f1)
echo ""
echo "打包完成: dist/$OUT ($SIZE)"
echo "说明: 该 exe 为自包含启动器；首次运行时检测 WebView2 运行时（缺失则静默安装），"
echo "      随后释放主程序到 %LOCALAPPDATA%\\CameraViewerLens\\App 并启动。"
