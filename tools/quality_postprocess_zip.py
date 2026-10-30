"""CPU-only quality cleanup for an already-completed See-through ZIP.

Run from the Upscale-Lab repository root:
  python tools/quality_postprocess_zip.py existing.zip quality-fixed.zip

Requires: Pillow, numpy. Does not contact AWS/RunPod or change the input ZIP.
Requires the matching backend/ai-worker/see_through_adapter.py in the repo.
"""
from __future__ import annotations
import argparse
import ast
import io
import json
from pathlib import Path
import re
import zipfile
import numpy as np
from PIL import Image


def _load_postprocessor():
    adapter = Path(__file__).resolve().parents[1] / 'backend/ai-worker/see_through_adapter.py'
    tree = ast.parse(adapter.read_text(encoding='utf-8'), filename=str(adapter))
    funcs = [node for node in tree.body if isinstance(node, ast.FunctionDef)
             and node.name == 'postprocess_layer_rgba']
    if len(funcs) != 1:
        raise RuntimeError('Expected postprocess_layer_rgba in see_through_adapter.py')
    namespace = {'Image': Image, 'np': np, 're': re, '__name__': 'layer_quality'}
    exec(compile(ast.Module(body=funcs, type_ignores=[]), str(adapter), 'exec'), namespace)
    return namespace['postprocess_layer_rgba']


def process_zip(source: Path, destination: Path) -> list[dict]:
    if source.resolve() == destination.resolve():
        raise ValueError('Input and output must be different files')
    cleanup = _load_postprocessor()
    with zipfile.ZipFile(source) as src:
        infos = src.infolist()
        if len(infos) > 70 or sum(i.file_size for i in infos) > 180*1024*1024:
            raise ValueError('Unexpected ZIP size or file count')
        manifest = json.loads(src.read('manifest.json'))
        layers = manifest.get('layers', [])
        diagnostics = manifest.get('diagnostics', {})
        names = {entry.get('file'): entry.get('sourceLayerName', '')
                 for entry in diagnostics.get('layers', [])}
        if not 1 <= len(layers) <= 64:
            raise ValueError('Invalid number of See-through layers')
        files = [entry.get('file') for entry in layers]
        if any(not isinstance(name, str) or not re.fullmatch(r'layers/[0-9]{3}\.png', name)
               for name in files) or len(set(files)) != len(files):
            raise ValueError('Unexpected layer paths')
        if any(f not in names for f in files):
            raise ValueError('Missing sourceLayerName from diagnostics')
        if diagnostics.get('qualityPostprocess'):
            raise ValueError('Input already has qualityPostprocess. Use original ZIP to avoid double processing.')
        info_map = {entry.get('file'): entry for entry in diagnostics.get('layers', [])}
        outputs = []
        results = []
        for name in files:
            raw = src.read(name)
            if len(raw) > 24 * 1024 * 1024:
                raise ValueError('Unexpectedly large layer')
            with Image.open(io.BytesIO(raw)) as im:
                rgba = im.convert('RGBA')
            if rgba.width > 4096 or rgba.height > 4096:
                raise ValueError('Unexpected layer dimensions')
            processed, stats = cleanup(rgba, names[name])
            blob = io.BytesIO()
            processed.save(blob, 'PNG')
            outputs.append((name, blob.getvalue()))
            info_map[name]['qualityCleanup'] = stats
            info_map[name]['export']['alphaMin'] = processed.getchannel('A').getextrema()[0]
            info_map[name]['export']['alphaMax'] = processed.getchannel('A').getextrema()[1]
            info_map[name]['export']['hasTransparency'] = processed.getchannel('A').getextrema()[0] < 255
            results.append({'file': name, **stats})
            print(f'{name}: source={names[name]} changed={stats.get("changedAlphaPixels",0)} '
                  f'leg_gap_removed={stats.get("legGapRemovedPixels",0)}')
        diagnostics['qualityPostprocess'] = {
            'version': 'conservative-v1',
            'note': 'CPU-only cleanup, no model inference; not an automatic rigging solution.'
        }
        destination.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(destination, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as out:
            out.writestr('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2))
            for filename, payload in outputs:
                out.writestr(filename, payload)
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source_zip', type=Path)
    parser.add_argument('output_zip', type=Path)
    args = parser.parse_args()
    results = process_zip(args.source_zip, args.output_zip)
    print('Saved:', args.output_zip)
    print('Layers:', len(results), 'changed pixels:', sum(r.get('changedAlphaPixels',0) for r in results))


if __name__ == '__main__':
    main()
