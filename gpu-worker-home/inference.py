import functools
import importlib.util
import io
import sys
from pathlib import Path

import cv2
import numpy as np
import torch
from PIL import Image, ImageOps


WORKERS = Path(__file__).resolve().parents[1] / "gpu-worker-runpod"
LAYER_MODELS = WORKERS / "layers" / "models"
UPSCALE_INPUT_BYTES = 20 * 1024 * 1024


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


sys.path.insert(0, str(WORKERS / "layers"))
layer_worker = load_module("runpod_layers_handler", WORKERS / "layers" / "handler.py")


@functools.cache
def upscale_worker():
    return load_module("runpod_upscale_handler", WORKERS / "handler.py")


def dimensions(width, height, long_edge):
    longest = max(width, height)
    return (max(1, (width * long_edge + longest // 2) // longest),
            max(1, (height * long_edge + longest // 2) // longest))


def upscale_png(content, target, check):
    with Image.open(io.BytesIO(content)) as source:
        if source.width * source.height > 64_000_000 or max(source.size) > 32768:
            raise ValueError("Input canvas exceeds the image limit.")
        image = ImageOps.exif_transpose(source)
        if target <= max(image.size):
            raise ValueError("Target resolution must exceed the input resolution.")
        output_size = dimensions(*image.size, target)
        has_alpha = "A" in image.getbands() or "transparency" in image.info
        image = image.convert("RGBA" if has_alpha else "RGB")

    # Real-ESRGAN allocates its native 4x output even when the final target is smaller.
    image.thumbnail((2048, 2048), Image.Resampling.LANCZOS)
    pixels = cv2.cvtColor(np.asarray(image), cv2.COLOR_RGBA2BGRA if has_alpha else cv2.COLOR_RGB2BGR)
    check()
    with torch.inference_mode():
        output, _ = upscale_worker().load_model().enhance(pixels, outscale=4)
    check()
    output = cv2.resize(output, output_size, interpolation=cv2.INTER_LANCZOS4)
    written, png = cv2.imencode(".png", output)
    if not written:
        raise RuntimeError("PNG encoding failed.")
    return png.tobytes(), output_size


def upscale(data, check):
    target = data.get("target_long_edge")
    scale = data.get("scale")
    if type(target) is not int or target not in (1920, 2560, 3840) or type(scale) is not int or scale not in (2, 4):
        raise ValueError("Expected an FHD, QHD or 4K upscale request.")
    source = layer_worker.signed_s3_url(data["input_url"])
    destination = layer_worker.signed_s3_url(data["output_url"])
    check()
    content = layer_worker.download(source, UPSCALE_INPUT_BYTES)
    png, (width, height) = upscale_png(content, target, check)
    check()
    layer_worker.upload(destination, png, "image/png")
    return {"status": "completed", "transfer": "s3", "width": width, "height": height}


def prepare_layers():
    for model_file in ("birefnet/config.json", "birefnet/model.safetensors", "big-lama/config.yaml",
                       "big-lama/models/best.ckpt", "lama-source/saicinpainting/__init__.py"):
        if not (LAYER_MODELS / model_file).is_file():
            raise RuntimeError(f"Missing layer model file: {model_file}")
    sys.path.insert(0, str(LAYER_MODELS / "lama-source"))


def layers(data, check):
    def download(url):
        check()
        return layer_worker.download(url)

    def upload(url, content, content_type):
        check()
        layer_worker.upload(url, content, content_type)

    return layer_worker.handler({"input": data}, download_input=download, upload_output=upload,
                                pipeline_factory=lambda: layer_worker.LayerPipeline(LAYER_MODELS))
