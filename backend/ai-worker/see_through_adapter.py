"""Run See-through's official PSD pipeline and export renderable PNG layers.

This adapter deliberately keeps the research repository outside this codebase. It invokes
`inference/scripts/inference_psd.py`, then converts the resulting PSD into a small JSON
manifest consumed by the ASP.NET Core worker.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys

from psd_tools import PSDImage


def classify_layer(name: str) -> str:
    normalized = name.lower().replace("-", "_").replace(" ", "_")
    mappings = (
        (("background", "bg"), "Background"),
        (("back_hair", "rear_hair", "behind"), "BackPart"),
        (("hair", "bang", "fringe"), "Hair"),
        (("face", "eye", "mouth", "nose", "ear"), "Face"),
        (("cloth", "dress", "shirt", "skirt", "sleeve", "pants"), "Clothes"),
        (("accessory", "ribbon", "hat", "glasses", "jewelry"), "Accessory"),
        (("body", "skin", "arm", "hand", "leg", "foot"), "Body"),
        (("front", "foreground"), "FrontPart"),
    )
    for keywords, layer_type in mappings:
        if any(keyword in normalized for keyword in keywords):
            return layer_type
    return "Other"


def pixel_layers(psd: PSDImage):
    for layer in psd.descendants():
        if layer.is_group() or not layer.is_visible():
            continue
        image = layer.composite()
        if image is not None and image.getbbox() is not None:
            yield layer, image


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
    layers_output = output / "layers"
    raw_output.mkdir(parents=True, exist_ok=True)
    layers_output.mkdir(parents=True, exist_ok=True)

    inference_script = repository / "inference" / "scripts" / "inference_psd.py"
    command = [
        sys.executable,
        str(inference_script),
        "--srcp",
        str(input_path),
        "--save_dir",
        str(raw_output),
        "--save_to_psd",
    ]
    subprocess.run(command, cwd=repository, check=True)

    psd_candidates = sorted(raw_output.rglob("*.psd"), key=lambda path: path.stat().st_mtime, reverse=True)
    if not psd_candidates:
        raise RuntimeError("See-through did not create a PSD output")

    psd = PSDImage.open(psd_candidates[0])
    extracted = list(pixel_layers(psd))
    if not extracted:
        raise RuntimeError("The generated PSD contains no visible pixel layers")

    manifest_layers = []
    denominator = max(1, len(extracted) - 1)
    for order, (layer, image) in enumerate(extracted):
        file_name = f"{order:03d}.png"
        image.save(layers_output / file_name, format="PNG")
        depth = order / denominator
        manifest_layers.append(
            {
                "file": f"layers/{file_name}",
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
        json.dumps({"layers": manifest_layers}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
