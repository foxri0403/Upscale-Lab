"""Export pretrained See-through PSD inference as full-canvas transparent PNG parts.

The existing ASP.NET Core result validator requires every part PNG to have the
same width and height. PSD layer.composite() can return only a cropped layer
bounding box, so this adapter places each part back on the PSD's full canvas.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys

from PIL import Image
from psd_tools import PSDImage


def classify_layer(name: str) -> str:
    normalized = name.casefold().replace("-", "_").replace(" ", "_")
    # Specific face/hair parts must be matched before general "face"/"hair".
    groups = (
        (("background", "bg"), "Background"),
        (("back_hair", "backhair", "rear_hair", "behind_hair"), "BackHair"),
        (("front_hair", "fronthair", "bang", "fringe"), "FrontHair"),
        (("eyebrow", "brow"), "Eyebrow"),
        (("eye", "iris", "pupil", "eyelash"), "Eye"),
        (("mouth", "lip"), "Mouth"),
        (("nose",), "Nose"),
        (("face", "head", "ear"), "Face"),
        (("hair",), "Hair"),
        (("cloth", "dress", "shirt", "skirt", "sleeve", "pants"), "Clothes"),
        (("accessory", "ribbon", "hat", "glasses", "jewelry"), "Accessory"),
        (("body", "skin", "arm", "hand", "leg", "foot"), "Body"),
        (("front", "foreground"), "FrontPart"),
        (("back",), "BackPart"),
    )
    for tokens, kind in groups:
        if any(token in normalized for token in tokens):
            return kind
    return "Other"


def pixel_layers(psd: PSDImage):
    for layer in psd.descendants():
        if layer.is_group() or not layer.is_visible():
            continue
        picture = layer.composite()
        if picture is not None and picture.getbbox() is not None:
            yield layer, picture


def render_full_canvas(image: Image.Image, layer, canvas_size: tuple[int, int]) -> Image.Image:
    """Restore PSD layer placement without premultiplying transparency twice."""
    rgba = image.convert("RGBA")
    if rgba.size == canvas_size:
        return rgba
    result = Image.new("RGBA", canvas_size, (0, 0, 0, 0))
    result.paste(rgba, (int(layer.left), int(layer.top)))
    return result


def export_psd(psd: PSDImage, output: Path) -> list[dict]:
    layers_dir = output / "layers"
    layers_dir.mkdir(parents=True, exist_ok=True)
    extracted = list(pixel_layers(psd))
    if not 1 <= len(extracted) <= 64:
        raise ValueError("See-through PSD must have between 1 and 64 visible pixel layers")
    manifest = []
    denominator = max(1, len(extracted) - 1)
    for order, (layer, picture) in enumerate(extracted):
        filename = f"{order:03d}.png"
        result = render_full_canvas(picture, layer, (psd.width, psd.height))
        result.save(layers_dir / filename, format="PNG")
        depth = order / denominator
        manifest.append(
            {
                "file": f"layers/{filename}",
                "layerType": classify_layer(layer.name or ""),
                "layerOrder": order,
                "depth": depth,
                "positionX": 0.0,
                "positionY": 0.0,
                "rotation": 0.0,
                "scale": 1.0,
                "movementX": round(4.0 + depth * 20.0, 3),
                "movementY": round(3.0 + depth * 16.0, 3),
            }
        )
    (output / "manifest.json").write_text(
        json.dumps({"layers": manifest}, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    return manifest


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repository", required=True)
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    repository = Path(args.repository).resolve()
    input_path = Path(args.input).resolve()
    output = Path(args.output).resolve()
    raw_output = output / "raw"
    raw_output.mkdir(parents=True, exist_ok=True)

    # Verified against official See-through inference_psd.py CLI options.
    inference_script = repository / "inference" / "scripts" / "inference_psd.py"
    subprocess.run(
        [sys.executable, str(inference_script), "--srcp", str(input_path),
         "--save_dir", str(raw_output), "--save_to_psd"],
        cwd=repository, check=True,
    )

    psd_candidates = sorted(raw_output.rglob("*.psd"), key=lambda p: p.stat().st_mtime, reverse=True)
    if not psd_candidates:
        raise RuntimeError("See-through did not create a PSD")
    export_psd(PSDImage.open(psd_candidates[0]), output)


if __name__ == "__main__":
    main()
