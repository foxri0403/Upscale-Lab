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

from PIL import Image, ImageFilter
import numpy as np
from psd_tools import PSDImage
from psd_tools.constants import ColorMode
from generic_quality_refiner import refine_layer_generic


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


def move_eyebrows_above_face(parts: list, name_index: int = 0) -> tuple[list, list[str]]:
    """Make eyebrow artwork visible without putting it in front of hair.

    The See-through PSD may list eyebrow layers BEFORE its opaque base face.
    The face then covers the eyebrows completely during layer compositing.
    Move only early eyebrows to just after the final base `face` layer, while
    preserving their relative order. Do not touch unrelated layers.

    `name_index` identifies where the source layer name lives in each tuple.
    Returns (reordered_parts, moved_layer_names).
    """
    def canonical(value) -> str:
        return re.sub(r"[^a-z0-9]", "", str(value).casefold())

    items = list(parts)
    face_indices = [i for i, item in enumerate(items)
                    if canonical(item[name_index]) == 'face']
    if not face_indices:
        return items, []
    last_face = max(face_indices)
    early_eyebrows = [i for i, item in enumerate(items)
                      if i < last_face
                      and canonical(item[name_index]) in
                      {'eyebrow', 'eyebrows', 'brow', 'brows'}]
    if not early_eyebrows:
        return items, []
    moved = [items[i] for i in early_eyebrows]
    other = [item for i, item in enumerate(items) if i not in set(early_eyebrows)]
    target = max(i for i, item in enumerate(other)
                 if canonical(item[name_index]) == 'face') + 1
    adjusted = other[:target] + moved + other[target:]
    return adjusted, [str(item[name_index]) for item in moved]



def normalize_eye_part_order(parts: list, name_index: int = 0) -> tuple[list, list[str]]:
    """Ensure visible eye features render in the correct order over the base face.

    Bottom-to-top: eye white/sclera, iris/pupil, eyelashes/eyeline.
    The frontal hair remains on top (this does not hallucinate hidden eyes).
    Parts without a base 'face' layer are left unchanged for safety.
    Names are taken from the model-produced PSD rather than image content.
    """
    def canonical(value) -> str:
        return re.sub(r"[^a-z0-9]", "", str(value).casefold())

    priority = {
        'eyewhite': 0, 'sclera': 0, 'eye whites': 0,
        'iris': 1, 'irides': 1, 'pupil': 1, 'pupils': 1,
        'eyelash': 2, 'eyelashes': 2, 'eyelid': 2,
        'eyeline': 2,
    }
    # Remove stray whitespace from lookup keys (canonical names are compact).
    keys = {canonical(k): v for k, v in priority.items()}
    items = list(parts)
    if not any(canonical(x[name_index]) == 'face' for x in items):
        return items, []
    eye_parts = [(i, item) for i, item in enumerate(items)
                 if canonical(item[name_index]) in keys]
    if not eye_parts:
        return items, []
    remaining = [item for item in items
                 if canonical(item[name_index]) not in keys]
    face_pos = max(i for i, item in enumerate(remaining)
                   if canonical(item[name_index]) == 'face')
    # Avoid changing a PSD with a nonstandard foreground-hair-before-face order.
    if any(canonical(item[name_index]) in {'fronthair', 'bangs'}
           for item in remaining[:face_pos]):
        return items, []
    sorted_eyes = [item for _, item in sorted(
        eye_parts,
        key=lambda pair: (keys[canonical(pair[1][name_index])], pair[0]))]
    updated = remaining[:face_pos + 1] + sorted_eyes + remaining[face_pos + 1:]
    new_positions = {id(item): i for i, item in enumerate(updated)}
    changed = [str(item[name_index]) for original_index, item in eye_parts
               if new_positions[id(item)] != original_index]
    return updated, changed


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



