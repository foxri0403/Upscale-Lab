#!/usr/bin/env python3
"""Batch-test safe generic v3 refinement on existing See-through ZIP archives.

Does not invoke RunPod, AWS or the See-through model. Works offline on PC.
Usage: python tools/batch_quality_test.py path/to/zip-or-directory --output reports/v3
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import sys
import zipfile

from PIL import Image, ImageDraw
import numpy as np

# Support execution from the repository root OR anywhere else.
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'backend' / 'ai-worker'))
from generic_quality_refiner import refine_layer_generic

MAX_UNCOMPRESSED = 160 * 1024 * 1024
MAX_IMAGE_PIXELS = 20_000_000
Image.MAX_IMAGE_PIXELS = MAX_IMAGE_PIXELS


def collect_inputs(paths: list[str], out: Path) -> list[Path]:
    archives: list[Path] = []
    for value in paths:
        path = Path(value).expanduser().resolve()
        if path.is_file() and path.suffix.lower() == '.zip':
            archives.append(path)
        elif path.is_dir():
            archives.extend(sorted(p.resolve() for p in path.rglob('*.zip')
                                   if not p.resolve().is_relative_to(out)))
        else:
            raise ValueError(f'ZIP 파일 또는 ZIP이 들어 있는 폴더가 아닙니다: {value}')
    unique = list(dict.fromkeys(archives))
    if not unique:
        raise ValueError('입력 ZIP 파일을 찾을 수 없습니다.')
    return unique


def safe_member(name: str) -> bool:
    p = PurePosixPath(name)
    return not p.is_absolute() and '..' not in p.parts and '\\' not in name


def read_package(path: Path) -> tuple[dict, list[tuple[dict, Image.Image]], str]:
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    with zipfile.ZipFile(path, 'r') as z:
        infos = z.infolist()
        if len(infos) > 100 or sum(f.file_size for f in infos) > MAX_UNCOMPRESSED:
            raise ValueError('ZIP 크기 또는 파일 개수 제한 초과')
        if not all(safe_member(f.filename) for f in infos):
            raise ValueError('ZIP 경로 안전성 검사 실패')
        if 'manifest.json' not in z.namelist():
            raise ValueError('manifest.json 파일 없음')
        manifest = json.loads(z.read('manifest.json'))
        layers = manifest.get('layers', [])
        if not isinstance(layers, list) or not 1 <= len(layers) <= 64:
            raise ValueError('manifest.layers는 1~64개 배열이어야 합니다')
        parts = []
        expected_size = None
        for i, layer in enumerate(layers):
            name = layer.get('file')
            if not isinstance(name, str) or not name.startswith('layers/') or not name.endswith('.png') or not safe_member(name):
                raise ValueError(f'잘못된 레이어 파일 경로: {name!r}')
            if name not in z.namelist():
                raise ValueError(f'레이어 PNG 누락: {name}')
            raw = z.read(name)
            with Image.open(io.BytesIO(raw)) as source:
                if source.width * source.height > MAX_IMAGE_PIXELS:
                    raise ValueError(f'이미지 픽셀 제한 초과: {name}')
                image = source.convert('RGBA')
            if expected_size is None:
                expected_size = image.size
            elif image.size != expected_size:
                raise ValueError('모든 파츠의 캔버스 크기가 동일해야 합니다')
            parts.append((layer, image))
        if len({layer['file'] for layer, _ in parts}) != len(parts):
            raise ValueError('중복 파일명 있음')
    return manifest, parts, digest


def checkerboard(size: tuple[int, int], step=18) -> Image.Image:
    img = Image.new('RGB', size, (242, 242, 242))
    draw = ImageDraw.Draw(img)
    for y in range(0, size[1], step):
        for x in range(0, size[0], step):
            if (x // step + y // step) % 2:
                draw.rectangle((x, y, min(size[0] - 1, x + step - 1),
                                min(size[1] - 1, y + step - 1)), fill=(201, 201, 201))
    return img


def composite(layers: list[tuple[dict, Image.Image]]) -> Image.Image:
    if not layers:
        raise ValueError('빈 레이어')
    result = Image.new('RGBA', layers[0][1].size, (0, 0, 0, 0))
    for _, img in sorted(layers, key=lambda entry: int(entry[0].get('layerOrder', 0))):
        result.alpha_composite(img)
    return result


def preview_side(image: Image.Image, width: int = 450, height: int = 450) -> Image.Image:
    resized = image.copy()
    resized.thumbnail((width, height), Image.Resampling.LANCZOS)
    back = checkerboard((width, height)).convert('RGBA')
    x = (width - resized.width) // 2
    y = (height - resized.height) // 2
    back.alpha_composite(resized, (x, y))
    return back.convert('RGB')


def save_preview(before: Image.Image, after: Image.Image, output: Path):
    a, b = preview_side(before), preview_side(after)
    panel = Image.new('RGB', (920, 485), 'white')
    panel.paste(a, (5, 32))
    panel.paste(b, (465, 32))
    draw = ImageDraw.Draw(panel)
    draw.text((8, 10), 'BEFORE', fill=(25, 25, 25))
    draw.text((468, 10), 'AFTER (generic v3)', fill=(25, 25, 25))
    panel.save(output, optimize=True)


def write_archive(path: Path, manifest: dict, parts: list[tuple[dict, Image.Image]], rows: list[dict]):
    enriched = json.loads(json.dumps(manifest, ensure_ascii=False))
    enriched.setdefault('diagnostics', {})['localGenericV3BatchTest'] = {
        'version': 'generic-v3-safe',
        'note': 'Offline test: existing ZIP layers were postprocessed; no fresh model inference.',
        'totalChangedAlphaPixels': sum(r['changedAlphaPixels'] for r in rows),
        'revertedLayers': sum(bool(r['reverted']) for r in rows),
    }
    with zipfile.ZipFile(path, 'w', compression=zipfile.ZIP_DEFLATED,
                         compresslevel=6) as z:
        z.writestr('manifest.json', json.dumps(enriched, ensure_ascii=False, indent=2))
        for meta, layer_img in parts:
            sink = io.BytesIO()
            layer_img.save(sink, format='PNG')
            z.writestr(meta['file'], sink.getvalue())


def process_one(path: Path, label: str, dest: Path, *, write_zip: bool):
    manifest, parts, sha256 = read_package(path)
    source_names = {
        record.get('file'): record.get('sourceLayerName', '')
        for record in manifest.get('diagnostics', {}).get('layers', [])
    }
    before = composite(parts)
    converted = []
    rows = []
    for info, image in parts:
        name = info['file']
        source_name = source_names.get(name) or info.get('semanticLayerType') or info.get('layerType') or ''
        semantic = info.get('semanticLayerType') or info.get('layerType') or ''
        updated, stats = refine_layer_generic(image, source_name, semantic)
        old_a = np.asarray(image.getchannel('A'))
        new_a = np.asarray(updated.getchannel('A'))
        removed = int(np.count_nonzero((old_a > 0) & (new_a == 0)))
        added = int(np.count_nonzero((old_a == 0) & (new_a > 0)))
        rows.append({
            'input': path.name, 'file': name, 'sourceName': source_name,
            'semanticType': semantic, 'changedAlphaPixels': stats['changedAlphaPixels'],
            'removedVisiblePixels': removed, 'addedVisiblePixels': added,
            'removedWeakSpeckles': stats['removedWeakSpeckles'],
            'repairedOpaquePinholes': stats['repairedOpaquePinholes'],
            'boostedFeaturePixels': stats['boostedFeaturePixels'],
            'changedVisibleFraction': stats['changedVisibleFraction'],
            'reverted': stats['reverted'], 'reason': stats['reason'],
        })
        converted.append((info, updated))
    after = composite(converted)
    preview = dest / f'{label}_before_after.png'
    save_preview(before, after, preview)
    if write_zip:
        write_archive(dest / f'{label}_generic-v3-test.zip', manifest, converted, rows)
    return {
        'input': str(path), 'inputSha256': sha256,
        'layers': len(parts), 'changedAlphaPixels': sum(r['changedAlphaPixels'] for r in rows),
        'removedVisiblePixels': sum(r['removedVisiblePixels'] for r in rows),
        'revertedLayers': sum(bool(r['reverted']) for r in rows),
        'preview': str(preview), 'layerRows': rows,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description='기존 See-through ZIP의 범용 v3 후처리를 일괄 비교 테스트 (RunPod 불필요)')
    parser.add_argument('inputs', nargs='+', help='ZIP 파일 또는 ZIP들이 들어 있는 폴더(여러 개 가능)')
    parser.add_argument('--output', '-o', required=True, help='검사 결과 폴더')
    parser.add_argument('--report-only', action='store_true', help='수정된 ZIP은 만들지 않고 비교 이미지/보고서만 생성')
    args = parser.parse_args(argv)
    output = Path(args.output).expanduser().resolve()
    output.mkdir(parents=True, exist_ok=True)
    try:
        archives = collect_inputs(args.inputs, output)
    except ValueError as exc:
        parser.error(str(exc))
    summary = []
    errors = []
    for index, path in enumerate(archives, 1):
        label = f'{index:03d}_{path.stem}'
        try:
            row = process_one(path, label, output, write_zip=not args.report_only)
            summary.append(row)
            print(f'[OK] {path.name}: 레이어 {row["layers"]}개, 알파 변경 {row["changedAlphaPixels"]:,}픽셀, 되돌림 {row["revertedLayers"]}개')
        except (ValueError, OSError, KeyError, json.JSONDecodeError, zipfile.BadZipFile) as exc:
            errors.append({'input': str(path), 'error': f'{type(exc).__name__}: {exc}'})
            print(f'[FAIL] {path.name}: {type(exc).__name__}: {exc}', file=sys.stderr)
    report = {'version': 'generic-v3-safe-batch', 'total': len(archives),
              'succeeded': len(summary), 'failed': len(errors),
              'results': summary, 'errors': errors}
    (output / 'quality_report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    headers = ['input', 'file', 'sourceName', 'semanticType', 'changedAlphaPixels',
               'removedVisiblePixels', 'addedVisiblePixels', 'removedWeakSpeckles',
               'repairedOpaquePinholes', 'boostedFeaturePixels',
               'changedVisibleFraction', 'reverted', 'reason']
    with (output / 'quality_report.csv').open('w', newline='', encoding='utf-8-sig') as f:
        writer = csv.DictWriter(f, fieldnames=headers)
        writer.writeheader()
        for item in summary:
            writer.writerows(item['layerRows'])
    print(f'\n결과 폴더: {output}')
    print('quality_report.csv / quality_report.json / *_before_after.png')
    if errors:
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
