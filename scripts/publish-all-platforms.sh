#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
VERSION="${2:-1.0.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
HOST_OS="$(uname -s)"

case "$HOST_OS" in
  Linux)
    TARGETS=(linux-x64 linux-arm64)
    ;;
  Darwin)
    TARGETS=(osx-arm64 osx-x64 ios-simulator)
    ;;
  *)
    echo "此脚本适用于 Linux 或 macOS；Windows 请运行 publish-all-platforms.ps1。" >&2
    exit 2
    ;;
esac

for target in "${TARGETS[@]}"; do
  bash "$ROOT/scripts/publish-platform.sh" "$target" "$CONFIGURATION" "$VERSION"
done

if [[ -n "${ANDROID_HOME:-}" ]]; then
  bash "$ROOT/scripts/publish-platform.sh" android "$CONFIGURATION" "$VERSION"
else
  echo "未检测到 ANDROID_HOME，跳过 Android。"
fi

if [[ "$HOST_OS" == "Darwin" \
   && -n "${HELLOV_IOS_CODESIGN_KEY:-}" \
   && -n "${HELLOV_IOS_PROVISION:-}" ]]; then
  bash "$ROOT/scripts/publish-platform.sh" ios "$CONFIGURATION" "$VERSION"
fi

echo
echo "全部可用目标已发布到：$ROOT/artifacts"
