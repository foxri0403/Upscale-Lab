import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";
const src = fs.readFileSync(new URL("../src/wallpaper-export-policy.ts", import.meta.url), "utf8");
const { wallpaperExportPolicy } = await import(`data:text/javascript,${encodeURIComponent(stripTypeScriptTypes(src, { mode: "strip" }))}`);
const base = { crop: "portrait", filter: "original", scale: 1, foreground: true };
test("character-only PNG layers stay Live2D when cropped for mobile", () => {
  const result = wallpaperExportPolicy({ layers: [{ layerType: "Face" }, { layerType: "FrontHair" }] }, base);
  assert.equal(result.mode, "Live2D"); assert.equal(result.layered, true);
  assert.equal(result.rendered, true);
});
test("background/subject retains existing Live2D path", () => {
  assert.equal(wallpaperExportPolicy({ background: "bg", foregrounds: ["fg"] }, base).mode, "Live2D");
});
test("disabling foreground deliberately creates a static export", () => {
  assert.equal(wallpaperExportPolicy({ layers: [{ layerType: "Face" }, { layerType: "Eye" }] },
    { ...base, foreground: false }).mode, "Static");
});
test("regular unlayered images remain static", () => {
  assert.equal(wallpaperExportPolicy({}, base).mode, "Static");
});
test("character-only saved wallpaper can be snapshotted without flattening", () => {
  const saved = wallpaperExportPolicy({ assetId: "asset-1", foregrounds: ["face", "hair"] }, base);
  assert.equal(saved.mode, "Live2D"); assert.equal(saved.layered, true);
});
