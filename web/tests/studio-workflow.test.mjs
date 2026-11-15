import test from "node:test";
import assert from "node:assert/strict";
import { imageFeatures } from "../src/image-features.ts";
import { imageAccept, maxImageBytes, validateImageUpload, validateStudioImageUpload } from "../src/image-upload.ts";

test("upscaling can coexist with either separation technique and legacy badges never show both", () => {
  const picture = { features: ["업스케일", "배경 분리"], edit: { liveEnabled: true } };
  assert.deepEqual(imageFeatures(picture), ["업스케일", "배경 분리"]);
  assert.deepEqual(imageFeatures({ ...picture, features: ["업스케일", "배경 분리", "Live 2D"] }), ["업스케일", "배경 분리"]);
  assert.deepEqual(imageFeatures({ ...picture, features: ["업스케일", "Live 2D"], edit: { liveEnabled: false } }), ["업스케일", "Live 2D"]);
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

test("studio blocks animation detected by file name, MIME type or container header", async () => {
  assert.doesNotMatch(imageAccept, /gif|heics|heifs/i);
  const gif = new File(["GIF89a"], "renamed.png", { type: "image/png" });
  const pngSignature = Uint8Array.of(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a);
  const apng = new File([pngSignature, "\0\0\0\0tEXt\0\0\0\0", "\0\0\0\bacTL", new Uint8Array(12)], "animated.png");
  const webp = new File(["RIFF\0\0\0\0WEBPVP8X\x0a\0\0\0\x02"], "animated.webp");
  const avif = new File(["\0\0\0\x18ftypavif\0\0\0\0avifavis"], "animated.avif");
  const heif = new File(["\0\0\0\x18ftypheic\0\0\0\0mif1msf1"], "animated.heic");
  for (const file of [gif, apng, webp, avif, heif, new File(["bytes"], "file.GIF"),
    new File(["bytes"], "file.png", { type: "image/apng" })]) {
    await assert.rejects(validateStudioImageUpload(file), /움직이는 이미지/, file.name);
  }
});

test("studio keeps still variants of animation-capable formats eligible", async () => {
  const stillPng = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6ZkAAAAASUVORK5CYII=", "base64");
  const stillWebpHeader = "RIFF\0\0\0\0WEBPVP8X\x0a\0\0\0\x10";
  const stillAvifHeader = "\0\0\0\x18ftypavif\0\0\0\0avifmif1";
  for (const file of [new File([stillPng], "still.png"), new File([stillWebpHeader], "still.webp"),
    new File([stillAvifHeader], "still.avif")]) {
    await assert.doesNotReject(validateStudioImageUpload(file), file.name);
  }
});
