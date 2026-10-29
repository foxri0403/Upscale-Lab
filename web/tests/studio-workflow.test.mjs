import test from "node:test";
import assert from "node:assert/strict";
import { imageFeatures } from "../src/image-features.ts";
import { maxImageBytes, validateImageUpload } from "../src/image-upload.ts";

test("all applied features coexist and a disabled animation remains separated", () => {
  const picture = { features: ["업스케일", "배경 분리"], edit: { liveEnabled: true } };
  assert.deepEqual(imageFeatures(picture), ["업스케일", "배경 분리", "Live 2D"]);
  assert.deepEqual(imageFeatures({ ...picture, features: ["업스케일", "배경 분리", "Live 2D"], edit: { liveEnabled: false } }), ["업스케일", "배경 분리"]);
});

test("background mouse and gyro selections do not identify the separate Live 2D feature", () => {
  assert.deepEqual(imageFeatures({ kind: "배경 분리", edit: { mouseEnabled: true, gyroEnabled: true, liveEnabled: false } }), ["배경 분리"]);
  assert.deepEqual(imageFeatures({ features: ["배경 분리"], edit: { mouseEnabled: false, gyroEnabled: true } }), ["배경 분리"]);
  assert.deepEqual(imageFeatures({ kind: "Live 2D", edit: { liveEnabled: true } }), ["Live 2D"]);
});
test("image validation accepts broad extensions and exactly 50MB, with strict limits", () => {
  for (const extension of ["jpeg", "PNG", "gif", "webp", "heif", "heic", "avif", "tiff", "bmp", "dng", "jp2"])
    assert.match(validateImageUpload({ name: `image.${extension}`, type: "", size: maxImageBytes }), /^image\//);
  assert.throws(() => validateImageUpload({ name: "image.png", type: "image/png", size: maxImageBytes + 1 }), /50MB/);
  assert.throws(() => validateImageUpload({ name: "image.png", type: "image/png", size: 0 }));
  assert.throws(() => validateImageUpload({ name: "image.svg", type: "image/svg+xml", size: 10 }));
  assert.throws(() => validateImageUpload({ name: "image.png", type: "text/html", size: 10 }));
});
