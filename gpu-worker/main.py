
import io
import os
import hmac
import json
import zipfile
import threading

import cv2
import numpy as np
import torch

from fastapi import FastAPI, File, Form, Header, HTTPException, UploadFile
from fastapi.responses import Response
from basicsr.archs.rrdbnet_arch import RRDBNet
from realesrgan import RealESRGANer

app = FastAPI(title="Upscale-Lab Intel GPU Worker")

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

gpu_lock = threading.Lock()

MAX_UPLOAD_BYTES = 20 * 1024 * 1024
MAX_INPUT_PIXELS = 4_000_000
MAX_OUTPUT_PIXELS = 64_000_000


def verify_key(key):
    expected = os.environ.get("UPSCALE_WORKER_API_KEY", "")
    if len(expected) < 32:
        raise HTTPException(503, "Worker API key is not configured")
    if not hmac.compare_digest(key or "", expected):
        raise HTTPException(401, "Unauthorized")


def decode_image(data):
    if len(data) > MAX_UPLOAD_BYTES:
        raise HTTPException(413, "File exceeds 20 MB")

    image = cv2.imdecode(
        np.frombuffer(data, dtype=np.uint8),
        cv2.IMREAD_COLOR
    )

    if image is None:
        raise HTTPException(422, "Invalid image")

    height, width = image.shape[:2]

    if width * height > MAX_INPUT_PIXELS:
        raise HTTPException(413, "Image dimensions exceed limit")

    return image, width, height


def run_upscale(image, scale):
    with gpu_lock:
        output, _ = upsampler.enhance(image, outscale=scale)

    success, encoded = cv2.imencode(".png", output)

    if not success:
        raise RuntimeError("PNG encoding failed")

    return output, encoded.tobytes()


@app.get("/health")
def health(x_worker_key: str | None = Header(default=None)):
    verify_key(x_worker_key)
    return {
        "status": "ok",
        "gpu": torch.xpu.get_device_name(0),
        "xpu_available": torch.xpu.is_available(),
        "model": "RealESRGAN_x4plus"
    }


@app.post("/v1/process")
def process(
    file: UploadFile = File(...),
    operation: str = Form(...),
    engine: str = Form("see-through"),
    scale: int = Form(2),
    x_worker_key: str | None = Header(default=None)
):
    verify_key(x_worker_key)

    if operation != "upscale":
        raise HTTPException(422, "Only upscale is supported")

    if engine not in ("see-through", "partglee"):
        raise HTTPException(422, "Invalid engine")

    if scale not in (2, 4):
        raise HTTPException(422, "Scale must be 2 or 4")

    data = file.file.read(MAX_UPLOAD_BYTES + 1)
    image, width, height = decode_image(data)

    if width * height * scale * scale > MAX_OUTPUT_PIXELS:
        raise HTTPException(413, "Output dimensions exceed limit")

    try:
        output, png_bytes = run_upscale(image, scale)

        out_height, out_width = output.shape[:2]

        manifest = {
            "schemaVersion": 1,
            "operation": operation,
            "engine": engine,
            "width": width,
            "height": height,
            "imageFile": "upscaled.png",
            "imageWidth": out_width,
            "imageHeight": out_height,
            "layers": []
        }

        buffer = io.BytesIO()

        with zipfile.ZipFile(
            buffer, "w", compression=zipfile.ZIP_DEFLATED
        ) as archive:
            archive.writestr(
                "manifest.json",
                json.dumps(manifest)
            )
            archive.writestr("upscaled.png", png_bytes)

        return Response(
            content=buffer.getvalue(),
            media_type="application/zip"
        )

    except HTTPException:
        raise
    except Exception as error:
        print(f"GPU processing error: {error}")
        raise HTTPException(500, "GPU processing failed")
    finally:
        file.file.close()


@app.post("/upscale")
def upscale(
    file: UploadFile = File(...),
    x_worker_key: str | None = Header(default=None)
):
    verify_key(x_worker_key)
    data = file.file.read(MAX_UPLOAD_BYTES + 1)
    image, width, height = decode_image(data)

    if width * height * 16 > MAX_OUTPUT_PIXELS:
        raise HTTPException(413, "Output dimensions exceed limit")

    output, png_bytes = run_upscale(image, 4)

    return Response(content=png_bytes, media_type="image/png")
