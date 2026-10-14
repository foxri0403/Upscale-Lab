"""Run configured local model environments; requests cannot choose commands or URLs."""
from __future__ import annotations

import asyncio
import json
import logging
import os
from pathlib import Path
import signal
import subprocess
import zipfile

from PIL import Image, ImageOps, UnidentifiedImageError
from worker_config import Settings

ADAPTERS = Path(__file__).resolve().parent
logger = logging.getLogger(__name__)


class InputError(ValueError):
    pass


class ConfigurationError(RuntimeError):
    pass


def normalize_image(source: Path, target: Path, settings: Settings) -> tuple[int, int]:
    try:
        with Image.open(source) as image:
            if image.width * image.height > settings.max_input_pixels:
                raise InputError("Input pixel limit exceeded")
            if getattr(image, "n_frames", 1) != 1:
                raise InputError("Only single-frame images are supported")
            normalized = ImageOps.exif_transpose(image).convert("RGBA")
            normalized.save(target, "PNG")
            return normalized.size
    except (UnidentifiedImageError, OSError, Image.DecompressionBombError) as error:
        raise InputError("Unsupported or invalid image") from error


def required_file(value: str, label: str) -> str:
    if not value or not Path(value).is_file():
        raise ConfigurationError(f"Configure {label} on the GPU host")
    return str(Path(value).resolve())


def repository(value: str, marker: str) -> Path:
    if not value or not (Path(value) / marker).is_file():
        raise ConfigurationError(f"Repository is missing {marker}")
    return Path(value).resolve()


async def run_model(command: list[str], cwd: Path, log_path: Path) -> None:
    env = dict(os.environ)
    env["PYTHONPATH"] = str(cwd) + os.pathsep + env.get("PYTHONPATH", "")
    kwargs = {"creationflags": subprocess.CREATE_NEW_PROCESS_GROUP} if os.name == "nt" else {"start_new_session": True}
    with log_path.open("wb") as log:
        process = await asyncio.create_subprocess_exec(*command, cwd=cwd, env=env, stdout=log, stderr=log, **kwargs)
        try:
            if await process.wait() != 0:
                with log_path.open("rb") as failure:
                    failure.seek(max(0, log_path.stat().st_size - 4000))
                    logger.error("Model failed: %s", failure.read().decode("utf-8", errors="replace"))
                raise RuntimeError("Local model inference failed; check worker logs")
        except asyncio.CancelledError:
            if process.returncode is None:
                if os.name == "nt":
                    killer = await asyncio.create_subprocess_exec("taskkill", "/PID", str(process.pid), "/T", "/F",
                                                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                    await killer.wait()
                else:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                await process.wait()
            raise


async def process_image(settings: Settings, workspace: Path, operation: str, engine: str, scale: int) -> Path:
    normalized = workspace / "input.png"
    width, height = normalize_image(workspace / "upload", normalized, settings)
    output = workspace / "output"
    output.mkdir()
    source = normalized
    image_width = image_height = 0
    async with asyncio.timeout(settings.timeout_seconds):
        if operation != "layers":
            if width * height * scale * scale > settings.max_output_pixels:
                raise InputError("Upscaled image would exceed the output pixel limit")
            repo = repository(settings.realesrgan_repository, "realesrgan/__init__.py")
            weights = required_file(settings.realesrgan_weights, "REALESRGAN_WEIGHTS (RealESRGAN_x4plus.pth)")
            source = output / "upscaled.png"
            await run_model([settings.realesrgan_python, str(ADAPTERS / "realesrgan_adapter.py"),
                             "--input", str(normalized), "--output", str(source), "--weights", weights,
                             "--scale", str(scale), "--tile", str(settings.realesrgan_tile)], repo, workspace / "upscale.log")
            with Image.open(source) as image:
                image_width, image_height = image.size
            if (image_width, image_height) != (width * scale, height * scale):
                raise RuntimeError("Unexpected upscale dimensions")
        if operation != "upscale":
            if engine == "see-through":
                repo = repository(settings.see_through_repository, "inference/scripts/inference_psd.py")
                command = [settings.see_through_python, str(ADAPTERS / "see_through_adapter.py"), "--repository", str(repo),
                           "--input", str(source), "--output", str(output), "--resolution", str(settings.see_through_resolution)]
                if settings.see_through_group_offload:
                    command.append("--group-offload")
            else:
                repo = repository(settings.partglee_repository, "projects/PartGLEE/partglee/PartGLEE.py")
                weights = required_file(settings.partglee_weights, "PARTGLEE_WEIGHTS")
                config = required_file(str(repo / settings.partglee_config), "PARTGLEE_CONFIG")
                command = [settings.partglee_python, str(ADAPTERS / "partglee_adapter.py"), "--input", str(source),
                           "--output", str(output), "--config", config, "--weights", weights,
                           "--threshold", str(settings.partglee_threshold), "--max-dimension", str(settings.partglee_max_dimension)]
            await run_model(command, repo, workspace / "layers.log")
            manifest = json.loads((output / "manifest.json").read_text(encoding="utf-8"))
        else:
            manifest = {"schemaVersion": 1, "width": image_width, "height": image_height, "layers": []}
    manifest.update(operation=operation, engine=engine)
    if operation != "layers":
        manifest.update(imageFile="upscaled.png", imageWidth=image_width, imageHeight=image_height)
    (output / "manifest.json").write_text(json.dumps(manifest, allow_nan=False), encoding="utf-8")
    return package_result(output, workspace / "result.zip", manifest, settings)


def package_result(output: Path, target: Path, manifest: dict, settings: Settings) -> Path:
    layers = manifest.get("layers", [])
    if len(layers) > 64 or (manifest["operation"] != "upscale" and not layers):
        raise RuntimeError("Invalid layer count")
    files = [("manifest.json", None)]
    files.extend((layer["file"], (manifest["width"], manifest["height"])) for layer in layers)
    if "imageFile" in manifest:
        files.append((manifest["imageFile"], (manifest["imageWidth"], manifest["imageHeight"])))
    names = set()
    total = 0
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_STORED) as archive:
        for name, dimensions in files:
            file = (output / name).resolve()
            if name in names or not file.is_relative_to(output.resolve()) or not file.is_file():
                raise RuntimeError("Invalid artifact path")
            names.add(name)
            if dimensions:
                if dimensions[0] <= 0 or dimensions[1] <= 0 or dimensions[0] * dimensions[1] > settings.max_output_pixels:
                    raise RuntimeError("Output pixel limit exceeded")
                with Image.open(file) as image:
                    if image.format != "PNG" or image.size != dimensions:
                        raise RuntimeError("Output canvas mismatch")
                    image.verify()
            total += file.stat().st_size
            if total > settings.max_archive_bytes - 65536:
                raise RuntimeError("Output archive limit exceeded")
            archive.write(file, name)
    return target
