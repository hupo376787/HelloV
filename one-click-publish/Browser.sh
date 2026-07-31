#!/usr/bin/env bash
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
bash "$DIR/../scripts/publish-platform.sh" browser Release 1.0.0
