import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";

// The helper uses a type-only import; Node strips it without requiring React or bundling.
const source = fs.readFileSync(new URL("../src/live-layer-preview.ts", import.meta.url), "utf8");
const js = stripTypeScriptTypes(source, { mode: "strip" });
const { livePreviewLayers, selectedLivePartId } =
  await import(`data:text/javascript,${encodeURIComponent(js)}`);
const layers = [
  { id: "eye", layerType: "Eye", layerOrder: 2 },
  { id: "body", layerType: "Body", layerOrder: 0 },
  { id: "face", layerType: "Face", layerOrder: 1 },
];

test("full view renders all layers in saved stacking order without changing the original", () => {
  assert.deepEqual(livePreviewLayers(layers, null).map(l => l.id), ["body", "face", "eye"]);
  assert.deepEqual(layers.map(l => l.id), ["eye", "body", "face"]);
});
test("clicking any part shows only its corresponding image", () => {
  assert.deepEqual(livePreviewLayers(layers, "face").map(l => l.id), ["face"]);
  assert.deepEqual(livePreviewLayers(layers, "eye").map(l => l.id), ["eye"]);
  assert.deepEqual(livePreviewLayers(layers, null).map(l => l.id), ["body", "face", "eye"]);
});
test("missing selection returns to full view after a project reload", () => {
  assert.equal(selectedLivePartId(layers, "deleted"), null);
  assert.equal(selectedLivePartId(layers, "body"), "body");
});
test("main studio and reorder dialog expose accessible part-only controls", () => {
  const studio = fs.readFileSync(new URL("../src/Studio.tsx", import.meta.url), "utf8");
  const canvas = fs.readFileSync(new URL("../src/StudioCanvas.tsx", import.meta.url), "utf8");
  const editor = fs.readFileSync(new URL("../src/LayerStackEditor.tsx", import.meta.url), "utf8");
  assert.match(studio, /setRequestedLivePartId\(layer.id\)/);
  assert.match(studio, /setRequestedLivePartId\(null\)[\s\S]*?<span>전체<\/span>/);
  assert.doesNotMatch(studio, /전체 합성 보기|파츠별 보기/);
  assert.match(canvas, /livePreviewLayers\(/);
  assert.match(editor, /layer-stack-preview-only/);
  assert.match(editor, /setRequestedPreviewPartId\(null\)/);
});
