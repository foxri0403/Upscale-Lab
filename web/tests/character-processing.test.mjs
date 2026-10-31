import test from 'node:test';
import assert from 'node:assert/strict';
import { separationOperation, separationAvailable } from '../src/character-processing.ts';

test('background mode continues using the original Layers operation', () => {
  assert.equal(separationOperation('background'), 'Layers');
});
test('character mode uses a separate API operation', () => {
  assert.equal(separationOperation('character'), 'CharacterLayers');
});
test('character mode cannot start if the separate endpoint is unavailable', () => {
  assert.equal(separationAvailable('character', null), false);
  assert.equal(separationAvailable('character', { layers: true }), false);
  assert.equal(separationAvailable('character', { layers: true, characterLayers: false }), false);
  assert.equal(separationAvailable('character', { layers: false, characterLayers: true }), true);
});
test('background mode preserves previous behavior and capability check', () => {
  assert.equal(separationAvailable('background', null), true);
  assert.equal(separationAvailable('background', { layers: true }), true);
  assert.equal(separationAvailable('background', { layers: false, characterLayers: true }), false);
});
