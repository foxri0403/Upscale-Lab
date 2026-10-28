import test from "node:test";
import assert from "node:assert/strict";
import { ownedExplorePicture, isOwnExplorePicture, wallpaperTargets } from "../src/explore-actions.ts";

const picture = { id: "public", name: "사진", src: "/fox.png", genre: "동물", created: "2026-10-28T00:00:00Z" };

test("own public images suppress saving even when an original is unavailable", () => {
  assert.equal(isOwnExplorePicture({ ...picture, ownerId: "me" }, "me", []), true);
  assert.equal(isOwnExplorePicture({ ...picture, ownerId: "other" }, "me", []), false);
});

test("web publication IDs resolve to the existing original before its frozen asset", () => {
  const original = { ...picture, id: "original", projectId: "project", sharedAssetIds: ["published"] };
  const frozen = { ...picture, id: "asset:published", assetId: "published" };
  assert.equal(ownedExplorePicture({ ...picture, assetId: "published" }, "me", [original, frozen]), original);
});

test("mobile posts resolve through owned gallery IDs without using another author's project", () => {
  const original = { ...picture, id: "original", galleryPostIds: ["post"] };
  assert.equal(ownedExplorePicture({ ...picture, ownerId: "me", galleryPostId: "post" }, "me", [original]), original);
  assert.equal(ownedExplorePicture({ ...picture, ownerId: "other", galleryPostId: "post", projectId: "original" }, "me", [original]), undefined);
});

test("matching titles, sources and foreign ownership do not grant access to private originals", () => {
  const original = { ...picture, id: "original", assetId: "same-id" };
  assert.equal(isOwnExplorePicture(picture, "me", [original]), false);
  assert.equal(ownedExplorePicture({ ...picture, ownerId: "other", assetId: "same-id" }, "me", [original]), undefined);
});

test("device targets exclude offline, disconnected and unsupported displays", () => {
  const display = { id: "screen", name: "화면", connected: true };
  const targets = wallpaperTargets([
    { id: "offline", name: "오프라인", online: false, displays: [display] },
    { id: "revoked", name: "연결 해제", online: true, connection: "disconnected", displays: [display] },
    { id: "pc", name: "PC", online: true, displays: [display,
      { ...display, id: "disconnected", connected: false }, { ...display, id: "unsupported", canApplyWallpaper: false }] },
  ]);
  assert.deepEqual(targets, [{ id: "pc/screen", deviceId: "pc", displayId: "screen", name: "PC · 화면" }]);
});

test("Android home and lock remain distinct targets on the same physical display", () => {
  assert.deepEqual(wallpaperTargets([{ id: "phone", name: "휴대폰", online: true, displays: [
    { id: "display:Home", name: "홈 화면", connected: true }, { id: "display:Lock", name: "잠금 화면", connected: true },
  ] }]).map(target => target.displayId), ["display:Home", "display:Lock"]);
});
