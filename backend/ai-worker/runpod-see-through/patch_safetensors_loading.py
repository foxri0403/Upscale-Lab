"""Fail-fast patch for See-through's UNet safetensors selection.

This patches the OFFICIAL See-through code inside the existing volume-v1 image,
not the app/backend, and avoids replacing the persistent-volume-aware handler.
"""
from __future__ import annotations

import ast
from pathlib import Path
import re

SOURCE = Path("/opt/see-through/common/utils/inference_utils.py")
PATTERN = re.compile(
    r"(?P<indent>^[ \t]*)unet[ \t]*=[ \t]*UNetFrameConditionModel\.from_pretrained"
    r"\([ \t]*pretrained[ \t]*,[ \t]*subfolder[ \t]*=[ \t]*(['\"])unet\2[ \t]*\)",
    re.MULTILINE,
)


def patch_file(path: Path) -> None:
    original = path.read_text(encoding="utf-8")
    matches = list(PATTERN.finditer(original))
    if len(matches) != 1:
        raise RuntimeError(
            f"Expected exactly one official See-through UNet loading call, found {len(matches)}; "
            "upstream changed, refusing to silently patch the wrong code"
        )
    match = matches[0]
    replacement = (
        match.group("indent")
        + "unet = UNetFrameConditionModel.from_pretrained("
        + "pretrained, subfolder='unet', use_safetensors=True)"
    )
    modified = original[:match.start()] + replacement + original[match.end():]
    ast.parse(modified, filename=str(path))
    path.write_text(modified, encoding="utf-8")
    print(f"Patched See-through UNet to request safetensors: {path}")


if __name__ == "__main__":
    if not SOURCE.exists():
        raise SystemExit(f"Missing official See-through source: {SOURCE}")
    patch_file(SOURCE)
