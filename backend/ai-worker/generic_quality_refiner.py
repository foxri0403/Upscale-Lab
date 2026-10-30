"""Conservative, model-agnostic cleanup for exported transparent layer PNGs.

Only reliable raster repairs are automatic:
  * Remove single-pixel and similarly isolated LOW-ALPHA speckles.
  * Repair fully enclosed one-pixel transparency defects in SOLID paint.
  * Optional small-feature alpha reinforcement (off by default because the
    current See-through adapter already applies it in postprocess_layer_rgba).
Ambiguous grey/white fills, overlaps, disconnected anatomical parts and hidden
regions are deliberately left untouched.  No OpenCV dependency.
"""
from __future__ import annotations

import re

import numpy as np
from PIL import Image

FEATURE_NAMES = frozenset({
    'eyelash', 'eyelashes', 'eyebrow', 'eyebrows', 'eyelid', 'eyeline',
    'eye', 'eyes', 'eyewhite', 'iris', 'irides', 'pupil', 'pupils',
    'brow', 'brows', 'nose', 'mouth', 'lip', 'lips',
})


def _canonical(name: str) -> str:
    return re.sub(r'[^a-z0-9]', '', str(name).casefold())


def refine_layer_generic(image: Image.Image, source_name: str = '',
                         semantic_type: str = '', *,
                         boost_features: bool = False,
                         max_change_fraction: float = 0.015) -> tuple[Image.Image, dict]:
    """Clean a single full-canvas RGBA image; preserve its geometry.

    Pixels in legitimate bright/grey interiors are NOT deleted. This is
    essential for white eyes, clothing, buttons, pale fur and accessories.

    max_change_fraction limits unintended changes relative to pixels with
    alpha > 0; when exceeded the original is returned unchanged.
    """
    if not (0.0 < max_change_fraction <= 0.1):
        raise ValueError('max_change_fraction must be in (0, 0.1]')
    rgba = image.convert('RGBA')
    original = np.array(rgba, dtype=np.uint8)
    out = original.copy()
    alpha = out[:, :, 3]
    orig_alpha = original[:, :, 3]
    stats = {
        'version': 'generic-v3-safe', 'sourceName': source_name,
        'semanticType': semantic_type, 'removedWeakSpeckles': 0,
        'repairedOpaquePinholes': 0, 'boostedFeaturePixels': 0,
        'changedAlphaPixels': 0, 'changedVisibleFraction': 0.0,
        'reverted': False, 'reason': 'completed',
        'ambiguousGhosts': 'not automatically removed (requires semantic or reference evidence)',
    }
    visible = int(np.count_nonzero(orig_alpha))
    if not visible:
        stats['reason'] = 'empty-layer'
        return rgba, stats

    # Only pixels whose entire 8-neighborhood lacks alpha >= 20 are candidates
    # for deletion. A true painted line (including very thin facial details)
    # should almost always touch stronger visible pixels.
    is_feature = _canonical(source_name) in FEATURE_NAMES
    if not is_feature:
        strong = orig_alpha >= 20
        padded = np.pad(strong, 1, mode='constant', constant_values=False)
        near_strong = np.zeros(strong.shape, dtype=bool)
        for dy in range(3):
            for dx in range(3):
                if dy == 1 and dx == 1:
                    continue
                near_strong |= padded[dy:dy + strong.shape[0],
                                       dx:dx + strong.shape[1]]
        weak_speckle = ((orig_alpha > 0) & (orig_alpha < 20) & ~near_strong)
        # Deletion is intentionally limited to faint pixels, not islands of
        # fully opaque art; it cannot remove eyelashes or small buttons.
        alpha[weak_speckle] = 0
        stats['removedWeakSpeckles'] = int(weak_speckle.sum())

    # Fix a zero-alpha pixel only when surrounded on all four sides by nearly
    # opaque pixels WITH SIMILAR COLORS. This excludes intentional eye/highlight
    # holes and deliberately transparent small contours.
    if min(alpha.shape) >= 3:
        a = alpha
        top, bottom = a[:-2, 1:-1], a[2:, 1:-1]
        left, right = a[1:-1, :-2], a[1:-1, 2:]
        center = a[1:-1, 1:-1]
        hole = ((center == 0) & (top >= 246) & (bottom >= 246)
                & (left >= 246) & (right >= 246))
        if hole.any():
            rgb = out[:, :, :3].astype(np.int16)
            ns = np.stack((rgb[:-2, 1:-1], rgb[2:, 1:-1],
                           rgb[1:-1, :-2], rgb[1:-1, 2:]), axis=0)
            color_range = ns.max(axis=0) - ns.min(axis=0)
            similar = np.max(color_range, axis=2) <= 18
            hole &= similar
            if hole.any():
                avg = np.rint(ns.mean(axis=0)).astype(np.uint8)
                out[1:-1, 1:-1, :3][hole] = avg[hole]
                center[hole] = 246
                stats['repairedOpaquePinholes'] = int(hole.sum())

    if is_feature and boost_features:
        eligible = (alpha >= 24) & (alpha <= 190)
        new_alpha = np.minimum(alpha.astype(np.uint16) + 8, 255).astype(np.uint8)
        stats['boostedFeaturePixels'] = int((eligible & (new_alpha != alpha)).sum())
        alpha[eligible] = new_alpha[eligible]

    changed = int(np.count_nonzero(alpha != orig_alpha))
    fraction = changed / visible
    if fraction > max_change_fraction:
        stats.update(reverted=True, reason='excessive-alpha-changes-reverted',
                     removedWeakSpeckles=0, repairedOpaquePinholes=0,
                     boostedFeaturePixels=0, changedAlphaPixels=0,
                     changedVisibleFraction=0.0)
        return rgba, stats
    stats['changedAlphaPixels'] = changed
    stats['changedVisibleFraction'] = round(fraction, 7)
    return Image.fromarray(out, mode='RGBA'), stats
