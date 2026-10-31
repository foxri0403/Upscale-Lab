import test from 'node:test';
import assert from 'node:assert/strict';
import { currentLayerIds, automaticLayerIds, isCompleteStack, addLayerToTop, removeLayer, moveLayer } from '../src/layer-stack.ts';
const layers = [
  {id:'face', layerOrder:2, automaticOrder:1},
  {id:'hair', layerOrder:0, automaticOrder:2},
  {id:'clothes', layerOrder:1, automaticOrder:0},
];
test('server order vs original AI order are independent', () => {
  assert.deepEqual(currentLayerIds(layers), ['hair','clothes','face']);
  assert.deepEqual(automaticLayerIds(layers), ['clothes','face','hair']);
});
test('manual stack starts empty and appends new selections at the top', () => {
  const first = addLayerToTop([], 'clothes', layers);
  const second = addLayerToTop(first, 'face', layers);
  assert.deepEqual(second, ['clothes','face']);
  assert.deepEqual(addLayerToTop(second, 'face', layers), second);
  assert.deepEqual(addLayerToTop(second, 'unknown', layers), second);
  assert.equal(isCompleteStack(second, layers), false);
});
test('full stack is required to save', () => {
  assert.equal(isCompleteStack(['face','hair','clothes'], layers), true);
  assert.equal(isCompleteStack(['face','face','clothes'], layers), false);
  assert.equal(isCompleteStack(['face','hair','foreign'], layers), false);
});
test('moving a layer and removing it do not mutate caller order', () => {
  const ids=['clothes','face','hair'];
  assert.deepEqual(moveLayer(ids,'face', 1), ['clothes','hair','face']);
  assert.deepEqual(moveLayer(ids,'clothes', -1), ids);
  assert.deepEqual(removeLayer(ids,'face'), ['clothes','hair']);
  assert.deepEqual(ids,['clothes','face','hair']);
});