# Post-processing v1 is intentionally conservative.  It should never be used
# as a substitute for learned segmentation, anatomical rigging or inpainting.
# NumPy is already installed by the pretrained See-through/torch environment.
def postprocess_layer_rgba(image: Image.Image, source_name: str = "") -> tuple[Image.Image, dict]:
    """Clean isolated alpha specks and improve small facial feature visibility.

    The exported full-canvas RGBA dimensions and original colors are retained.
    Weakly opaque single-pixel specks with no neighboring opaque content are
    removed. Tiny isolated dark holes may be repaired in solid paint only.
    Legwear is treated separately: when a convincing low-confidence gap divides
    the TWO opaque legs, remove model hallucination between those legs.
    Never hard-threshold arbitrary layer borders or blur the whole image.
    """
    rgba = image.convert("RGBA")
    arr = np.asarray(rgba, dtype=np.uint8).copy()
    alpha = arr[:, :, 3].copy()
    original_alpha = alpha.copy()
    canonical = re.sub(r"[^a-z0-9]", "", source_name.casefold())
    h, w = alpha.shape
    mask = alpha > 0
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        return rgba, {"applied": False, "reason": "empty-layer", "changedAlphaPixels": 0}

    # Work only within the true nontransparent bounding rectangle.
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    stats = {"applied": True, "sourceName": source_name, "removedSpecklePixels": 0,
             "repairedPinholes": 0, "boostedFeaturePixels": 0,
             "legGapRows": 0, "legGapRemovedPixels": 0}

    # A standalone, weak pixel cannot be a meaningful edge, but do not remove
    # singleton eyebrow/eyelash/iris/mouth/nose features.
    features = {"eyebrow", "eyelash", "eyewhite", "irides", "iris",
                "mouth", "nose", "eyelid", "eyeline"}
    if canonical not in features:
        core = (alpha[y0:y1, x0:x1] >= 48)
        p = np.pad(core, ((1, 1), (1, 1)), mode="constant")
        neighbors = np.zeros(core.shape, dtype=bool)
        for dy in range(3):
            for dx in range(3):
                if dy == 1 and dx == 1:
                    continue
                neighbors |= p[dy:dy + core.shape[0], dx:dx + core.shape[1]]
        sub = alpha[y0:y1, x0:x1]
        speckle = (sub > 0) & (sub < 48) & (~neighbors)
        stats["removedSpecklePixels"] = int(speckle.sum())
        sub[speckle] = 0

    # Restore 1-pixel pinholes only when fully surrounded by solid paint.
    # Interpolate RGBA from reliable immediate neighbors, no hallucinated shape.
    if canonical not in features and (y1-y0) > 2 and (x1-x0) > 2:
        a = alpha[y0:y1, x0:x1]
        center = a[1:-1, 1:-1]
        solid = ((center == 0) & (a[:-2, 1:-1] >= 244)
                 & (a[2:, 1:-1] >= 244)
                 & (a[1:-1, :-2] >= 244)
                 & (a[1:-1, 2:] >= 244))
        if np.any(solid):
            patch = arr[y0:y1, x0:x1]
            rgb = patch[:, :, :3].astype(np.uint16)
            estimated = (rgb[:-2, 1:-1] + rgb[2:, 1:-1] +
                         rgb[1:-1, :-2] + rgb[1:-1, 2:]) // 4
            patch[1:-1, 1:-1, :3][solid] = estimated[solid].astype(np.uint8)
            center[solid] = 245
            stats["repairedPinholes"] = int(solid.sum())

    # A slight boost for small facial features, while preserving shape and
    # already opaque pixels. Do not use strong erosion, closing or dilation.
    if canonical in features:
        eligible = (alpha >= 24) & (alpha <= 190)
        boosted = np.minimum(255, alpha.astype(np.uint16) +
                             np.minimum(12, np.rint(alpha.astype(np.float32) * 0.10).astype(np.uint16)))
        newvals = boosted.astype(np.uint8)
        stats["boostedFeaturePixels"] = int(np.count_nonzero(eligible & (newvals != alpha)))
        alpha[eligible] = newvals[eligible]

    # The See-through legwear mask sometimes paints a translucent gray triangle
    # in the otherwise empty space BETWEEN legs. Only clean it if robust, long
    # left/right opaque silhouettes can be identified. Never attempt to split
    # connected pant legs, which requires a separate segmentation step.
    if canonical == "legwear" and (y1-y0) >= 150 and (x1-x0) >= 80:
        candidates = []
        y_start = y0 + int((y1-y0) * 0.34)
        min_run = max(12, int((x1-x0) * 0.04))
        min_gap = max(14, int((x1-x0) * 0.035))
        for y in range(y_start, y1):
            opaque = alpha[y, x0:x1] >= 230
            change = np.diff(np.pad(opaque.astype(np.int8), (1, 1)))
            starts = np.flatnonzero(change == 1) + x0
            ends = np.flatnonzero(change == -1) + x0
            runs = [(int(s), int(e)) for s, e in zip(starts, ends)
                    if e-s >= min_run]
            if len(runs) != 2:
                continue
            left, right = runs
            gap_start, gap_end = left[1], right[0]
            if gap_end-gap_start < min_gap:
                continue
            mid = alpha[y, gap_start:gap_end]
            if mid.size and mid.max() < 230:
                candidates.append((y, gap_start, gap_end))
        # Require a series of clear left/right silhouettes, not isolated rows.
        required = max(24, int((y1-y_start)*0.12))
        # Process only a contiguous, long gap sequence; a few accidental
        # two-run scanlines must not erase actual clothing in other poses.
        groups = []
        for entry in candidates:
            if not groups or entry[0] != groups[-1][-1][0] + 1:
                groups.append([])
            groups[-1].append(entry)
        longest = max(groups, key=len, default=[])
        if len(longest) >= required:
            first_y = longest[0][0]
            # A progressive fade avoids a visible flat cutoff at the top of
            # the detected gap; at its bottom the spurious mask is removed.
            feather_rows = max(20, min(90, int((y1-y0) * 0.12)))
            for y, lo, hi in longest:
                lo2, hi2 = lo+2, hi-2
                if hi2 <= lo2:
                    continue
                view = alpha[y, lo2:hi2]
                old = view.copy()
                strength = min(1.0, max(0.0, (y-first_y)/feather_rows))
                alpha[y, lo2:hi2] = np.rint(view * (1.0-strength)).astype(np.uint8)
                stats["legGapRemovedPixels"] += int(np.count_nonzero(old != alpha[y, lo2:hi2]))
                stats["legGapRows"] += 1

    # RGB is preserved except for repaired pinholes. Normalize fully
    # transparent pixels to avoid misleading white rectangles in some viewers.
    arr[:, :, 3] = alpha
    transparent = alpha == 0
    arr[transparent, :3] = 0
    stats["changedAlphaPixels"] = int(np.count_nonzero(alpha != original_alpha))
    stats["changedAlphaFraction"] = round(stats["changedAlphaPixels"] / max(1,
            int(np.count_nonzero(original_alpha))), 5)
    return Image.fromarray(arr, mode="RGBA"), stats

