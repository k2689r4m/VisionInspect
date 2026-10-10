
from __future__ import annotations

import io
import os
import time
import uuid
from contextlib import asynccontextmanager
from pathlib import Path
from threading import Lock

import cv2
import numpy as np
import torch

from PIL import Image

from fastapi import FastAPI, File, HTTPException, Query, UploadFile
from fastapi.staticfiles import StaticFiles

from anomalib.engine import Engine
from anomalib.models import Patchcore, Padim, EfficientAd
import json

from fastapi import HTTPException



ROOT = Path(__file__).resolve().parent.parent

RESULT_DIR = ROOT / "results"
RESULT_DIR.mkdir(parents=True, exist_ok=True)

CHECKPOINTS = {
    "patchcore": ROOT
    / "outputs/patchcore/Patchcore/MVTecAD/transistor/v0/"
      "weights/lightning/model.ckpt",

    "padim": ROOT
    / "outputs/padim/Padim/MVTecAD/transistor/v0/"
      "weights/lightning/model.ckpt",

    "efficientad": ROOT
    / "outputs/efficientad/EfficientAd/MVTecAD/transistor/v0/"
      "weights/lightning/model.ckpt",
}

MODEL_CLASSES = {
    "patchcore": Patchcore,
    "padim": Padim,
    "efficientad": EfficientAd,
}

MODEL_NAMES = {
    "patchcore": "PatchCore",
    "padim": "PaDiM",
    "efficientad": "EfficientAD",
}

MODELS = {}
INFERENCE_LOCK = Lock()


def load_models():
    device = "cuda" if torch.cuda.is_available() else "cpu"

    print(f"[VisionInspect] Device: {device}")

    for key, checkpoint in CHECKPOINTS.items():
        if not checkpoint.is_file():
            print(f"[WARNING] {key}: checkpoint not found: {checkpoint}")
            continue

        try:
            model = MODEL_CLASSES[key].load_from_checkpoint(
                str(checkpoint),
                map_location=device,
                weights_only=False,
            )

            model.eval()
            MODELS[key] = model

            print(f"[OK] Loaded {MODEL_NAMES[key]}")

        except Exception as exc:
            print(f"[ERROR] Failed to load {key}: {exc}")


@asynccontextmanager
async def lifespan(app: FastAPI):
    load_models()
    yield
    MODELS.clear()


app = FastAPI(
    title="VisionInspect AI API",
    version="1.3.0",
    lifespan=lifespan,
)

app.mount(
    "/results",
    StaticFiles(directory=str(RESULT_DIR)),
    name="results",
)


@app.get("/api/health")
def health():
    return {
        "status": "ok" if MODELS else "error",
        "loadedModels": [
            MODEL_NAMES[key] for key in MODELS
        ],
        "availableModels": list(MODEL_NAMES.values()),
        "device": "cuda" if torch.cuda.is_available() else "cpu",
    }


def to_numpy(value):
    if value is None:
        return None

    if isinstance(value, torch.Tensor):
        return value.detach().float().cpu().numpy()

    return np.asarray(value)


def scalar(value):
    if value is None:
        return None

    array = to_numpy(value)

    if array is None or array.size == 0:
        return None

    return float(array.reshape(-1)[0])


def extract_map(prediction):
    anomaly_map = to_numpy(
        getattr(prediction, "anomaly_map", None)
    )

    if anomaly_map is None:
        return None

    anomaly_map = np.squeeze(anomaly_map)

    if anomaly_map.ndim != 2:
        return None

    return anomaly_map.astype(np.float32)


def extract_threshold(model):
    post_processor = getattr(
        model, "post_processor", None
    )

    if post_processor is None:
        return None

    return scalar(
        getattr(post_processor, "image_threshold", None)
    )


