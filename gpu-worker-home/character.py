import io
import json
import os
import subprocess
import tempfile
import zipfile
from pathlib import Path

import torch
from PIL import Image, ImageOps
from psd_tools import PSDImage

from inference import layer_worker


MAX_INPUT_BYTES = 16 * 1024 * 1024
MAX_ARCHIVE_BYTES = 128 * 1024 * 1024
MAX_UNCOMPRESSED_BYTES = 300 * 1024 * 1024
PART_TYPES = {
    "background": "Background", "bg": "Background", "hairb": "BackHair", "hairf": "FrontHair",
    "back_hair": "BackHair", "front_hair": "FrontHair", "hair": "Hair", "face": "Face",
    "skin": "Body", "body": "Body", "tail": "BackPart",
    "wings": "BackPart", "mouth": "Mouth", "nose": "Nose", "eyebrow": "Eyebrow", "brow": "Eyebrow",
    "eyewear": "Accessory", "headwear": "Accessory", "earwear": "Accessory", "neckwear": "Accessory",
    "objects": "Accessory", "eye": "Eye", "iris": "Eye", "irides": "Eye", "ear": "Face", "neck": "Body",
    "topwear": "Clothes", "handwear": "Clothes", "bottomwear": "Clothes", "legwear": "Clothes",
    "footwear": "Clothes",
}


def part_type(name):
    name = name.lower().strip().replace(" ", "_").replace("-", "_")
    return next((kind for prefix, kind in PART_TYPES.items() if name.startswith(prefix)), "Other")


def archive_layers(psd_path, output_path, check):
    psd = PSDImage.open(psd_path)
    if not (1 <= psd.width == psd.height <= 1280):
        raise ValueError("Unexpected See-through canvas.")
    layers = []
    total_bytes = 0
    with zipfile.ZipFile(output_path, "w", zipfile.ZIP_DEFLATED) as archive:
        for layer in psd.descendants():
            check()
            if layer.is_group() or not layer.is_visible():
                continue
            pixels = layer.composite()
            if pixels is None:
                continue
            pixels = pixels.convert("RGBA")
            if not pixels.getchannel("A").getbbox():
                continue
            order = len(layers)
            if order == 64:
                raise ValueError("Too many See-through layers.")
            canvas = Image.new("RGBA", psd.size)
            canvas.paste(pixels, (layer.left, layer.top))
            content = layer_worker.png_bytes(canvas)
            total_bytes += len(content)
            if total_bytes > MAX_UNCOMPRESSED_BYTES:
                raise ValueError("See-through layers exceed the unpacked ZIP limit.")
            file = f"layers/{order:03d}.png"
            archive.writestr(file, content)
            layers.append({"file": file, "layerType": part_type(layer.name), "layerOrder": order})
        if not layers:
            raise ValueError("No See-through layers in the PSD.")
        for item in layers:
            depth = item["layerOrder"] / max(1, len(layers) - 1)
            item.update(depth=depth, positionX=0, positionY=0, rotation=0, scale=1,
                        movementX=4 + depth * 20, movementY=3 + depth * 16)
        archive.writestr("manifest.json", json.dumps({"layers": layers}))
    if output_path.stat().st_size > MAX_ARCHIVE_BYTES:
        raise ValueError("See-through archive exceeds the ZIP size limit.")


def create_processor(root, models):
    root = root.resolve()
    models = models.resolve()
    python = root / ".venv/Scripts/python.exe"
    script = root / "inference/scripts/inference_psd_quantized.py"
    environment = dict(os.environ, HF_HUB_DISABLE_PROGRESS_BARS="1",
                       MPLCONFIGDIR=str(root / ".venv/matplotlib"))
    weights = "unet/diffusion_pytorch_model.safetensors"
    for path in (python, script, *(models / name / weights for name in ("layerdiff", "depth"))):
        if not path.is_file():
            raise RuntimeError(f"Missing See-through runtime file: {path.name}")

    def process(data, check):
        source = layer_worker.signed_s3_url(data["input_url"])
        destination = layer_worker.signed_s3_url(data["output_url"])
        check()
        content = layer_worker.download(source, MAX_INPUT_BYTES)
        with tempfile.TemporaryDirectory(prefix="character-", dir=root.parent) as temporary:
            work = Path(temporary)
            input_path = work / "input.png"
            with Image.open(io.BytesIO(content)) as original:
                if original.width * original.height > 64_000_000:
                    raise ValueError("Character source exceeds the pixel limit.")
                image = ImageOps.exif_transpose(original).convert("RGBA")
                image.thumbnail((1280, 1280), Image.Resampling.LANCZOS)
                image.save(input_path)

            check()
            torch.cuda.empty_cache()
            command = [str(python), str(script), "--srcp", str(input_path), "--save_dir", str(work / "layers"),
                       "--save_to_psd", "--no_group_offload", "--resolution", "1024", "--resolution_depth", "640",
                       "--repo_id_layerdiff", str(models / "layerdiff"),
                       "--repo_id_depth", str(models / "depth")]
            with subprocess.Popen(command, cwd=root, env=environment, stdout=subprocess.DEVNULL,
                                  stderr=subprocess.DEVNULL) as child:
                try:
                    while child.poll() is None:
                        check()
                        try:
                            child.wait(timeout=1)
                        except subprocess.TimeoutExpired:
                            continue
                finally:
                    if child.poll() is None:
                        child.kill()
                        child.wait()
                if child.returncode != 0:
                    raise RuntimeError("See-through inference failed.")
            output = work / "layers.zip"
            archive_layers(work / "layers" / "input.psd", output, check)
            check()
            layer_worker.upload(destination, output.read_bytes(), "application/zip")
            return {"status": "completed"}

    return process
