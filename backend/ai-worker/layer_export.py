"""Shared full-canvas layer representation consumed by the existing LiveLayer renderer."""
from __future__ import annotations

import json
from pathlib import Path


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


def layer_record(file: str, order: int, depth: float, layer_type: str, name: str = "") -> dict:
    return {
        "file": file, "name": name, "layerType": layer_type, "layerOrder": order,
        "depth": depth, "positionX": 0.0, "positionY": 0.0, "rotation": 0.0, "scale": 1.0,
        "movementX": round(depth * 20.0, 3), "movementY": round(depth * 12.0, 3),
    }


def write_manifest(output: Path, width: int, height: int, engine: str, layers: list[dict], **metadata) -> None:
    if not layers:
        raise RuntimeError("The model produced no usable layers")
    payload = {"schemaVersion": 1, "operation": "layers", "engine": engine,
               "width": width, "height": height, "layers": layers, **metadata}
    (output / "manifest.json").write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
