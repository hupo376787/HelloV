#!/usr/bin/env bash
DIR="$(cd "$(dirname "$0")" && pwd)"
bash "$DIR/../scripts/publish-platform.sh" osx-x64 Release 1.0.0
STATUS=$?
echo
read -n 1 -s -r -p "Press any key to close..."
exit $STATUS
