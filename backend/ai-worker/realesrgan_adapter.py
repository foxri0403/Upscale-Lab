"""Local Real-ESRGAN x4plus inference (2x is its documented output rescale)."""
from __future__ import annotations

import argparse
from pathlib import Path
import sys


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--weights", required=True)
    parser.add_argument("--scale", type=int, choices=(2, 4), required=True)
    parser.add_argument("--tile", type=int, default=256)
    args = parser.parse_args()

    import cv2
    import torch
    # BasicSR 1.4.2 still imports torchvision's pre-0.17 module path.
    # Keep the upstream package usable with current CUDA/Python builds.
    try:
        import torchvision.transforms.functional_tensor  # noqa: F401
    except ModuleNotFoundError:
        from torchvision.transforms import functional
        sys.modules["torchvision.transforms.functional_tensor"] = functional
    from basicsr.archs.rrdbnet_arch import RRDBNet
    from realesrgan import RealESRGANer

    if not torch.cuda.is_available():
        raise RuntimeError("Real-ESRGAN requires an available CUDA GPU on this worker")
    weights = Path(args.weights).resolve(strict=True)
    model = RRDBNet(num_in_ch=3, num_out_ch=3, num_feat=64, num_block=23, num_grow_ch=32, scale=4)
    restorer = RealESRGANer(scale=4, model_path=str(weights), model=model, tile=args.tile,
                           tile_pad=10, pre_pad=0, half=True, gpu_id=0)
    source = cv2.imread(args.input, cv2.IMREAD_UNCHANGED)
    if source is None:
        raise ValueError("Cannot decode input image")
    result, _ = restorer.enhance(source, outscale=args.scale)
    if not cv2.imwrite(args.output, result):
        raise RuntimeError("Cannot save upscaled image")


if __name__ == "__main__":
    main()
