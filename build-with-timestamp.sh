#!/bin/bash
# Build script with timestamp suffix for Windows exe
set -e

PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"

# ---------------------------------------------------------------------------
# Ensure we are running in a Visual Studio MSVC x64 environment.
# In Git Bash the GNU /usr/bin/link shadows MSVC link.exe, and without the
# vcvars environment LIB/INCLUDE are missing, so we activate VS2022 here.
# ---------------------------------------------------------------------------
VCVARS_CACHE="/d/tmp/vcvars_env.txt"

if ! command -v cl >/dev/null 2>&1 || ! command -v link >/dev/null 2>&1 || \
   ! [ -f "$VCVARS_CACHE" ] || [ -n "$(find "$VCVARS_CACHE" -mmin +60 2>/dev/null)" ]; then
    echo "Activating Visual Studio 2022 x64 environment..."
    mkdir -p /d/tmp
    cat > /d/tmp/vcvars_env.cmd <<'EOF'
@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" x64
set
EOF
    cmd //c 'D:\tmp\vcvars_env.cmd' > /d/tmp/vcvars_env.txt
fi

# Save original msys PATH so bash utilities remain available
ORIGINAL_PATH="$PATH"

# Source Visual Studio environment variables.
# PATH is converted to msys style; LIB/INCLUDE/LIBPATH stay Windows style.
python_script=$(cat <<'PY'
import os, re
path = r'D:\tmp\vcvars_env.txt'
keys = {'PATH','LIB','INCLUDE','LIBPATH','VCINSTALLDIR','VCToolsInstallDir','VCToolsRedistDir','WindowsSdkDir','WindowsSdkBinPath','WindowsSdkVerBinPath','WindowsSdkIncludePath','WindowsSdkLibPath','UCRTVersion','UniversalCRTSdkDir'}

def to_msys(p):
    if len(p) >= 2 and p[1] == ':':
        p = '/' + p[0].lower() + p[2:]
    return p.replace('\\', '/')

with open(path, 'r', encoding='utf-8', errors='ignore') as f:
    for line in f:
        line = line.rstrip('\r\n')
        m = re.match(r'^([A-Za-z_][A-Za-z0-9_]*)=(.*)$', line)
        if not m:
            continue
        k, v = m.group(1), m.group(2)
        if k not in keys:
            continue
        if k == 'PATH':
            v = ':'.join(to_msys(x) for x in v.split(';') if x)
        print(f'export {k}={repr(v)}')
PY
)

eval "$(python -c "$python_script")"
export PATH="${PATH}${ORIGINAL_PATH:+:$ORIGINAL_PATH}"

# ---------------------------------------------------------------------------
# Ensure Tauri CLI is installed.
# ---------------------------------------------------------------------------
if ! cargo --list 2>/dev/null | grep -q tauri; then
    echo "Installing tauri-cli..."
    cargo install tauri-cli --locked
fi

# ---------------------------------------------------------------------------
# Build timestamped executable directly (no installer bundle).
# ---------------------------------------------------------------------------
cd "$PROJECT_ROOT/src-tauri"

TIMESTAMP=$(date +%Y%m%d%H%M)
echo "Building CameraViewerTauri with timestamp: $TIMESTAMP..."

cargo tauri build --no-bundle

OUTPUT_DIR="$(dirname "$PROJECT_ROOT")"
EXE_PATH="$PROJECT_ROOT/src-tauri/target/release/camera-viewer-tauri.exe"

if [ -f "$EXE_PATH" ]; then
    NEWNAME="CameraViewerTauri_${TIMESTAMP}.exe"
    cp "$EXE_PATH" "$OUTPUT_DIR/$NEWNAME"
    echo "Created: $OUTPUT_DIR/$NEWNAME"
    # 同时复制运行时依赖，确保 exe 可直接运行
    VCDLL_PATH="$PROJECT_ROOT/src-tauri/target/release/vcruntime140.dll"
    if [ -f "$VCDLL_PATH" ]; then
        cp "$VCDLL_PATH" "$OUTPUT_DIR/vcruntime140.dll"
        echo "Copied: $OUTPUT_DIR/vcruntime140.dll"
    fi
else
    echo "Error: executable not found at $EXE_PATH"
    exit 1
fi

echo "Build complete!"
