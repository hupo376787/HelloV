#!/usr/bin/env bash
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
bash "$DIR/../scripts/publish-platform.sh" linux-arm64 Release 1.0.0
