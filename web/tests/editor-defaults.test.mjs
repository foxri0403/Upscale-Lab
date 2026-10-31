import assert from 'node:assert/strict';
import { test } from 'node:test';
import { defaultMotionStrength, motionStrength } from '../src/editor-defaults.ts';
test('new and legacy default motion strength is 50',()=>{
  assert.equal(defaultMotionStrength,50);assert.equal(motionStrength(),50);
  assert.equal(motionStrength({strength:57}),50);assert.equal(motionStrength({strength:57,motionDefaultsVersion:0}),50);
});
test('explicit motion changes stay intact including a newly chosen 57',()=>{
  assert.equal(motionStrength({strength:57,motionDefaultsVersion:1}),57);
  assert.equal(motionStrength({strength:72}),72);assert.equal(motionStrength({strength:0}),0);
});
