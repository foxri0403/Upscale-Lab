#!/usr/bin/env python3
"""Add the v3 module COPY to the *existing* Dockerfile without rewriting it."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DOCKERFILE = ROOT / 'backend' / 'ai-worker' / 'runpod-see-through' / 'Dockerfile'
COPY_LINE = 'COPY generic_quality_refiner.py /opt/worker/generic_quality_refiner.py'
ANCHOR = 'COPY see_through_adapter.py /opt/worker/see_through_adapter.py'


def main():
    if not DOCKERFILE.is_file():
        raise SystemExit(f'Dockerfile을 찾을 수 없습니다: {DOCKERFILE}')
    data = DOCKERFILE.read_text(encoding='utf-8')
    if COPY_LINE in data:
        print('이미 v3 모듈 COPY가 반영되어 있습니다.')
        return
    if data.count(ANCHOR) != 1:
        raise SystemExit('기존 adapter COPY 라인이 없거나 여러 개입니다. 직접 확인해주세요. 기존 파일은 수정되지 않았습니다.')
    new = data.replace(ANCHOR, ANCHOR + '\n' + COPY_LINE, 1)
    DOCKERFILE.write_text(new, encoding='utf-8')
    print('Dockerfile에 v3 모듈 COPY 추가 완료:', DOCKERFILE)


if __name__ == '__main__':
    main()