def create_visualizations(
    original_rgb: np.ndarray,
    anomaly_map: np.ndarray | None,
    inspection_id: str,
):
    height, width = original_rgb.shape[:2]

    if anomaly_map is None:
        anomaly_map = np.zeros(
            (height, width),
            dtype=np.float32,
        )

    anomaly_map = cv2.resize(
        anomaly_map,
        (width, height),
        interpolation=cv2.INTER_LINEAR,
    )

    min_value = float(anomaly_map.min())
    max_value = float(anomaly_map.max())

    if max_value > min_value:
        normalized = (
            anomaly_map - min_value
        ) / (max_value - min_value)
    else:
        normalized = np.zeros_like(anomaly_map)

    heatmap_uint8 = (
        normalized * 255
    ).clip(0, 255).astype(np.uint8)

    heatmap_bgr = cv2.applyColorMap(
        heatmap_uint8,
        cv2.COLORMAP_JET,
    )

    original_bgr = cv2.cvtColor(
        original_rgb,
        cv2.COLOR_RGB2BGR,
    )

    overlay_bgr = cv2.addWeighted(
        original_bgr,
        0.6,
        heatmap_bgr,
        0.4,
        0,
    )

    # 시각화 전용 임계값.
    # 실제 모델의 OK/NG 판정 임계값과 다름.
    mask = (
        normalized >= 0.55
    ).astype(np.uint8) * 255

    kernel = np.ones((3, 3), np.uint8)

    mask = cv2.morphologyEx(
        mask,
        cv2.MORPH_OPEN,
        kernel,
    )

    contours, _ = cv2.findContours(
        mask,
        cv2.RETR_EXTERNAL,
        cv2.CHAIN_APPROX_SIMPLE,
    )

    defects = []
    result_bgr = original_bgr.copy()

    for contour in contours:
        x, y, w, h = cv2.boundingRect(contour)
        area = int(cv2.contourArea(contour))

        if area < 10:
            continue

        defects.append({
            "x": int(x),
            "y": int(y),
            "width": int(w),
            "height": int(h),
            "area": area,
        })

        cv2.rectangle(
            result_bgr,
            (x, y),
            (x + w, y + h),
            (0, 0, 255),
            2,
        )

    paths = {
        "heatmap": RESULT_DIR / f"{inspection_id}_heatmap.png",
        "overlay": RESULT_DIR / f"{inspection_id}_overlay.png",
        "result": RESULT_DIR / f"{inspection_id}_result.png",
    }

    cv2.imwrite(str(paths["heatmap"]), heatmap_bgr)
    cv2.imwrite(str(paths["overlay"]), overlay_bgr)
    cv2.imwrite(str(paths["result"]), result_bgr)

    outputs = {
        key: f"/results/{path.name}"
        for key, path in paths.items()
    }

    return defects, outputs


@app.post("/api/inspect")
async def inspect(
    file: UploadFile = File(...),
    model: str = Query(default="patchcore"),
):
    model_key = model.strip().lower()

    if model_key not in MODEL_CLASSES:
        raise HTTPException(
            status_code=400,
            detail=f"Unsupported model: {model}",
        )

    if model_key not in MODELS:
        raise HTTPException(
            status_code=503,
            detail=f"Model not loaded: {model_key}",
        )

    try:
        content = await file.read()

        with Image.open(io.BytesIO(content)) as image:
            original_rgb = np.array(
                image.convert("RGB")
            )

    except Exception as exc:
        raise HTTPException(
            status_code=400,
            detail=f"Invalid image: {exc}",
        )

    inspection_id = str(uuid.uuid4())

    input_path = RESULT_DIR / f"{inspection_id}_input.png"

    Image.fromarray(original_rgb).save(input_path)

    selected_model = MODELS[model_key]

    engine = Engine(
        accelerator=(
            "gpu"
            if torch.cuda.is_available()
            else "cpu"
        ),
        devices=1,
        default_root_dir=str(
            ROOT / "outputs" / "api_inference"
        ),
    )

    try:
        # 모델을 동시에 사용하는 요청 간 충돌 방지.
        with INFERENCE_LOCK:
            start = time.perf_counter()

            predictions = engine.predict(
                model=selected_model,
                data_path=str(input_path),
                return_predictions=True,
            )

            if torch.cuda.is_available():
                torch.cuda.synchronize()

            processing_time_ms = (
                time.perf_counter() - start
            ) * 1000

    except Exception as exc:
        raise HTTPException(
            status_code=500,
            detail=f"Inference failed: {exc}",
        )

    if not predictions:
        raise HTTPException(
            status_code=500,
            detail="No prediction returned",
        )

    prediction = predictions[0]

    score = scalar(
        getattr(prediction, "pred_score", None)
    )

    label = scalar(
        getattr(prediction, "pred_label", None)
    )

    threshold = extract_threshold(selected_model)

    if label is not None:
        result = "NG" if label >= 0.5 else "OK"
    elif threshold is not None and score is not None:
        result = "NG" if score >= threshold else "OK"
    else:
        raise HTTPException(
            status_code=500,
            detail="No prediction label or valid threshold",
        )

    anomaly_map = extract_map(prediction)

    defects, outputs = create_visualizations(
        original_rgb,
        anomaly_map,
        inspection_id,
    )

    return {
        "inspectionId": inspection_id,
        "modelName": MODEL_NAMES[model_key],
        "result": result,
        "anomalyScore": score,
        "imageThreshold": threshold,
        "pixelThreshold": scalar(
            getattr(
                getattr(selected_model, "post_processor", None),
                "pixel_threshold",
                None,
            )
        ),
        "processingTimeMs": round(processing_time_ms, 2),
        "defectCount": len(defects),
        "defects": defects,
        "outputs": outputs,
    }


@app.get("/api/benchmark/latest")
def get_latest_benchmark():
    benchmark_root = (
        ROOT / "outputs" / "benchmark"
    )

    if not benchmark_root.is_dir():
        raise HTTPException(
            status_code=404,
            detail="No benchmark results found",
        )

    files = sorted(
        benchmark_root.glob(
            "*/benchmark.json"
        ),
        key=lambda path: path.stat().st_mtime,
        reverse=True,
    )

    if not files:
        raise HTTPException(
            status_code=404,
            detail="No benchmark results found",
        )

    with files[0].open(
        "r",
        encoding="utf-8",
    ) as file:
        return json.load(file)
