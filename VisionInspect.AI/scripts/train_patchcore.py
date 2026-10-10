

import torch

from anomalib.data import MVTecAD
from anomalib.models import Patchcore
from anomalib.engine import Engine

from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent

DATASET_DIR = PROJECT_ROOT / "datasets" / "MVTecAD"
OUTPUT_DIR = PROJECT_ROOT / "outputs" / "patchcore"



def main():
    print("=== VisionInspect PatchCore ===")

    device = "gpu" if torch.cuda.is_available() else "cpu"

    print(f"Device: {device}")

    # 1. 데이터셋 구성
    datamodule = MVTecAD(
        root=str(DATASET_DIR),
        category="transistor",
        train_batch_size=8,
        eval_batch_size=8,
        num_workers=0,
    )

    # 2. PatchCore 모델
    model = Patchcore(
        backbone="wide_resnet50_2",
        layers=["layer2", "layer3"],
        coreset_sampling_ratio=0.1,
        num_neighbors=9,
    )

    # 3. 학습 엔진
    engine = Engine(
        accelerator=device,
        devices=1,
        default_root_dir=str(OUTPUT_DIR),
        max_epochs=1,
    )

    # 4. 학습
    print("PatchCore 학습 시작")

    engine.fit(
        model=model,
        datamodule=datamodule,
    )

    # 5. 테스트
    print("성능 평가 시작")

    results = engine.test(
        model=model,
        datamodule=datamodule,
    )

    print("평가 결과:", results)


if __name__ == "__main__":
    main()
