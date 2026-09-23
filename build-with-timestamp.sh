#!/bin/bash
set -e

# 定位 Gradle：优先使用脚本所在目录下的本地 Gradle，否则使用系统 PATH
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ -x "$SCRIPT_DIR/gradle-dist/gradle-9.7.1/bin/gradle" ]; then
    GRADLE_CMD="$SCRIPT_DIR/gradle-dist/gradle-9.7.1/bin/gradle"
else
    GRADLE_CMD="gradle"
fi

# 构建 fat jar
echo "Building fat jar..."
"$GRADLE_CMD" fatJar

# 查找生成的 fat jar：优先当前目录，回退到 build/libs
JAR_FILE=$(find . -maxdepth 1 -name "camera-viewer-*-fat*.jar" -type f -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -n 1 | cut -d' ' -f2-)

if [ -z "$JAR_FILE" ]; then
    JAR_FILE=$(find build/libs -name "camera-viewer-*.jar" -type f -printf '%T@ %p\n' 2>/dev/null | sort -n | tail -n 1 | cut -d' ' -f2-)
fi

if [ -z "$JAR_FILE" ]; then
    echo "Error: Could not find generated jar file"
    exit 1
fi

# 生成时间戳后缀：yyyyMMddHHmmssfff（含毫秒）
TIMESTAMP=$(date +"%Y%m%d%H%M%S%3N")

# 构造新文件名
BASENAME=$(basename "$JAR_FILE")
if [[ "$BASENAME" =~ ^(camera-viewer-[^_]+-fat)_.*\.jar$ ]]; then
    NEW_NAME="${BASH_REMATCH[1]}_${TIMESTAMP}.jar"
else
    NEW_NAME="${BASENAME%.jar}_${TIMESTAMP}.jar"
fi

# 复制到本目录
echo "Copying $JAR_FILE -> $NEW_NAME"
cp "$JAR_FILE" "$NEW_NAME"

echo "Done: $NEW_NAME"
