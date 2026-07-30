#!/usr/bin/env bash
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
bash "$DIR/../scripts/publish-platform.sh" android Release 1.0.0
