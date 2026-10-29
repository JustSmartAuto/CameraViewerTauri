#!/usr/bin/env bash
# 已迁移：打包单文件 exe 的入口脚本为项目根目录的 bundle-exe-with-timestamp.sh。
# 本文件仅保留为兼容旧用法，转调根目录脚本。
set -euo pipefail
exec "$(cd "$(dirname "$0")/.." && pwd)/bundle-exe-with-timestamp.sh" "$@"
