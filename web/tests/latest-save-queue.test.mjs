import test from 'node:test';
import assert from 'node:assert/strict';
import { LatestSaveQueue } from '../src/latest-save-queue.ts';

test('rapid crop changes persist only the latest selection', async () => {
  const writes=[], states=[];
  const queue=new LatestSaveQueue(async value=>{writes.push(value);}, value=>states.push(value), error=>{throw error;},60000);
  queue.enqueue('a',1);queue.enqueue('a',2);queue.enqueue('a',3);
  await queue.flush();
  assert.deepEqual(writes,[3]);assert.equal(states.at(-1),false);
});
test('a slow old write is followed by the latest crop and never overwrites it afterward', async () => {
  const starts=[], completed=[];let release;
  const gate=new Promise(resolve=>{release=resolve;});
  const queue=new LatestSaveQueue(async value=>{starts.push(value);if(value===1)await gate;completed.push(value);},()=>{},error=>{throw error;},60000);
  queue.enqueue('a',1);const flushing=queue.flush();
  queue.enqueue('a',2);queue.enqueue('a',3);
  assert.deepEqual(starts,[1]);release();await flushing;
  assert.deepEqual(completed,[1,3]);
});
test('switching images retains the pending crop for both images', async () => {
  const writes=[];
  const queue=new LatestSaveQueue(async value=>{writes.push(value);},()=>{},error=>{throw error;},60000);
  queue.enqueue('a',{image:'a',crop:1});queue.enqueue('b',{image:'b',crop:2});queue.enqueue('a',{image:'a',crop:3});
  await queue.flush();assert.deepEqual(writes,[{image:'a',crop:3},{image:'b',crop:2}]);
});
test('a failed save is reported, does not claim persistence, and a later edit can save', async () => {
  const writes=[], failures=[], states=[];
  const queue=new LatestSaveQueue(async value=>{if(value===1)throw new Error('offline');writes.push(value);},v=>states.push(v),e=>failures.push(e.message),60000);
  queue.enqueue('a',1);await queue.flush();
  assert.deepEqual(writes,[]);assert.deepEqual(failures,['offline']);assert.equal(states.at(-1),false);
  queue.enqueue('a',2);await queue.flush();assert.deepEqual(writes,[2]);
});
