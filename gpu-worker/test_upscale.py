
import cv2
import torch

from basicsr.archs.rrdbnet_arch import RRDBNet
from realesrgan import RealESRGANer

device = torch.device("xpu")

model = RRDBNet(
    num_in_ch=3,
    num_out_ch=3,
    num_feat=64,
    num_block=23,
    num_grow_ch=32,
    scale=4
)

upsampler = RealESRGANer(
    scale=4,
    model_path="models/RealESRGAN_x4plus.pth",
    model=model,
    tile=128,
    tile_pad=10,
    pre_pad=0,
    half=False,
    device=device
)

image = cv2.imread("input/test.png")

if image is None:
    raise FileNotFoundError("input/test.png 파일을 찾을 수 없습니다.")

print("GPU:", device)
print("원본 해상도:", image.shape[1], "x", image.shape[0])

output, _ = upsampler.enhance(image, outscale=4)

cv2.imwrite("output/test_upscaled.png", output)

print("결과 해상도:", output.shape[1], "x", output.shape[0])
print("업스케일링 완료!")
