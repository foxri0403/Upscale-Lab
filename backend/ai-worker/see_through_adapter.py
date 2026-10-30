"""See-through PSD exporter with non-destructive color/alpha diagnostics.

Keep the established full-canvas PNG + manifest interface intact.
Diagnostic information is attached to the manifest; sensitive URLs are never read
or logged here. Does NOT fabricate color or alpha when the PSD lacks them.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys

from PIL import Image
from psd_tools import PSDImage
from psd_tools.constants import ColorMode


def classify_layer(name: str) -> str:
    normalized = name.casefold().replace('-', '_').replace(' ', '_')
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


def pixel_quality(image: Image.Image | None) -> dict | None:
    """Compact quality indicators; no image pixels or private data in metadata.

    A downsample is used only for COLOR detection, not for the exported images.
    Full-resolution alpha min/max is computed from the actual alpha channel.
    """
    if image is None:
        return None
    rgba = image.convert('RGBA')
    amin, amax = rgba.getchannel('A').getextrema()
    probe = rgba.copy()
    probe.thumbnail((256, 256))
    pixels = probe.getdata()
    visible = colored = 0
    for r, g, b, a in pixels:
        if a > 8:
            visible += 1
            if max(r, g, b) - min(r, g, b) >= 5:
                colored += 1
    return {
        'mode': image.mode,
        'size': [image.width, image.height],
        'alphaMin': amin,
        'alphaMax': amax,
        'hasTransparency': amin < 255,
        'visibleColoredPercent': round(100.0 * colored / max(visible, 1), 2),
    }


def render_full_canvas(image: Image.Image, layer, canvas_size: tuple[int, int]) -> Image.Image:
    rgba = image.convert('RGBA')
    if rgba.size == canvas_size:
        return rgba
    result = Image.new('RGBA', canvas_size, (0, 0, 0, 0))
    # Direct paste preserves the original RGBA channels; a mask would multiply alpha.
    result.paste(rgba, (int(layer.left), int(layer.top)))
    return result


def export_psd(psd: PSDImage, output: Path, input_quality: dict | None = None) -> list[dict]:
    layers_dir = output / 'layers'
    layers_dir.mkdir(parents=True, exist_ok=True)
    extracted = []
    for layer in psd.descendants():
        if layer.is_group() or not layer.is_visible():
            continue
        comp = layer.composite()
        if comp is not None and comp.getbbox() is not None:
            extracted.append((layer, comp))

    if not 1 <= len(extracted) <= 64:
        raise ValueError('See-through PSD must have between 1 and 64 visible pixel layers')

    manifest = []
    diagnoses = []
    denominator = max(1, len(extracted) - 1)
    for order, (layer, composite) in enumerate(extracted):
        try:
            raw = layer.topil()
        except (ValueError, NotImplementedError):
            raw = None
        composite_quality = pixel_quality(composite)
        raw_quality = pixel_quality(raw)
        # Trust the composited result by default, because it applies PSD masks.
        # Use source pixels only when the composite is clearly defective and
        # raw pixels are both colored and transparent, with no external masks.
        has_mask = bool(layer.has_mask()) or bool(layer.has_vector_mask())
        has_clipping = bool(layer.has_clip_layers())
        raw_is_better = bool(
            raw_quality and composite_quality
            and composite_quality['visibleColoredPercent'] == 0
            and not composite_quality['hasTransparency']
            and raw_quality['visibleColoredPercent'] > 0
            and raw_quality['hasTransparency']
            and not has_mask and not has_clipping
        )
        selected = raw if raw_is_better else composite
        filename = f'{order:03d}.png'
        result = render_full_canvas(selected, layer, (psd.width, psd.height))
        result.save(layers_dir / filename, format='PNG')
        final_quality = pixel_quality(result)
        depth = order / denominator
        manifest.append({
            'file': f'layers/{filename}',
            'layerType': classify_layer(layer.name or ''),
            'layerOrder': order,
            'depth': depth,
            'positionX': 0.0,
            'positionY': 0.0,
            'rotation': 0.0,
            'scale': 1.0,
            'movementX': round(4.0 + depth * 20.0, 3),
            'movementY': round(3.0 + depth * 16.0, 3),
        })
        diagnoses.append({
            'file': f'layers/{filename}',
            'sourceLayerName': layer.name or '',
            'composite': composite_quality,
            'rawPixels': raw_quality,
            'export': final_quality,
            'exportMethod': 'raw-fallback' if raw_is_better else 'composite',
            'hasMask': has_mask,
            'hasClipping': has_clipping,
        })
        print(f'[PSD] part={order:03d} method={diagnoses[-1]["exportMethod"]} '
              f'composite={composite_quality} raw={raw_quality} final={final_quality}', flush=True)

    all_gray = all(row['export']['visibleColoredPercent'] == 0 for row in diagnoses)
    all_opaque = all(not row['export']['hasTransparency'] for row in diagnoses)
    psd_all_opaque = all(not row['composite']['hasTransparency'] for row in diagnoses)
    warnings = []
    if all_gray:
        warnings.append('ALL_OUTPUT_LAYERS_GRAYSCALE')
    if all_opaque:
        warnings.append('ALL_OUTPUT_LAYERS_OPAQUE')
    if psd_all_opaque:
        warnings.append('ALL_PSD_COMPOSITES_OPAQUE')
    if warnings:
        print('[PSD][QUALITY WARNING] ' + ','.join(warnings), flush=True)

    payload = {
        'layers': manifest,
        'diagnostics': {
            'psdColorMode': str(psd.color_mode),
            'inputImage': input_quality,
            'layers': diagnoses,
            'warnings': warnings,
            'notice': 'Depth and movement are deterministic placeholders, not learned rigging.',
        },
    }
    (output / 'manifest.json').write_text(
        json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf-8'
    )
    return manifest



def select_color_psd(output_dir: Path, input_stem: str) -> Path:
    """Select See-through's character PSD, never its separately emitted depth PSD.

    The inference script names its output after the input file stem, so we use
    that exact name rather than file modification time or alphabetical order.
    """
    expected = f'{input_stem}.psd'
    matches = [p for p in output_dir.rglob('*.psd')
               if p.name == expected and p.is_file()]
    if len(matches) != 1:
        raise RuntimeError(
            f'Expected exactly one character PSD named {expected!r}; '
            f'found {len(matches)}. Do not use *_depth.psd as a substitute.'
        )
    return matches[0]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--repository', required=True)
    parser.add_argument('--input', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()

    repository = Path(args.repository).resolve()
    input_path = Path(args.input).resolve()
    output = Path(args.output).resolve()
    raw_output = output / 'raw'
    raw_output.mkdir(parents=True, exist_ok=True)

    with Image.open(input_path) as source:
        input_quality = pixel_quality(source)
    print(f'[PSD] input={input_quality}', flush=True)

    script = repository / 'inference' / 'scripts' / 'inference_psd.py'
    subprocess.run([
        sys.executable, str(script), '--srcp', str(input_path),
        '--save_dir', str(raw_output), '--save_to_psd',
    ], cwd=repository, check=True)

    # The official See-through pipeline emits TWO PSD files for one input:
    #   input.psd        -> RGB(A) character parts
    #   input_depth.psd  -> grayscale depth maps (written AFTER the color PSD)
    # Selecting the newest *.psd accidentally chooses the depth document.
    # Match the input stem and explicitly reject depth maps instead.
    color_psd = select_color_psd(raw_output, input_path.stem)
    psd = PSDImage.open(color_psd)
    if psd.color_mode != ColorMode.RGB:
        raise ValueError(
            f'Expected RGB See-through parts PSD, got color mode {psd.color_mode}; '
            'refusing to export grayscale/depth output as character layers'
        )
    print(f'[PSD] selected character parts PSD: {color_psd.name}; '
          f'color_mode={psd.color_mode}', flush=True)
    export_psd(psd, output, input_quality)


if __name__ == '__main__':
    main()
