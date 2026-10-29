#!/usr/bin/env bash
# ============================================================
# bundle-with-timestamp.sh  —  一键完整打包
#
# 流程：
#   1. Gradle fatJar → 重命名带时间戳后缀（build-with-timestamp.sh 逻辑）
#   2. jlink 精简 JRE → 内嵌 payload/jre.zip（已存在则跳过）
#   3. dotnet build net48 → Costura 把 JRE + app.jar merge 进 EXE
#   4. dist/Cognex相机日志服务器_{timestamp}.exe
#
# 前置：
#   - Zulu JDK 25（jlink）
#   - dotnet SDK 8+/9+（能 target net48）
#
# 用法：
#   ./bundle-with-timestamp.sh              # 完整流程
#   ./bundle-with-timestamp.sh --skip-jlink  # 复用已有的 payload/jre.zip
# ============================================================
set -euo pipefail

SKIP_JLINK=false
for arg in "$@"; do
  case "$arg" in
    --skip-jlink) SKIP_JLINK=true ;;
    -h|--help)    sed -n '2,20p' "$0"; exit 0 ;;
  esac
done

ROOT="$(cd "$(dirname "$0")" && pwd)"
STAMP="$(date +%Y%m%d%H%M%S)"

echo "============================================================"
echo " Cognex 相机日志服务器 — 一键打包"
echo " root:   $ROOT"
echo " stamp:  $STAMP"
echo "============================================================"

# --------------------------------------------------
# Step 1: Gradle fat jar + 时间戳重命名
# --------------------------------------------------
echo
echo "[1/4] Gradle fat jar + 时间戳重命名"

GRADLE_DIST="$ROOT/gradle-dist/gradle-9.7.1/bin/gradle"
if [ -x "$GRADLE_DIST" ]; then
  GRADLE_CMD="$GRADLE_DIST"
else
  GRADLE_CMD="gradle"
fi

"$GRADLE_CMD" -p "$ROOT" fatJar --no-daemon

# 找最新 fat jar（兼容 build/libs 和根目录两种位置）
JAR_FILE="$(find "$ROOT" -maxdepth 2 -name "audit-log-server-*-fat*.jar" -type f \
  -not -name "*sources*" -not -name "*javadoc*" \
  -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -n 1 | cut -d' ' -f2-)"

if [ -z "$JAR_FILE" ]; then
  echo "ERROR: fat jar 未找到"
  exit 1
fi
echo "  found: $JAR_FILE"

# 生成带毫秒的时间戳后缀 → yyyyMMddHHmmssfff
TS_LONG="$(date +"%Y%m%d%H%M%S%3N")"
BASENAME="$(basename "$JAR_FILE")"

# 剥离可能已存在的尾部 _时间戳 后缀，再追加新后缀
STEM="$(echo "$BASENAME" | sed -E 's/_[0-9]{13,}\.jar$/.jar/')"
# 若已无后缀（stem ≠ basename），把 stem 的 .jar 去掉再加新后缀
if [ "$STEM" = "$BASENAME" ]; then
  NEW_NAME="${BASENAME%.jar}_${TS_LONG}.jar"
else
  NEW_NAME="${STEM%.jar}_${TS_LONG}.jar"
fi

DEST="$ROOT/$NEW_NAME"
if [ "$JAR_FILE" != "$DEST" ]; then
  mv -f "$JAR_FILE" "$DEST"
fi
echo "  → $NEW_NAME"

# --------------------------------------------------
# Step 2: jlink 精简 JRE → payload/jre.zip
# --------------------------------------------------
echo
echo "[2/4] jlink 精简 JRE"

LAUNCHER="$ROOT/launcher/Launcher"
PAYLOAD="$LAUNCHER/payload"
mkdir -p "$PAYLOAD"

if [ -f "$PAYLOAD/jre.zip" ]; then
  if [ "$SKIP_JLINK" = true ]; then
    echo "  复用已有 $PAYLOAD/jre.zip"
  else
    echo "  $PAYLOAD/jre.zip 已存在，跳过 jlink（--skip-jlink 可显式指定）"
  fi
else
  JLINK=""
  for p in "/c/Program Files/Zulu/zulu-25/bin/jlink.exe" \
           "/c/Program Files/Eclipse Adoptium/jdk-25/bin/jlink.exe" \
           "/usr/lib/jvm/zulu-25/bin/jlink" \
           "/opt/zulu-25/bin/jlink"; do
    if [ -x "$p" ]; then JLINK="$p"; break; fi
  done
  if [ -z "$JLINK" ] && command -v jlink >/dev/null 2>&1; then
    JLINK="$(command -v jlink)"
  fi
  if [ -z "$JLINK" ]; then
    echo "ERROR: jlink 未找到。安装 Zulu JDK 25 (https://adoptium.net/)。"
    exit 1
  fi
  JDK_HOME="$(dirname "$(dirname "$JLINK")")"
  echo "  $JLINK"
  echo "  JDK_HOME=$JDK_HOME"

  _TMP="$(mktemp -d)"
  "$JLINK" \
    --module-path "$JDK_HOME/jmods" \
    --add-modules java.desktop,java.logging,java.sql,java.prefs,java.instrument,java.naming,java.security.jgss \
    --strip-debug --no-man-pages \
    --output "$_TMP/jre"
  cd "$_TMP" && zip -qr "$PAYLOAD/jre.zip" jre && cd -
  rm -rf "$_TMP"
  echo "  → payload/jre.zip ($(du -h "$PAYLOAD/jre.zip" | cut -f1))"
fi

# --------------------------------------------------
# Step 3: 复制 fat jar → payload/app.jar
# --------------------------------------------------
echo
echo "[3/4] fat jar → payload/app.jar"
cp "$DEST" "$PAYLOAD/app.jar"
echo "  $DEST → $PAYLOAD/app.jar"

# --------------------------------------------------
# Step 4: dotnet build + 时间戳拷贝 → dist/
# --------------------------------------------------
echo
echo "[4/4] dotnet build net48 (Costura merge)"

OUT_DIR="$ROOT/dist"
mkdir -p "$OUT_DIR"

( cd "$LAUNCHER" && dotnet restore && dotnet build -c Release )

EXE_SRC="$LAUNCHER/bin/Release/Cognex相机日志服务器.exe"
CFG_SRC="$LAUNCHER/bin/Release/Cognex相机日志服务器.exe.config"
if [ ! -f "$EXE_SRC" ]; then
  echo "ERROR: launcher exe 构建失败"
  exit 1
fi

OUT_NAME="Cognex相机日志服务器_${STAMP}.exe"
cp "$EXE_SRC" "$OUT_DIR/$OUT_NAME"
cp "$CFG_SRC" "$OUT_DIR/${OUT_NAME}.config"

SIZE_MB="$(du -m "$OUT_DIR/$OUT_NAME" | cut -f1)"

echo
echo "============================================================"
echo " ✅ 完成"
echo "    $OUT_DIR/$OUT_NAME    ($SIZE_MB MB)"
echo "    内嵌: JRE 25 + fat jar + net48 Launcher"
echo "    双击即可运行，零外部依赖"
echo "============================================================"
