import test from 'node:test';
import assert from 'node:assert/strict';
import { layerLabel } from '../src/layer-label.ts';
test('layer label names are in Korean, with an unknown label preserved', () => {
  assert.equal(layerLabel('BackHair'), '뒷머리');
  assert.equal(layerLabel('Eye'), '눈');
  assert.equal(layerLabel('Eyebrow'), '눈썹');
  assert.equal(layerLabel('Other'), '기타 파츠');
  assert.equal(layerLabel('NeverKnown'), 'NeverKnown');
});
