import test from "node:test";
import assert from "node:assert/strict";
import { canSelectUpscaleResolution, upscaleDimensions, upscaleResolutions } from "../src/upscale-resolution.ts";

test("FHD inputs produce the selected QHD and 4K pixel dimensions", () => {
  assert.deepEqual(upscaleDimensions(1920, 1080, 2560), { width: 2560, height: 1440 });
  assert.deepEqual(upscaleDimensions(1920, 1080, 3840), { width: 3840, height: 2160 });
  assert.deepEqual(upscaleResolutions.map(value => value.edge), [1920, 2560, 3840]);
});

test("resolution presets preserve portrait, square and non-widescreen aspect ratios", () => {
  assert.deepEqual(upscaleDimensions(1080, 1920, 2560), { width: 1440, height: 2560 });
  assert.deepEqual(upscaleDimensions(1536, 1024, 2560), { width: 2560, height: 1707 });
  assert.deepEqual(upscaleDimensions(960, 960, 3840), { width: 3840, height: 3840 });
});

test("unknown image dimensions and unsupported presets do not invent a target size", () => {
  assert.equal(upscaleDimensions(0, 1080, 2560), null);
  assert.equal(upscaleDimensions(1920, NaN, 2560), null);
  assert.equal(upscaleDimensions(1920, 1080, 4096), null);
});

test("completed FHD, QHD and 4K outputs allow same-resolution revisions, but do not permit downscaling", () => {
  for (const edge of [1920, 2560, 3840]) {
    assert.equal(canSelectUpscaleResolution(edge, edge, true), true);
    assert.equal(canSelectUpscaleResolution(edge, edge, false), false);
    assert.equal(canSelectUpscaleResolution(edge, edge + 10, true), false);
  }
  assert.equal(canSelectUpscaleResolution(1920, 1080, false), true);
  assert.equal(canSelectUpscaleResolution(1920, 0, true), false);
  assert.equal(canSelectUpscaleResolution(4096, 1920, true), false);
});
