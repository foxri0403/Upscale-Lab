"""Small local smoke test: python tools/test_quality_postprocess.py"""
from __future__ import annotations
import ast
import re
from pathlib import Path
import numpy as np
from PIL import Image

adapter = Path(__file__).resolve().parents[1] / 'backend/ai-worker/see_through_adapter.py'
tree = ast.parse(adapter.read_text(encoding='utf-8'), filename=str(adapter))
func = next(x for x in tree.body if isinstance(x, ast.FunctionDef) and x.name == 'postprocess_layer_rgba')
ns = {'Image':Image, 'np':np, 're':re}
exec(compile(ast.Module(body=[func],type_ignores=[]),str(adapter),'exec'), ns)
cleanup = ns['postprocess_layer_rgba']

# Feature strokes are not erased even when faint.
a=np.zeros((180,180,4),dtype=np.uint8)
a[30:32,25:70,:3] = (45,30,15)
a[30:32,25:70,3] = 60
before=int(a[:,:,3].sum())
out, stats=cleanup(Image.fromarray(a,'RGBA'),'eyebrow')
assert int(np.asarray(out)[:,:,3].sum()) >= before
assert np.count_nonzero(np.asarray(out)[:,:,3]) == 90

# A disconnected, low-confidence point is removed, but main feature stays.
a=np.zeros((180,180,4),dtype=np.uint8)
a[20:110,20:120,:] = (20,30,40,255)
a[150,155,:] = (255,255,255,20)
out, stats=cleanup(Image.fromarray(a,'RGBA'),'topwear')
b=np.asarray(out)
assert b[150,155,3] == 0
assert b[50,50,3] == 255

# Non-legwear must never run a leg-gap opacity suppression.
shape=np.zeros((250,220,4),dtype=np.uint8)
shape[20:245,30:80,:] = (25,20,20,255)
shape[20:245,140:190,:] = (25,20,20,255)
shape[20:245,80:140,:] = (120,120,120,160)
clothes,_=cleanup(Image.fromarray(shape,'RGBA'),'topwear')
legs,stats=cleanup(Image.fromarray(shape,'RGBA'),'legwear')
assert np.asarray(clothes)[200,100,3] > 0
assert np.asarray(legs)[200,100,3] == 0,stats
assert stats['legGapRows'] >= 24
print('Smoke tests passed: thin features, speckle cleanup, conditional leg gap.')
