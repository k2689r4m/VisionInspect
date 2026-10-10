
from __future__ import annotations

import argparse
import csv
import json
import math
import time
from datetime import datetime
from pathlib import Path

import torch

from anomalib.data import MVTecAD
from anomalib.engine import Engine
from anomalib.models import Patchcore, Padim, EfficientAd


ROOT = Path(__file__).resolve().parent.parent

DATASET_ROOT = ROOT / "datasets" / "MVTecAD"
OUTPUT_ROOT = ROOT / "outputs" / "benchmark"

MODEL_CONFIGS = {
    "patchcore": {
        "name": "PatchCore",
        "class": Patchcore,
        "checkpoint": (
            ROOT / "outputs" / "patchcore"
            / "Patchcore" / "MVTecAD" / "transistor"
            / "v0" / "weights" / "lightning" / "model.ckpt"
        ),
    },
    "padim": {
        "name": "PaDiM",
        "class": Padim,
        "checkpoint": (
            ROOT / "outputs" / "padim"
            / "Padim" / "MVTecAD" / "transistor"
            / "v0" / "weights" / "lightning" / "model.ckpt"
        ),
    },
    "efficientad": {
        "name": "EfficientAD",
        "class": EfficientAd,
        "checkpoint": (
            ROOT / "outputs" / "efficientad"
            / "EfficientAd" / "MVTecAD" / "transistor"
            / "v0" / "weights" / "lightning" / "model.ckpt"
        ),
    },
}


def as_float(value):
    if value is None:
        return None

    if isinstance(value, torch.Tensor):
        if value.numel() != 1:
            return None
        value = value.detach().cpu().item()

    try:
        result = float(value)
    except (TypeError, ValueError):
        return None

    return result if math.isfinite(result) else None


def build_datamodule(category: str):
    return MVTecAD(
        root=str(DATASET_ROOT),
        category=category,
        train_batch_size=1,
        eval_batch_size=1,
        num_workers=0,
    )


def evaluate_model(
    key: str,
    category: str,
    device: str,
    output_dir: Path,
):
    config = MODEL_CONFIGS[key]
    checkpoint = config["checkpoint"]

    if not checkpoint.is_file():
        raise FileNotFoundError(
            f"Checkpoint not found: {checkpoint}"
        )

    print()
    print("=" * 65)
    print(f"Evaluating {config['name']}")
    print("=" * 65)

    model = config["class"].load_from_checkpoint(
        str(checkpoint),
        map_location=(
            "cuda" if device == "gpu" else "cpu"
        ),
        weights_only=False,
    )

    model.eval()

    datamodule = build_datamodule(category)

    engine = Engine(
        accelerator=device,
        devices=1,
        default_root_dir=str(output_dir / key),
    )

    if device == "gpu":
        torch.cuda.synchronize()

    start = time.perf_counter()

    metrics = engine.test(
        model=model,
        datamodule=datamodule,
    )

    if device == "gpu":
        torch.cuda.synchronize()

    elapsed_ms = (
        time.perf_counter() - start
    ) * 1000.0

    if not metrics:
        raise RuntimeError(
            f"No test metrics returned for {key}"
        )

    metric = metrics[0]

    result = {
        "modelKey": key,
        "modelName": config["name"],
        "category": category,
        "imageAUROC": as_float(
            metric.get("image_AUROC")
        ),
        "imageF1": as_float(
            metric.get("image_F1Score")
        ),
        "pixelAUROC": as_float(
            metric.get("pixel_AUROC")
        ),
        "pixelF1": as_float(
            metric.get("pixel_F1Score")
        ),
        "evaluationTimeMs": round(elapsed_ms, 2),
        "status": "success",
        "error": None,
    }

    print(
        f"[{config['name']}] "
        f"Image AUROC: {result['imageAUROC']}"
    )

    return result


def save_csv(results: list[dict], path: Path):
    columns = [
        "modelKey",
        "modelName",
        "category",
        "imageAUROC",
        "imageF1",
        "pixelAUROC",
        "pixelF1",
        "evaluationTimeMs",
        "status",
        "error",
    ]

    with path.open(
        "w",
        newline="",
        encoding="utf-8-sig",
    ) as file:
        writer = csv.DictWriter(
            file,
            fieldnames=columns,
        )
        writer.writeheader()
        writer.writerows(results)


def run_benchmark(
    category: str = "transistor",
    model_keys: list[str] | None = None,
):
    if model_keys is None:
        model_keys = list(MODEL_CONFIGS.keys())

    unknown = [
        key for key in model_keys
        if key not in MODEL_CONFIGS
    ]

    if unknown:
        raise ValueError(
            f"Unknown models: {unknown}"
        )

    dataset_dir = DATASET_ROOT / category

    if not dataset_dir.is_dir():
        raise FileNotFoundError(
            f"Dataset not found: {dataset_dir}"
        )

    timestamp = datetime.now().strftime(
        "%Y%m%d_%H%M%S"
    )

    output_dir = (
        OUTPUT_ROOT / f"{category}_{timestamp}"
    )
    output_dir.mkdir(
        parents=True,
        exist_ok=True,
    )

    device = (
        "gpu" if torch.cuda.is_available() else "cpu"
    )

    results = []

    for key in model_keys:
        try:
            result = evaluate_model(
                key=key,
                category=category,
                device=device,
                output_dir=output_dir,
            )

        except Exception as exc:
            result = {
                "modelKey": key,
                "modelName": MODEL_CONFIGS[key]["name"],
                "category": category,
                "imageAUROC": None,
                "imageF1": None,
                "pixelAUROC": None,
                "pixelF1": None,
                "evaluationTimeMs": None,
                "status": "failed",
                "error": str(exc),
            }

            print(f"[ERROR] {key}: {exc}")

        results.append(result)

    report = {
        "benchmarkId": timestamp,
        "dataset": "MVTec AD",
        "category": category,
        "device": device,
        "createdAt": datetime.now().isoformat(),
        "results": results,
    }

    json_path = output_dir / "benchmark.json"
    csv_path = output_dir / "benchmark.csv"

    with json_path.open(
        "w",
        encoding="utf-8",
    ) as file:
        json.dump(
            report,
            file,
            indent=2,
            ensure_ascii=False,
        )

    save_csv(results, csv_path)

    print()
    print("=" * 65)
    print("Benchmark completed")
    print(f"JSON: {json_path}")
    print(f"CSV : {csv_path}")
    print("=" * 65)

    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()

    parser.add_argument(
        "--category",
        default="transistor",
    )

    parser.add_argument(
        "--models",
        nargs="+",
        choices=list(MODEL_CONFIGS.keys()),
        default=list(MODEL_CONFIGS.keys()),
    )

    args = parser.parse_args()

    run_benchmark(
        category=args.category,
        model_keys=args.models,
    )
