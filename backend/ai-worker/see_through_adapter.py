"""See-through PSD exporter with non-destructive color/alpha diagnostics.

Keep the established full-canvas PNG + manifest interface intact.
Diagnostic information is attached to the manifest; sensitive URLs are never read
or logged here. Does NOT fabricate color or alpha when the PSD lacks them.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
import subprocess
import sys

from PIL import Image
from psd_tools import PSDImage
from psd_tools.constants import ColorMode


def classify_layer(name: str) -> str:
    """Classify semantic PSD layer names without substring collisions.

    The existing API expects its established layerType vocabulary.  In
    particular, `topwear` must not match the substring `ear` (Face).
    """
    # Separate words consistently, including names like `FrontHair`.
    words = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", name.strip())
    normalized = re.sub(r"[^a-z0-9]+", "_", words.casefold()).strip("_")
    if not normalized:
        return "Other"
    tokens = set(normalized.split("_"))
    compact = normalized.replace("_", "")

    # The exact names emitted by See-through take priority.
    explicit = {
        "topwear": "Clothes", "bottomwear": "Clothes",
        "legwear": "Clothes", "handwear": "Clothes",
        "upperwear": "Clothes", "outerwear": "Clothes",
        "backhair": "BackHair", "rearhair": "BackHair",
        "fronthair": "FrontHair", "bangs": "FrontHair",
        "neck": "Body", "ears": "Face", "ear": "Face",
        "irides": "Eye", "iris": "Eye", "eyewhite": "Eye",
        "eyelash": "Eye", "eyebrow": "Eyebrow",
        "crown": "Accessory", "wand": "Accessory",
        "staff": "Accessory", "headgear": "Accessory",
        "hand": "Body", "fist": "Body", "arm": "Body",
    }
    if compact in explicit:
        return explicit[compact]

    # Token matches instead of `word in name`: prevents topwear -> ear.
    if "background" in tokens or "bg" in tokens:
        return "Background"
    if ("hair" in tokens and tokens.intersection({"back", "rear", "behind"})) or "backhair" in tokens:
        return "BackHair"
    if ("hair" in tokens and tokens.intersection({"front", "bang", "fringe"})) or tokens.intersection({"fringe", "bang", "bangs", "fronthair"}):
        return "FrontHair"
    if tokens.intersection({"eyebrow", "eyebrows", "brow", "brows"}):
        return "Eyebrow"
    if tokens.intersection({"eye", "eyes", "iris", "irides", "pupil", "eyelash", "eyelashes", "eyewhite", "sclera"}):
        return "Eye"
    if tokens.intersection({"mouth", "lip", "lips"}):
        return "Mouth"
    if "nose" in tokens:
        return "Nose"
    if tokens.intersection({"face", "head", "ear", "ears"}):
        return "Face"
    if "hair" in tokens:
        return "Hair"
    if tokens.intersection({"cloth", "clothes", "clothing", "dress", "shirt", "skirt", "sleeve", "pants", "jacket", "coat", "topwear", "bottomwear", "upperwear", "outerwear"}):
        return "Clothes"
    if tokens.intersection({"accessory", "ribbon", "hat", "glasses", "jewelry", "crown", "wand", "staff", "star", "headgear"}):
        return "Accessory"
    if tokens.intersection({"body", "skin", "arm", "arms", "hand", "hands", "fist", "leg", "legs", "foot", "feet", "neck"}):
        return "Body"
    if "foreground" in tokens or "front" in tokens:
        return "FrontPart"
    if "back" in tokens:
        return "BackPart"
    return "Other"


# The 2026-10-26 ASP.NET Core LayerType enum only understands these 9 values:
# Background, BackPart, Body, FrontPart, Hair, Face, Clothes, Accessory, Other.
# Preserve fine-grained labels separately for future automatic rigging.
def api_layer_type(semantic_type: str) -> str:
    return {
        'BackHair': 'Hair', 'FrontHair': 'Hair',
        'Eye': 'Face', 'Eyebrow': 'Face',
        'Mouth': 'Face', 'Nose': 'Face',
    }.get(semantic_type, semantic_type)


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


def split_disconnected_handwear(image: Image.Image) -> tuple[list[tuple[str, Image.Image]], int | None]:
    """Conservatively split *visually disconnected* left/right sleeve+hand regions.

    Treat 'left' and 'right' as screen coordinates, NOT anatomical sides.
    Only split when a >=12-pixel EMPTY COLUMN GAP separates two substantial
    visible regions. No model inference or guessing hidden arm boundaries.
    Other input types must not call this function.
    """
    rgba = image.convert('RGBA')
    alpha = rgba.getchannel('A')
    bbox = alpha.getbbox()
    if not bbox:
        return [('whole', rgba)], None
    x0, y0, x1, y1 = bbox
    if x1 - x0 < 60:
        return [('whole', rgba)], None

    counts = [0] * rgba.width
    px = alpha.load()
    for y in range(y0, y1):
        for x in range(x0, x1):
            if px[x, y] >= 16:
                counts[x] += 1

    total = sum(counts)
    if total < 4000:
        return [('whole', rgba)], None

    gaps = []
    x = x0
    while x < x1:
        if counts[x]:
            x += 1
            continue
        start = x
        while x < x1 and counts[x] == 0:
            x += 1
        finish = x
        left_pixels = sum(counts[x0:start])
        right_pixels = sum(counts[finish:x1])
        if (finish - start >= 12
                and min(left_pixels, right_pixels) >= 1500
                and min(left_pixels, right_pixels) / total >= 0.15):
            gaps.append((finish - start, start, finish))
    if not gaps:
        return [('whole', rgba)], None

    _, gap_start, gap_end = max(gaps)
    split_x = (gap_start + gap_end) // 2
    screen_left = rgba.copy()
    screen_left.paste((0, 0, 0, 0), (split_x, 0, rgba.width, rgba.height))
    screen_right = rgba.copy()
    screen_right.paste((0, 0, 0, 0), (0, 0, split_x, rgba.height))
    if not screen_left.getchannel('A').getbbox() or not screen_right.getchannel('A').getbbox():
        return [('whole', rgba)], None
    return [('screenLeft', screen_left), ('screenRight', screen_right)], split_x


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

    # First select the correct PSD color/alpha image. Then split only handwear
    # parts that have two disconnected screen-left/right regions.
    prepared = []
    for layer, composite in extracted:
        try:
            raw = layer.topil()
        except (ValueError, NotImplementedError):
            raw = None
        composite_quality = pixel_quality(composite)
        raw_quality = pixel_quality(raw)
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
        full = render_full_canvas(selected, layer, (psd.width, psd.height))
        layer_name = layer.name or ''
        if layer_name.casefold().replace(' ', '').replace('_', '') == 'handwear':
            pieces, split_x = split_disconnected_handwear(full)
        else:
            pieces, split_x = [('whole', full)], None
        for side, piece in pieces:
            prepared.append((layer_name, piece, side, split_x,
                             composite_quality, raw_quality,
                             'raw-fallback' if raw_is_better else 'composite',
                             has_mask, has_clipping))

    if not 1 <= len(prepared) <= 64:
        raise ValueError('More than 64 exported parts after conservative splitting')

    manifest, diagnoses = [], []
    denominator = max(1, len(prepared) - 1)
    for order, (name, result, side, split_x, composite_quality, raw_quality,
                export_method, has_mask, has_clipping) in enumerate(prepared):
        filename = f'{order:03d}.png'
        result.save(layers_dir / filename, format='PNG')
        final_quality = pixel_quality(result)
        depth = order / denominator
        semantic_type = classify_layer(name)
        manifest.append({
            'file': f'layers/{filename}',
            'layerType': api_layer_type(semantic_type),
            'semanticLayerType': semantic_type,
            'layerOrder': order,
            'depth': depth,
            'positionX': 0.0,
            'positionY': 0.0,
            'rotation': 0.0,
            'scale': 1.0,
            'movementX': round(4.0 + depth * 20.0, 3),
            'movementY': round(3.0 + depth * 16.0, 3),
        })
        diag = {
            'file': f'layers/{filename}',
            'sourceLayerName': name,
            'semanticLayerType': semantic_type,
            'composite': composite_quality,
            'rawPixels': raw_quality,
            'export': final_quality,
            'exportMethod': export_method,
            'hasMask': has_mask,
            'hasClipping': has_clipping,
        }
        if side != 'whole':
            diag['splitFrom'] = name
            diag['splitScreenSide'] = side
            diag['splitColumn'] = split_x
            diag['splitMethod'] = 'disconnected-alpha-horizontal-gap'
        diagnoses.append(diag)
        print(f'[PSD] exported part={order:03d} source={name} side={side} '
              f'quality={final_quality}', flush=True)

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
