import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";
const source = fs.readFileSync(new URL("../src/shared-editing.ts", import.meta.url), "utf8");
const { needsEditableCopy, assetCharacterLayers } = await import(`data:text/javascript,${encodeURIComponent(stripTypeScriptTypes(source, { mode: "strip" }))}`);
test("imported wallpaper needs an editable project but own project does not", () => {
  assert.equal(needsEditableCopy({ assetId: "saved" }), true);
  assert.equal(needsEditableCopy({ assetId: "saved", projectId: "editable" }), false);
  assert.equal(needsEditableCopy({ projectId: "editable" }), false);
});
test("character-only shared wallpapers use every PNG via authenticated endpoints", () => {
  assert.deepEqual(assetCharacterLayers({ assetId: "saved", foregrounds: ["face", "hair"],
    exportSources: { foregrounds: ["/api/wallpapers/1/content/0", "/api/wallpapers/1/content/1"] } }), [
      { source: "face", endpoint: "/api/wallpapers/1/content/0" },
      { source: "hair", endpoint: "/api/wallpapers/1/content/1" },
    ]);
});
test("shared link character files also compose through first-party endpoints", () => {
  const sources = assetCharacterLayers({ foregrounds: ["face", "hair"],
    exportSources: { foregrounds: ["/api/wallpapers/links/token/content/0", "/api/wallpapers/links/token/content/1"] } });
  assert.equal(sources.length, 2);
  assert.equal(sources[1].endpoint, "/api/wallpapers/links/token/content/1");
});
test("background-subject assets follow their existing composite route", () => {
  assert.deepEqual(assetCharacterLayers({ assetId: "saved", background: "bg", foregrounds: ["fg"] }), []);
});
test("shared asset open/edit and studio direct link create independent projects", () => {
  const store = fs.readFileSync(new URL("../src/cloud-store.ts", import.meta.url), "utf8");
  const ws = fs.readFileSync(new URL("../src/Workspace.tsx", import.meta.url), "utf8");
  const studio = fs.readFileSync(new URL("../src/Studio.tsx", import.meta.url), "utf8");
  assert.match(store, /export async function prepareEditableProject/);
  assert.match(store, /new File\(\[image\]/);
  assert.match(ws, /const editable = open \? await editablePicture\(saved\)/);
  assert.match(studio, /편집용 복사본 만들기/);
});
