import test from "node:test";
import assert from "node:assert/strict";
import { watchDeviceUpdates } from "../src/device-updates.ts";
import { mapDeviceLibrary } from "../src/device-state.ts";
import { galleryImages } from "../src/gallery-library.ts";

test("server notifications refresh immediately, unchanged cursors do not refetch, and unmount cancels the wait", async () => {
  const cursors = []; let refreshed = 0; let pending;
  const stop = watchDeviceUpdates(async (after, signal) => {
    cursors.push(after); pending = signal;
    if (cursors.length <= 2) return { cursor: "version-1" };
    return new Promise((_, reject) => signal.addEventListener("abort", () => reject(new Error("stopped")), { once: true }));
  }, async () => { refreshed++; });
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(cursors, [undefined, "version-1", "version-1"]);
  assert.equal(refreshed, 1); stop(); assert.equal(pending.aborted, true);
});

test("Android home and lock count as one physical display; extra screens retain their actual information", () => {
  const result = mapDeviceLibrary([{ id: "phone", name: "Phone", platform: "Android", recentlySeen: true,
    displays: [{ id: "built-in", width: 1080, height: 2400, isConnected: true, isPrimary: true, refreshRateHz: 119.88,
      wallpapers: [{ target: "Home", reportedAssetId: "a" }, { target: "Lock" }] },
      { id: "external", name: "External", width: 1920, height: 1080, isConnected: true, canApplyWallpaper: false,
        refreshRateHz: 59.94, wallpapers: [] },
      { id: "disconnected", width: 100, height: 100, isConnected: false, wallpapers: [] }] }]);
  assert.equal(result.devices[0].physicalDisplayCount, 2);
  assert.equal(result.devices[0].displays[0].refreshRateHz, 119.88);
  const external = result.devices[0].displays.find(d => d.nativeId === "external");
  assert.equal(external.refreshRateHz, 59.94); assert.equal(external.name, "External"); assert.equal(external.canApplyWallpaper, false);
});

test("applying the same project to several screens does not add duplicate My Images cards", () => {
  const project = { id: "p", projectId: "p", name: "Image" };
  const assets = [{ id: "asset:1", assetId: "1", sourceProjectId: "p" }, { id: "asset:2", assetId: "2", sourceProjectId: "p" }];
  assert.deepEqual(galleryImages([project], assets), [project]);
  assert.equal(assets.length, 2, "both immutable assets remain available to device previews");
});

test("imported wallpapers and saved versions whose source was deleted remain accessible", () => {
  const project = { id: "p", projectId: "p", name: "Same name" };
  const imported = { id: "import", assetId: "import", name: "Same name" };
  const orphan = { id: "orphan", assetId: "orphan", sourceProjectId: "deleted" };
  assert.deepEqual(galleryImages([project], [imported, orphan]), [project, imported, orphan]);
});