def refine_legwear_v2(image: Image.Image, source_name: str = '') -> tuple[Image.Image, dict]:
    """Remove bright, near-neutral 'background ghosts' INSIDE two dark pant legs.

    See-through may paint the white background between black trousers as an
    *opaque gray triangle*.  Simply dropping weak-alpha pixels (v1) cannot
    remove it.  v2 looks for TWO independently dark, opaque leg rails over a
    long continuous vertical interval.  It clears only gray/neutral pixels
    strictly in the gap, never dark trouser pixels or colored clothing.

    This conservative operation is disabled for all other layer names and for
    colored/light trousers without the necessary two-rail evidence.  It is
    NOT a generative reconstruction or automatic leg rigging algorithm.
    """
    canonical = re.sub(r'[^a-z0-9]', '', source_name.casefold())
    stats = {'applied': False, 'sourceName': source_name, 'version': 'leg-gap-gray-v2',
             'removedGrayGhostPixels': 0, 'candidateRows': 0,
             'validatedRows': 0, 'reason': 'not-legwear'}
    if canonical != 'legwear':
        return image, stats
    rgba = image.convert('RGBA')
    arr = np.array(rgba, dtype=np.uint8, copy=True)
    alpha = arr[:, :, 3]
    ys, xs = np.where(alpha >= 80)
    if len(xs) == 0:
        stats['reason'] = 'empty'; return rgba, stats
    x0, x1, y0, y1 = int(xs.min()), int(xs.max()) + 1, int(ys.min()), int(ys.max()) + 1
    width, height = x1-x0, y1-y0
    if width < 120 or height < 200:
        stats['reason'] = 'small-shape'; return rgba, stats

    rgb = arr[:, :, :3].astype(np.int16)
    luminance = rgb.mean(axis=2)
    chroma = rgb.max(axis=2) - rgb.min(axis=2)
    min_run = max(12, width // 42)
    min_gap = max(9, width // 75)
    rows = []
    for y in range(y0 + int(height * .12), y1):
        # Opaque DARK pixels, not light-blue edge highlights or see-through gray.
        dark = (alpha[y, x0:x1] >= 215) & (luminance[y, x0:x1] <= 65)
        # Bridge isolated antialiasing pixel holes within each dark rail.
        mask = np.convolve(np.pad(dark.astype(np.uint8), (2, 2)),
                           np.ones(5, dtype=np.uint8), mode='valid') >= 3
        edges = np.diff(np.pad(mask.astype(np.int8), (1, 1)))
        starts, ends = np.flatnonzero(edges == 1), np.flatnonzero(edges == -1)
        runs = [(x0 + int(a), x0 + int(b)) for a, b in zip(starts, ends)
                if b-a >= min_run]
        candidates = []
        for left, right in zip(runs, runs[1:]):
            lo, hi = left[1] + 2, right[0] - 2
            if hi - lo < min_gap:
                continue
            # Gray pixels with moderate or strong opacity are specifically
            # the failure v1 couldn't correct. Colored or dark art is spared.
            ghost = ((luminance[y, lo:hi] >= 92)
                     & (chroma[y, lo:hi] <= 35)
                     & (alpha[y, lo:hi] >= 35))
            n = int(np.count_nonzero(ghost))
            if n >= max(5, int((hi-lo)*.12)):
                candidates.append((n, lo, hi))
        if candidates:
            n, lo, hi = max(candidates)
            rows.append((y, lo, hi, n))
    stats['candidateRows'] = len(rows)
    if not rows:
        stats['reason'] = 'no-two-rail-gray-gap'; return rgba, stats

    # Require >=30 successive scanlines. A single gray stripe or outfit
    # decoration must never turn into a hole.
    groups = []
    for row in rows:
        if not groups or row[0] > groups[-1][-1][0] + 2:
            groups.append([])
        groups[-1].append(row)
    best = max(groups, key=len)
    required = max(30, int(height * .07))
    if len(best) < required:
        stats['reason'] = 'insufficient-contiguous-rows'; return rgba, stats
    stats['validatedRows'] = len(best)

    modifications = []
    for y, lo, hi, _ in best:
        selected = ((luminance[y, lo:hi] >= 92)
                    & (chroma[y, lo:hi] <= 35)
                    & (alpha[y, lo:hi] >= 35))
        if np.any(selected):
            modifications.append((y, lo, hi, selected))
    changed = sum(int(sel.sum()) for _, _, _, sel in modifications)
    # Avoid overaggressive modifications on unrelated art.
    visible_pixels = int(np.count_nonzero(alpha >= 8))
    if changed > visible_pixels * 0.15 or changed > width*height*.10:
        stats['reason'] = 'excessive-change-guard'; return rgba, stats
    for y, lo, hi, selected in modifications:
        alpha[y, lo:hi][selected] = 0
        # Avoid RGB halos if the compositing client mishandles alpha.
        arr[y, lo:hi, :3][selected] = 0
    stats['applied'] = bool(changed)
    stats['removedGrayGhostPixels'] = changed
    stats['reason'] = 'fixed-gray-gap' if changed else 'no-change'
    return Image.fromarray(arr, mode='RGBA'), stats


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

    prepared, moved_eyebrows = move_eyebrows_above_face(prepared, name_index=0)
    prepared, reordered_eyes = normalize_eye_part_order(prepared, name_index=0)
    if reordered_eyes:
        print('[PSD] fixed eye order: sclera -> iris -> eyelashes; front hair remains above', flush=True)
    if moved_eyebrows:
        print('[PSD] fixed render order: eyebrow above base face, below front hair', flush=True)

    if not 1 <= len(prepared) <= 64:
        raise ValueError('More than 64 exported parts after conservative splitting')

    manifest, diagnoses = [], []
    denominator = max(1, len(prepared) - 1)
    for order, (name, result, side, split_x, composite_quality, raw_quality,
                export_method, has_mask, has_clipping) in enumerate(prepared):
        filename = f'{order:03d}.png'
        semantic_type = classify_layer(name)
        result, cleanup_stats = postprocess_layer_rgba(result, name)
        # v3 applies a safe generic cleanup without erasing ambiguous white/grey art.
        # v1 already boosts facial alpha, so don't boost again here.
        result, generic_stats = refine_layer_generic(
            result, source_name=name, semantic_type=semantic_type,
            boost_features=False,
        )
        result, refine_stats = refine_legwear_v2(result, name)
        result.save(layers_dir / filename, format='PNG')
        final_quality = pixel_quality(result)
        depth = order / denominator
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
            'qualityCleanup': cleanup_stats,
            'qualityRefinementV3Generic': generic_stats,
            'qualityRefinementV2': refine_stats,
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
            'qualityPostprocess': {
                'version': 'generic-v3-safe + conservative-v2',
                'note': 'v1 cleanup, general v3 safe cleanup, plus guarded v2 legwear cleanup; no learned reconstruction. Ambiguous bright/grey regions remain unchanged.',
            },
            'renderOrderCorrections': {
                'eyebrowsMovedAboveFace': moved_eyebrows,
                'eyePartsReordered': reordered_eyes,
            },
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
