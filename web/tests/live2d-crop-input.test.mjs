import test from 'node:test';
import assert from 'node:assert/strict';
import { live2dCropRequired, croppedLive2dFileName, maxLive2dInputBytes } from '../src/live2d-crop-input.ts';

test('only selected crop regions need a new derived project before Live2D', () => {
  assert.equal(live2dCropRequired({crop:'original'}), false);
  for (const crop of ['wide','portrait','square','custom'])
    assert.equal(live2dCropRequired({crop}), true);
});
test('derived Live2D project has safe, recognizable file names', () => {
  assert.equal(croppedLive2dFileName('캐릭터/원본', 'portrait', 'png'), '캐릭터_원본-9-16-Live2D.png');
  assert.equal(croppedLive2dFileName('<테스트>', 'wide', 'jpg'), '_테스트_-16-9-Live2D.jpg');
  assert.equal(maxLive2dInputBytes, 16*1024*1024);
});
