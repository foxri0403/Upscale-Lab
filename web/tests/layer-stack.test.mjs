import test from 'node:test';
import assert from 'node:assert/strict';
import { currentLayerIds, automaticLayerIds, isCompleteStack, moveLayer } from '../src/layer-stack.ts';
const layers = [
  {id:'face', layerOrder:2, automaticOrder:1},
  {id:'hair', layerOrder:0, automaticOrder:2},
  {id:'clothes', layerOrder:1, automaticOrder:0},
];
test('server order vs original AI order are independent', () => {
  assert.deepEqual(currentLayerIds(layers), ['hair','clothes','face']);
  assert.deepEqual(automaticLayerIds(layers), ['clothes','face','hair']);
});
test('full stack is required to save', () => {
  assert.equal(isCompleteStack(['face','hair','clothes'], layers), true);
  assert.equal(isCompleteStack(['face','hair'], layers), false);
  assert.equal(isCompleteStack(['face','face','clothes'], layers), false);
  assert.equal(isCompleteStack(['face','hair','foreign'], layers), false);
});
test('moving a layer retains every part and does not mutate caller order', () => {
  const ids=['clothes','face','hair'];
  const moved = moveLayer(ids,'face', 1);
  assert.deepEqual(moved, ['clothes','hair','face']);
  assert.equal(isCompleteStack(moved, layers), true);
  assert.deepEqual(moveLayer(ids,'clothes', -1), ids);
  assert.deepEqual(moveLayer(ids,'unknown', 1), ids);
  assert.deepEqual(ids,['clothes','face','hair']);
});
