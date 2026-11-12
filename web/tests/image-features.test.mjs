import test from 'node:test';
import assert from 'node:assert/strict';
import { imageFeatures } from '../src/image-features.ts';

const parts = [{ layerType: 'BackPart' }, { layerType: 'Face' }];
test('character parts fix the old background-separation label even with motion off', () => {
  assert.deepEqual(imageFeatures({ features: ['배경 분리'], layers: parts, edit: { liveEnabled: false } }), ['Live 2D']);
});
test('rendered static character exports retain their server Live 2D feature', () => {
  assert.deepEqual(imageFeatures({ features: ['업스케일', 'Live 2D'], edit: { liveEnabled: false } }), ['업스케일', 'Live 2D']);
});
test('photo separation retains only its own label even when legacy motion is enabled', () => {
  const picture = { features: ['배경 분리'], background: '/back.png', foregrounds: ['/front.png'],
    layers: [{ layerType: 'Background' }, { layerType: 'Body' }] };
  assert.deepEqual(imageFeatures({ ...picture, edit: { liveEnabled: false } }), ['배경 분리']);
  assert.deepEqual(imageFeatures({ ...picture, edit: { liveEnabled: true } }), ['배경 분리']);
});
test('unprocessed images have no invented features and upscaled images retain theirs', () => {
  assert.deepEqual(imageFeatures({}), []);
  assert.deepEqual(imageFeatures({ kind: '업스케일' }), ['업스케일']);
});
