#!/usr/bin/env bash
# ============================================================
# build-bundle.sh  —  便携 bundle（内嵌 JRE + fat jar + launcher）
#
# 产物：Cognex相机日志服务器_{timestamp}.exe （单文件，含 JRE + 应用）
#
# 前置：Gradle fat jar 已构建（build-with-timestamp.sh）
#       系统安装 Zulu JDK 25 + dotnet SDK
# ============================================================
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
LAUNCHER="$ROOT/launcher/Launcher"
PAYLOAD="$LAUNCHER/payload"
STAMP="$(date +%Y%m%d%H%M%S)"
OUT_DIR="$ROOT/dist"

echo "==> Cognex 相机日志服务器 bundle 构建"
echo "    root:   $ROOT"
echo "    stamp:  $STAMP"

mkdir -p "$PAYLOAD" "$OUT_DIR"

# ---- 1. 找 Gradle fat jar ----
FAT_JAR="$(ls -t "$ROOT"/audit-log-server-*-fat.jar 2>/dev/null | head -1 || true)"
if [ -z "$FAT_JAR" ]; then
  echo "ERROR: fat jar not found. Run build-with-timestamp.sh first."
  exit 1
fi
echo "==> fat jar: $FAT_JAR"
cp "$FAT_JAR" "$PAYLOAD/app.jar"

# ---- 2. jlink 精简 JRE ----
if [ ! -f "$PAYLOAD/jre.zip" ]; then
  JLINK="${JLINK:-}"
  if [ -z "$JLINK" ]; then
    # 尝试常见位置
    for p in "/c/Program Files/Zulu/zulu-25/bin/jlink.exe" \
             "/usr/lib/jvm/zulu-25/bin/jlink" \
             "/opt/zulu-25/bin/jlink"; do
      if [ -x "$p" ]; then JLINK="$p"; break; fi
    done
  fi
  if [ -z "$JLINK" ]; then
    echo "ERROR: jlink not found. Install Zulu JDK 25 (https://adoptium.net/)."
    exit 1
  fi
  JDK_HOME="$(dirname "$(dirname "$JLINK")")"
  echo "==> jlink ($JLINK) → jre.zip"
  _TMP="$(mktemp -d)"
  "$JLINK" \
    --module-path "$JDK_HOME/jmods" \
    --add-modules java.desktop,java.logging,java.sql,java.prefs,java.instrument,java.naming,java.security.jgss \
    --strip-debug --no-man-pages \
    --output "$_TMP/jre"
  cd "$_TMP" && zip -qr "$PAYLOAD/jre.zip" jre && cd -
  rm -rf "$_TMP"
else
  echo "==> payload/jre.zip already exists, skipping jlink"
fi

# ---- 3. dotnet build ----
echo "==> dotnet build (net48 Release)"
cd "$LAUNCHER"
dotnet restore
dotnet build -c Release

# ---- 4. 复制产物 + 时间戳 ----
EXE="bin/Release/Cognex相机日志服务器.exe"
CFG="bin/Release/Cognex相机日志服务器.exe.config"
if [ ! -f "$EXE" ]; then echo "ERROR: launcher exe not built."; exit 1; fi

OUT_NAME="Cognex相机日志服务器_${STAMP}.exe"
cp "$EXE"  "$OUT_DIR/$OUT_NAME"
cp "$CFG"  "$OUT_DIR/${OUT_NAME}.config"

echo
echo "==> 完成 ✅"
echo "    $OUT_DIR/$OUT_NAME     ($(du -h "$OUT_DIR/$OUT_NAME" | cut -f1))"
echo "    可直接双击运行，内嵌 JRE + 应用，无需任何外部依赖"
