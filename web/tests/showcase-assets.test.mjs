import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createHash } from "node:crypto";

const manifest = JSON.parse(readFileSync(new URL("../src/showcase-character-data.json", import.meta.url), "utf8"));
const sha256 = bytes => createHash("sha256").update(bytes).digest("hex");

test("showcase contains the real completed See-through layer stack and matching source artwork", () => {
  assert.equal(manifest.layers.length, 12);
  assert.deepEqual(manifest.layers.map(layer => layer.order), Array.from({ length: 12 }, (_, index) => index));
  assert.equal(new Set(manifest.layers.map(layer => layer.file)).size, manifest.layers.length);
  assert.ok(manifest.layers.some(layer => layer.name === "irides"));
  assert.ok(manifest.layers.some(layer => layer.semanticType === "FrontHair"));
  assert.ok(manifest.layers.some(layer => layer.semanticType === "BackHair"));
  assert.ok(manifest.layers.some(layer => layer.semanticType === "Clothes"));
  const source = readFileSync(new URL(`../public/images/${manifest.provenance.source}`, import.meta.url));
  assert.equal(sha256(source), manifest.provenance.sourceSha256);
});

test("every rendered part is its own verified RGBA PNG on the common model canvas", () => {
  for (const layer of manifest.layers) {
    assert.match(layer.file, /^\d{3}\.png$/);
    const png = readFileSync(new URL(`../public/images/live2d-character-parts/${layer.file}`, import.meta.url));
    assert.equal(png.subarray(0, 8).toString("hex"), "89504e470d0a1a0a");
    assert.equal(png.readUInt32BE(16), manifest.width);
    assert.equal(png.readUInt32BE(20), manifest.height);
    assert.equal(png[25], 6, "model parts must preserve RGBA transparency");
    assert.equal(sha256(png), layer.sha256, layer.file);
  }
});
