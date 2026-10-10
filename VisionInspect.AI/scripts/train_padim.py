
from pathlib import Path
from time import perf_counter

import torch

from anomalib.data import MVTecAD
from anomalib.engine import Engine
from anomalib.models import Padim


ROOT = Path(__file__).resolve().parent.parent

DATASET_DIR = ROOT / "datasets" / "MVTecAD"
OUTPUT_DIR = ROOT / "outputs" / "padim"

CATEGORY = "transistor"


def main():
    device = "gpu" if torch.cuda.is_available() else "cpu"

    print("=" * 60)
    print("VisionInspect - PaDiM Training")
    print("=" * 60)
    print("Dataset :", DATASET_DIR)
    print("Category:", CATEGORY)
    print("Device  :", device)
    print("Output  :", OUTPUT_DIR)

    datamodule = MVTecAD(
        root=str(DATASET_DIR),
        category=CATEGORY,
        train_batch_size=8,
        eval_batch_size=8,
        num_workers=0,
    )

    model = Padim(
        backbone="resnet18",
        layers=["layer1", "layer2", "layer3"],
        n_features=100,
    )

    engine = Engine(
        accelerator=device,
        devices=1,
        max_epochs=1,
        default_root_dir=str(OUTPUT_DIR),
    )

    start = perf_counter()

    engine.fit(
        model=model,
        datamodule=datamodule,
    )

    training_time = perf_counter() - start

    print(f"\nTraining time: {training_time:.2f} sec")

    print("\nEvaluating PaDiM...")

    results = engine.test(
        model=model,
        datamodule=datamodule,
    )

    print("\nTest results:")
    print(results)

    print("\nTraining completed.")


if __name__ == "__main__":
    main()
