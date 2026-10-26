import base64
import binascii
import os
import sys
import threading
import types

import cv2
import numpy as np
import runpod
import torch

# Compatibility patch for older BasicSR imports
import torchvision.transforms.functional as tv_functional

compat = types.ModuleType(
    "torchvision.transforms.functional_tensor"
)
compat.rgb_to_grayscale = tv_functional.rgb_to_grayscale

sys.modules.setdefault(
    "torchvision.transforms.functional_tensor",
    compat
)

from basicsr.archs.rrdbnet_arch import RRDBNet
from realesrgan import RealESRGANer


MODEL_PATH = "/app/models/RealESRGAN_x4plus.pth"

MAX_INPUT_BYTES = 2 * 1024 * 1024
MAX_INPUT_PIXELS = 1_000_000
MAX_OUTPUT_PIXELS = 4_000_000
MAX_OUTPUT_BYTES = 5 * 1024 * 1024

upsampler = None
gpu_lock = threading.Lock()


def load_model():
    global upsampler

    if upsampler is not None:
        return upsampler

    if not torch.cuda.is_available():
        raise RuntimeError("NVIDIA CUDA GPU is not available.")

    if not os.path.isfile(MODEL_PATH):
        raise RuntimeError("Real-ESRGAN model file is missing.")

    print("Loading Real-ESRGAN model...", flush=True)

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
        model_path=MODEL_PATH,
        model=model,
        tile=128,
        tile_pad=10,
        pre_pad=0,
        half=False,
        device=torch.device("cuda")
    )

    print(
        "Real-ESRGAN loaded on "
        + torch.cuda.get_device_name(0),
        flush=True
    )

    return upsampler


def upscale_image(image_base64, scale):
    if not isinstance(image_base64, str):
        raise ValueError("image_base64 must be a string.")

    if image_base64.startswith("data:"):
        if "," not in image_base64:
            raise ValueError("Invalid data URL.")
        image_base64 = image_base64.split(",", 1)[1]

    if len(image_base64) > 4 * MAX_INPUT_BYTES:
        raise ValueError("Input image is too large.")

    try:
        image_bytes = base64.b64decode(
            image_base64,
            validate=True
        )
    except (ValueError, binascii.Error):
        raise ValueError("Invalid base64 image.")

    if len(image_bytes) > MAX_INPUT_BYTES:
        raise ValueError("Input exceeds 2 MB limit.")

    image_array = np.frombuffer(image_bytes, np.uint8)
    image = cv2.imdecode(image_array, cv2.IMREAD_UNCHANGED)

    if image is None:
        raise ValueError("Cannot decode input image.")

    height, width = image.shape[:2]

    if width * height > MAX_INPUT_PIXELS:
        raise ValueError("Input pixel limit exceeded.")

    if width * height * scale * scale > MAX_OUTPUT_PIXELS:
        raise ValueError("Output pixel limit exceeded.")

    model = load_model()

    with gpu_lock:
        output, _ = model.enhance(
            image,
            outscale=scale
        )

    success, encoded = cv2.imencode(".png", output)

    if not success:
        raise RuntimeError("PNG encoding failed.")

    result_bytes = encoded.tobytes()

    if len(result_bytes) > MAX_OUTPUT_BYTES:
        raise ValueError(
            "Output exceeds test response size limit."
        )

    output_height, output_width = output.shape[:2]

    return {
        "status": "completed",
        "operation": "upscale",
        "model": "RealESRGAN_x4plus",
        "scale": scale,
        "width": output_width,
        "height": output_height,
        "image_base64": base64.b64encode(
            result_bytes
        ).decode("ascii")
    }


def handler(event):
    data = event.get("input", {})

    operation = data.get("operation", "test")

    if operation == "test":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA GPU is not available.")

        return {
            "status": "ok",
            "gpu": torch.cuda.get_device_name(0),
            "cuda_available": True,
            "model": "RealESRGAN_x4plus"
        }

    if operation != "upscale":
        raise ValueError("Unsupported operation.")

    scale = data.get("scale", 2)

    if type(scale) is not int or scale not in (2, 4):
        raise ValueError("Scale must be 2 or 4.")

    return upscale_image(
        data.get("image_base64"),
        scale
    )


if __name__ == "__main__":
    runpod.serverless.start({
        "handler": handler
    })