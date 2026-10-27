"""RunPod Serverless worker for inference using published See-through weights.

No training occurs. Large assets are transferred with short-lived S3 pre-signed URLs
provided by the trusted Upscale Lab backend, NOT in RunPod JSON responses.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from urllib.parse import urlparse
import zipfile

import requests
from PIL import Image, UnidentifiedImageError

MAX_INPUT_BYTES = 16 * 1024 * 1024
MAX_ZIP_BYTES = 128 * 1024 * 1024
MAX_IMAGE_PIXELS = 20_000_000


def _check_s3_url(value: str) -> str:
    """Only allow AWS S3 HTTPS URLs; reject arbitrary network targets."""
    if not isinstance(value, str):
        raise ValueError("S3 URL missing")
    url = urlparse(value)
    host = (url.hostname or "").lower()
    if (url.scheme != "https" or url.username or url.password or url.port or not url.query):
        raise ValueError("Only presigned HTTPS S3 URLs are permitted")
    if not host.endswith(".amazonaws.com") or not any(
        host == prefix or host.endswith("." + prefix)
        for prefix in ("s3.amazonaws.com", "s3-accelerate.amazonaws.com")
    ) and ".s3." not in host and not host.startswith("s3."):
        raise ValueError("Only AWS S3 hosts are permitted")
    return value


def _download_image(url: str, path: Path) -> None:
    with requests.get(url, stream=True, timeout=(10, 150), allow_redirects=False) as response:
        response.raise_for_status()
        length = 0
        with path.open("wb") as output:
            for chunk in response.iter_content(chunk_size=256 * 1024):
                length += len(chunk)
                if length > MAX_INPUT_BYTES:
                    raise ValueError("Source image exceeds the input size limit")
                output.write(chunk)
    if length == 0:
        raise ValueError("Source image is empty")


def _prepare_png(source: Path, destination: Path) -> None:
    Image.MAX_IMAGE_PIXELS = MAX_IMAGE_PIXELS
    try:
        with Image.open(source) as image:
            if image.width * image.height > MAX_IMAGE_PIXELS:
                raise ValueError("Source image resolution exceeds the limit")
            image.convert("RGB").save(destination, format="PNG")
    except (UnidentifiedImageError, OSError) as exc:
        raise ValueError("Invalid input image") from exc


def _create_output_archive(output_directory: Path, archive: Path) -> int:
    manifest = json.loads((output_directory / "manifest.json").read_text(encoding="utf-8"))
    layers = manifest.get("layers")
    if not isinstance(layers, list) or not 1 <= len(layers) <= 64:
        raise ValueError("See-through did not generate a valid layer manifest")
    paths = []
    for layer in layers:
        name = layer.get("file")
        if not isinstance(name, str) or not name.startswith("layers/") or not name.endswith(".png"):
            raise ValueError("See-through manifest has an invalid layer path")
        file_path = (output_directory / name).resolve()
        if output_directory.resolve() not in file_path.parents or not file_path.is_file():
            raise ValueError("See-through layer path is outside the output directory")
        paths.append((name, file_path))
    if len({name for name, _ in paths}) != len(paths):
        raise ValueError("Duplicate layer paths")

    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
        zipped.write(output_directory / "manifest.json", "manifest.json")
        for name, path in paths:
            zipped.write(path, name)
    if archive.stat().st_size > MAX_ZIP_BYTES:
        raise ValueError("Layer output archive exceeds size limit")
    return len(paths)


def handle(event: dict) -> dict:
    payload = event.get("input", {})
    if not isinstance(payload, dict) or payload.get("operation") != "character_layers_pretrained":
        return {"status": "failed", "message": "Unsupported inference operation"}
    try:
        input_url = _check_s3_url(payload.get("input_url"))
        output_url = _check_s3_url(payload.get("output_url"))
        repository = Path(os.environ.get("SEE_THROUGH_REPOSITORY", "/opt/see-through")).resolve()
        adapter = Path(os.environ.get("UPSCALE_LAB_ADAPTER", "/opt/worker/see_through_adapter.py")).resolve()
        if not (repository / "inference/scripts/inference_psd.py").is_file() or not adapter.is_file():
            return {"status": "failed", "message": "Pretrained See-through installation is missing"}

        with tempfile.TemporaryDirectory(prefix="upscale-lab-parts-") as tmp:
            root = Path(tmp)
            source = root / "source"
            png = root / "input.png"
            output = root / "result"
            archive = root / "layers.zip"
            _download_image(input_url, source)
            _prepare_png(source, png)
            subprocess.run(
                [sys.executable, str(adapter), "--repository", str(repository),
                 "--input", str(png), "--output", str(output)],
                cwd=str(repository), check=True,
                timeout=int(os.environ.get("SEE_THROUGH_INFERENCE_TIMEOUT_SECONDS", "1800")),
            )
            count = _create_output_archive(output, archive)
            with archive.open("rb") as stream:
                result = requests.put(
                    output_url, data=stream,
                    headers={"Content-Type": "application/octet-stream"},
                    timeout=(10, 240), allow_redirects=False,
                )
                result.raise_for_status()
            return {"status": "completed", "layer_count": count}
    except (ValueError, OSError, requests.RequestException, subprocess.CalledProcessError,
            subprocess.TimeoutExpired, json.JSONDecodeError, KeyError) as exc:
        # Never include signed URLs, raw exception text, or credentials in worker output.
        return {"status": "failed", "message": f"Pretrained layer inference failed ({type(exc).__name__})"}


if __name__ == "__main__":
    import runpod
    runpod.serverless.start({"handler": handle})
