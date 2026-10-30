"""CPU-only See-through ZIP repair: semantic classes and clearly disconnected handwear.

Usage: python tools/repair_completed_zip.py old.zip new.zip
Requirements: Pillow. Input is never modified. No AWS, RunPod, or GPU used.
"""
from __future__ import annotations
import argparse
import ast
import io
import json
from pathlib import Path
import re
import zipfile
from PIL import Image


def load_adapter_helpers():
    # Load only pure helper functions from this local, trusted project code;
    # importing the full adapter would require psd-tools on the local PC.
    adapter = Path(__file__).resolve().parents[1] / 'backend/ai-worker/see_through_adapter.py'
    tree = ast.parse(adapter.read_text(encoding='utf-8'), filename=str(adapter))
    allowed = {'classify_layer', 'api_layer_type', 'pixel_quality', 'split_disconnected_handwear', 'move_eyebrows_above_face', 'normalize_eye_part_order'}
    body = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in allowed]
    if len(body) != 6:
        raise RuntimeError('Could not load all required adapter helpers')
    namespace = {'re': re, 'Image': Image, '__name__': 'repair_helpers'}
    exec(compile(ast.Module(body=body, type_ignores=[]), str(adapter), 'exec'), namespace)
    return (namespace['classify_layer'], namespace['api_layer_type'],
            namespace['pixel_quality'], namespace['split_disconnected_handwear'],
            namespace['move_eyebrows_above_face'], namespace['normalize_eye_part_order'])


def repair(source: Path, output: Path) -> tuple[int, int]:
    if source.resolve() == output.resolve():
        raise ValueError('Input and output ZIP paths must be different')
    classify, api_layer_type, pixel_quality, split_arms, move_eyebrows, fix_eyes = load_adapter_helpers()
    with zipfile.ZipFile(source) as src:
        manifest = json.loads(src.read('manifest.json'))
        originals = manifest.get('layers', [])
        diagnoses = manifest.get('diagnostics', {}).get('layers', [])
        names = {item['file']: item.get('sourceLayerName', '') for item in diagnoses}
        if not 1 <= len(originals) <= 64 or any(item.get('file') not in names for item in originals):
            raise ValueError('Missing source layer names or invalid layer count')
        # Defend against archives with unrelated large objects or file paths.
        if len(src.infolist()) > 70 or sum(z.file_size for z in src.infolist()) > 180*1024*1024:
            raise ValueError('ZIP is too large for expected See-through output')

        prepared = []
        for original in originals:
            name = original['file']
            if not re.fullmatch(r'layers/[0-9]{3}\.png', name):
                raise ValueError(f'Invalid PNG entry: {name}')
            layername = names[name]
            with Image.open(io.BytesIO(src.read(name))) as opened:
                rgba = opened.convert('RGBA')
            if rgba.size[0] > 4096 or rgba.size[1] > 4096:
                raise ValueError('Unexpected oversized layer image')
            if layername.casefold().replace(' ', '').replace('_', '') == 'handwear':
                parts, cut = split_arms(rgba)
            else:
                parts, cut = [('whole', rgba)], None
            base_diag = next(item for item in diagnoses if item['file'] == name)
            for side, img in parts:
                prepared.append((original, base_diag, layername, side, cut, img))
            label = ', '.join(side for side, _ in parts)
            print(f'{name}: {layername} -> {label}')

        prepared, moved_eyebrows = move_eyebrows(prepared, name_index=2)
        prepared, reordered_eyes = fix_eyes(prepared, name_index=2)

        if len(prepared) > 64:
            raise ValueError('More than 64 parts after splitting')
        repaired_layers, repaired_diags, outputs = [], [], []
        denom = max(1, len(prepared) - 1)
        for index, (old, old_diag, source_name, side, cut, img) in enumerate(prepared):
            destname = f'layers/{index:03d}.png'
            layer = dict(old)
            semantic = classify(source_name)
            layer.update({
                'file': destname,
                'layerType': api_layer_type(semantic),
                'semanticLayerType': semantic,
                'layerOrder': index,
                'depth': index / denom,
                'movementX': round(4 + 20 * index / denom, 3),
                'movementY': round(3 + 16 * index / denom, 3),
            })
            diag = dict(old_diag)
            diag.update({'file': destname, 'sourceLayerName': source_name,
                         'semanticLayerType': semantic, 'export': pixel_quality(img)})
            if side != 'whole':
                diag.update({'splitFrom': source_name, 'splitScreenSide': side,
                             'splitColumn': cut, 'splitMethod': 'disconnected-alpha-horizontal-gap'})
            buff = io.BytesIO()
            img.save(buff, format='PNG')
            outputs.append((destname, buff.getvalue()))
            repaired_layers.append(layer)
            repaired_diags.append(diag)
            print(f'  -> {destname} type={layer["layerType"]} semantic={semantic} side={side}')

        manifest['layers'] = repaired_layers
        manifest.setdefault('diagnostics', {})['layers'] = repaired_diags
        manifest['diagnostics']['postprocess'] = {
            'classificationFixed': True,
            'screenSeparatedHandwearOnly': len(prepared) > len(originals),
            'eyebrowRenderOrderFixed': bool(moved_eyebrows),
            'eyebrowsMovedAboveFace': moved_eyebrows,
            'eyeRenderOrderFixed': bool(reordered_eyes),
            'eyePartsReordered': reordered_eyes,
            'note': 'Eye white renders below iris, iris below eyelash, all above base face and below front hair. Legs remain grouped; no inferred rigging.',
        }
        output.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as dst:
            dst.writestr('manifest.json', json.dumps(manifest, indent=2, ensure_ascii=False))
            for filename, payload in outputs:
                dst.writestr(filename, payload)
        return len(originals), len(prepared)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('output_zip', type=Path)
    args = parser.parse_args()
    old, new = repair(args.source_zip, args.output_zip)
    print(f'Repaired ZIP saved: {args.output_zip} ({old} -> {new} parts)')


if __name__ == '__main__':
    main()
