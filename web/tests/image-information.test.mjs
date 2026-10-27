import test from "node:test";
import assert from "node:assert/strict";
import { imageFileFormat, imageResolution } from "../src/image-information.ts";

test("image information prefers actual API or data encoding over the file extension", () => {
  assert.equal(imageFileFormat("https://images.test/renamed.jpg?signature=png", "image/png"), "PNG");
  assert.equal(imageFileFormat("https://images.test/renamed.png", "IMAGE/JPEG; charset=binary"), "JPEG");
  assert.equal(imageFileFormat("data:image/jpeg;base64,aGVsbG8=", "image/png"), "JPEG");
  assert.equal(imageFileFormat("https://images.test/object", "image/webp"), "WebP");
});

test("legacy image URLs use only the source path, excluding signed query strings", () => {
  assert.equal(imageFileFormat("https://images.test/image.PNG?signature=.jpg"), "PNG");
  assert.equal(imageFileFormat("/images/fox.png"), "PNG");
  assert.equal(imageFileFormat("https://images.test/object?filename=photo.jpg"), "정보 없음");
  assert.equal(imageFileFormat("blob:https://images.test/unknown-id"), "정보 없음");
  assert.equal(imageFileFormat("https://images.test/object", "application/octet-stream"), "정보 없음");
});

test("resolution shows real pixel dimensions and rejects invalid or missing metadata", () => {
  assert.equal(imageResolution(3840, 2160), "3,840 × 2,160 px");
  for (const [width, height] of [[undefined, 2160], [3840, 0], [-1, 10], [NaN, 10], [Infinity, 10], [1.5, 10]])
    assert.equal(imageResolution(width, height), null);
});
