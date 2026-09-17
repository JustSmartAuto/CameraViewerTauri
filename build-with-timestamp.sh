#!/usr/bin/env bash
# 打包脚本：构建 WPF 主程序(单文件) -> 编译内嵌运行时安装包+主程序的 .NET4 启动器 -> 生成带时间戳的便携式单文件 exe
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
APP="$ROOT/CameraViewer"
LAUNCHER="$ROOT/CameraViewer.Launcher"
PUBLISH_APP="$ROOT/publish/app"
DIST="$ROOT/dist"
STAMP="$(date +%Y%m%d%H%M)"
OUT="CameraViewerDotnet_$STAMP.exe"

echo "==> [1/3] 发布主程序 (net8.0-windows, win-x64 单文件, 框架依赖)..."
rm -rf "$PUBLISH_APP"
dotnet publish "$APP/CameraViewer.csproj" -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_APP"

echo "==> [2/3] 编译启动器 (.NET Framework 4.8, 内嵌运行时安装包与主程序)..."
MSBUILD=""
for d in "/c/Program Files/Microsoft Visual Studio/2022"/* "/c/Program Files (x86)/Microsoft Visual Studio/2019"/*; do
  c="$d/MSBuild/Current/Bin/MSBuild.exe"
  [ -f "$c" ] && MSBUILD="$c" && break
done
[ -z "$MSBUILD" ] && MSBUILD="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe"
"$MSBUILD" "$LAUNCHER/CameraViewer.Launcher.csproj" //p:Configuration=Release //v:m //nologo

echo "==> [3/3] 输出便携式单文件..."
mkdir -p "$DIST"
cp "$LAUNCHER/bin/Release/CameraViewer.Launcher.exe" "$DIST/$OUT"
echo "完成: $DIST/$OUT"
