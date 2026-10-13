"""Configuration belongs to the GPU host; API requests never supply executable paths."""
from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path
import sys


@dataclass(frozen=True)
class Settings:
    api_key: str = ""
    work_dir: Path = Path("workspace/jobs")
    timeout_seconds: int = 1800
    max_upload_bytes: int = 20 * 1024 * 1024
    max_input_pixels: int = 16_000_000
    max_output_pixels: int = 64_000_000
    max_archive_bytes: int = 256 * 1024 * 1024
    realesrgan_repository: str = ""
    realesrgan_python: str = sys.executable
    realesrgan_weights: str = ""
    realesrgan_tile: int = 256
    partglee_repository: str = ""
    partglee_python: str = sys.executable
    partglee_weights: str = ""
    partglee_config: str = "projects/PartGLEE/configs/Inference/RN50/paco.yaml"
    partglee_threshold: float = 0.5
    partglee_max_dimension: int = 1280
    see_through_repository: str = ""
    see_through_python: str = sys.executable
    see_through_resolution: int = 1280
    see_through_group_offload: bool = False

    @classmethod
    def from_env(cls) -> "Settings":
        defaults = cls()
        values = {}
        for name in cls.__dataclass_fields__:
            value = os.getenv("UPSCALE_WORKER_" + name.upper())
            if value is None:
                continue
            default = getattr(defaults, name)
            if isinstance(default, bool):
                if value.lower() not in ("true", "false"):
                    raise ValueError(f"{name} must be true or false")
                values[name] = value.lower() == "true"
            elif isinstance(default, int):
                values[name] = int(value)
            elif isinstance(default, float):
                values[name] = float(value)
            elif isinstance(default, Path):
                values[name] = Path(value)
            else:
                values[name] = value
        settings = cls(**values)
        for name in ("timeout_seconds", "max_upload_bytes", "max_input_pixels", "max_output_pixels", "max_archive_bytes", "realesrgan_tile", "partglee_max_dimension", "see_through_resolution"):
            if getattr(settings, name) <= 0:
                raise ValueError(f"{name} must be positive")
        if not 0 < settings.partglee_threshold <= 1:
            raise ValueError("partglee_threshold must be in (0, 1]")
        return settings

    def capabilities(self) -> dict[str, bool]:
        # Configuration presence is not a successful model/GPU readiness probe.
        return {
            "upscale": bool(self.realesrgan_repository and self.realesrgan_weights),
            "partglee": bool(self.partglee_repository and self.partglee_weights),
            "see-through": bool(self.see_through_repository),
        }
