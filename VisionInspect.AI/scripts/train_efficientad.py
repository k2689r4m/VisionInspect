
from pathlib import Path
from time import perf_counter

import torch

from anomalib.data import MVTecAD
from anomalib.engine import Engine
from anomalib.models import EfficientAd


ROOT = Path(__file__).resolve().parent.parent

DATASET_DIR = ROOT / "datasets" / "MVTecAD"
OUTPUT_DIR = ROOT / "outputs" / "efficientad"

CATEGORY = "transistor"

MAX_EPOCHS = 20
BATCH_SIZE = 1


def main():
    device = (
        "gpu"
        if torch.cuda.is_available()
        else "cpu"
    )

    print("=" * 60)
    print("VisionInspect - EfficientAD Training")
    print("=" * 60)

    print("Dataset :", DATASET_DIR)
    print("Category:", CATEGORY)
    print("Device  :", device)
    print("Epochs  :", MAX_EPOCHS)
    print("Output  :", OUTPUT_DIR)

    datamodule = MVTecAD(
        root=str(DATASET_DIR),
        category=CATEGORY,
        train_batch_size=BATCH_SIZE,
        eval_batch_size=BATCH_SIZE,
        num_workers=0,
    )

    model = EfficientAd(
        model_size="small",
    )

    engine = Engine(
        accelerator=device,
        devices=1,
        max_epochs=MAX_EPOCHS,
        default_root_dir=str(OUTPUT_DIR),
    )

    start = perf_counter()

    engine.fit(
        model=model,
        datamodule=datamodule,
    )

    training_time = (
        perf_counter() - start
    )

    print(
        f"\nTraining time: "
        f"{training_time:.2f} sec"
    )

    print("\nEvaluating EfficientAD...")

    results = engine.test(
        model=model,
        datamodule=datamodule,
    )

    print("\nTest results:")
    print(results)

    print("\nTraining completed.")


if __name__ == "__main__":
    main()
