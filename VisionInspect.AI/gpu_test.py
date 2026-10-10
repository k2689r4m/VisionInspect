
import torch

print("PyTorch:", torch.__version__)
print("CUDA Runtime:", torch.version.cuda)
print("CUDA Available:", torch.cuda.is_available())

if torch.cuda.is_available():
    print("GPU:", torch.cuda.get_device_name(0))
    print("Compute Capability:",
          torch.cuda.get_device_capability(0))

    x = torch.randn(1000, 1000, device="cuda")
    y = torch.matmul(x, x)

    torch.cuda.synchronize()

    print("GPU 연산 성공:", y.shape)
else:
    print("CUDA GPU를 사용할 수 없습니다.")
