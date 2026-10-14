"""PartGLEE PACO object/part masks -> approximate 2.5D parallax layers.

Depth uses vertical image position, not measured geometry. OpenCV fills background
holes; this does not reconstruct hidden object surfaces or produce a rigged model.
"""
from __future__ import annotations

import argparse
from pathlib import Path

from layer_export import classify_layer, layer_record, write_manifest


def export_layers(rgb, masks, scores, names, output: Path, threshold: float) -> None:
    import cv2
    import numpy as np
    from PIL import Image

    height, width = rgb.shape[:2]
    candidates = [(mask.astype(bool), float(score), name) for mask, score, name in zip(masks, scores, names)
                  if score >= threshold and mask.sum() >= 64]
    # Small parts get their own pixels; enclosing object masks contribute the remainder.
    candidates.sort(key=lambda item: (int(item[0].sum()), -item[1]))
    claimed = np.zeros((height, width), dtype=bool)
    pieces = []
    for mask, score, name in candidates:
        visible = mask & ~claimed
        if visible.sum() < max(64, mask.sum() * 0.05):
            continue
        depth = 0.2 + 0.7 * float(np.nonzero(visible)[0].mean()) / max(1, height - 1)
        pieces.append((visible, name, depth))
        claimed |= visible
        if len(pieces) == 63:
            break
    if not pieces:
        raise RuntimeError("PartGLEE did not identify usable objects at the configured confidence threshold")
    folder = output / "layers"
    folder.mkdir(parents=True, exist_ok=True)
    background = cv2.inpaint(rgb, claimed.astype(np.uint8) * 255, 5, cv2.INPAINT_TELEA)
    Image.fromarray(background).convert("RGBA").save(folder / "000.png")
    layers = [layer_record("layers/000.png", 0, 0, "Background", "inpainted background")]
    for order, (mask, name, depth) in enumerate(sorted(pieces, key=lambda piece: piece[2]), 1):
        rgba = np.dstack((rgb, mask.astype(np.uint8) * 255))
        filename = f"layers/{order:03d}.png"
        Image.fromarray(rgba).save(output / filename)
        layers.append(layer_record(filename, order, depth, classify_layer(name), name))
    write_manifest(output, width, height, "partglee", layers,
                   depthMethod="vertical-position-heuristic", backgroundMethod="opencv-telea")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--config", required=True)
    parser.add_argument("--weights", required=True)
    parser.add_argument("--threshold", type=float, default=0.5)
    parser.add_argument("--max-dimension", type=int, default=1280)
    args = parser.parse_args()
    import numpy as np
    import torch
    from PIL import Image
    from detectron2.config import get_cfg
    from detectron2.checkpoint import DetectionCheckpointer
    from detectron2.modeling import build_model
    from detectron2.projects.partglee import add_partglee_config

    if not torch.cuda.is_available():
        raise RuntimeError("PartGLEE requires an available CUDA GPU")
    cfg = get_cfg()
    add_partglee_config(cfg)
    cfg.merge_from_file(args.config)
    cfg.MODEL.WEIGHTS = str(Path(args.weights).resolve(strict=True))
    cfg.MODEL.DEVICE = "cuda"
    cfg.MODEL.MaskDINO.TEST.INSTANCE_ON = True
    cfg.MODEL.MaskDINO.TEST.SEMANTIC_ON = False
    cfg.MODEL.MaskDINO.TEST.PANOPTIC_ON = False
    # We supply an aspect-preserving image, with no LSJ square padding/cropping.
    cfg.INPUT.DATASET_MAPPER_NAME = "joint_image_lsj"
    cfg.freeze()
    model = build_model(cfg)
    model.eval()
    DetectionCheckpointer(model).load(cfg.MODEL.WEIGHTS)
    with Image.open(args.input) as image:
        # A fixed white backdrop also makes transparent source pixels deterministic.
        rgba = image.convert("RGBA")
        rgb_image = Image.new("RGBA", rgba.size, (255, 255, 255, 255))
        rgb_image.alpha_composite(rgba)
        rgb_image = rgb_image.convert("RGB")
        resampling = getattr(Image, "Resampling", Image)  # Upstream PartGLEE also supports Pillow 8.x.
        rgb_image.thumbnail((args.max_dimension, args.max_dimension), resampling.LANCZOS)
        rgb = np.asarray(rgb_image).copy()
    pixels = rgb if cfg.INPUT.FORMAT == "RGB" else rgb[:, :, ::-1].copy()
    tensor = torch.as_tensor(pixels.transpose(2, 0, 1).copy(), dtype=torch.float32)
    with torch.inference_mode():
        prediction = model([{"image": tensor, "height": rgb.shape[0], "width": rgb.shape[1], "dataset_name": "paco"}])[0]
    instances = prediction["instances"]
    # Filter before transferring potentially hundreds of masks to CPU.
    keep = instances.scores >= args.threshold
    instances = instances[keep].to("cpu")
    labels = model.dataset_name_dicts["paco"]
    names = [labels[int(label)] for label in instances.pred_classes]
    export_layers(rgb, instances.pred_masks.numpy(), instances.scores.numpy(), names,
                  Path(args.output), args.threshold)


if __name__ == "__main__":
    main()
