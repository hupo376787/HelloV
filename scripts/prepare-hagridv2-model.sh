#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TOOLS="$ROOT/.model-tools"
VENV="$TOOLS/venv"
CHECKPOINT="$TOOLS/YOLOv10n_gestures.pt"
MODEL_DIR="$ROOT/Models"
OUTPUT="$MODEL_DIR/YOLOv10n_gestures.onnx"
URL="https://rndml-team-cv.obs.ru-moscow-1.hc.sbercloud.ru/datasets/hagrid_v2/models/YOLOv10n_gestures.pt"

mkdir -p "$TOOLS" "$MODEL_DIR"
if [[ ! -f "$CHECKPOINT" ]]; then
  echo "正在下载 HaGRIDv2 官方 YOLOv10n 手势模型…"
  curl -fL "$URL" -o "$CHECKPOINT"
fi

if [[ ! -d "$VENV" ]]; then
  python3 -m venv "$VENV"
fi

"$VENV/bin/python" -m pip install --upgrade pip
"$VENV/bin/python" -m pip install "ultralytics>=8.3,<9" "onnx>=1.16" "onnxslim>=0.1.50"
"$VENV/bin/python" "$ROOT/scripts/export-hagridv2-onnx.py" \
  --checkpoint "$CHECKPOINT" \
  --output "$OUTPUT" \
  --imgsz 640

echo "Desktop / Android / iOS 共用模型：$OUTPUT"
