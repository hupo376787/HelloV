from __future__ import annotations

import argparse
import shutil
from pathlib import Path

import onnx
from ultralytics import YOLO


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkpoint", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--imgsz", type=int, default=640)
    args = parser.parse_args()

    checkpoint = Path(args.checkpoint).resolve()
    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)

    model = YOLO(str(checkpoint), task="detect")
    exported = Path(
        model.export(
            format="onnx",
            imgsz=args.imgsz,
            opset=17,
            simplify=True,
            dynamic=False,
        )
    ).resolve()

    if exported != output:
        shutil.copy2(exported, output)

    graph = onnx.load(str(output), load_external_data=False)
    onnx.checker.check_model(graph)
    if not graph.graph.node:
        raise RuntimeError("导出的 ONNX graph 没有计算节点。")
    if not graph.graph.output:
        raise RuntimeError("导出的 ONNX 没有输出节点。")

    dims = graph.graph.output[0].type.tensor_type.shape.dim
    known_dims = [dim.dim_value for dim in dims if dim.dim_value > 0]
    if 6 not in known_dims[-2:]:
        raise RuntimeError(
            "导出的模型不是 YOLOv10 end-to-end [x1,y1,x2,y2,score,class] 输出。"
            f"输出尺寸={known_dims}"
        )

    print(f"ONNX 已生成：{output}")


if __name__ == "__main__":
    main()
