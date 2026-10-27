import test from "node:test";
import assert from "node:assert/strict";
import { isImageShared, projectSharing, sharingTargets } from "../src/image-sharing.ts";

test("both mobile publications and saved wallpaper versions lock editing", () => {
  assert.equal(isImageShared({}), false);
  assert.equal(isImageShared({ galleryPostId: "mobile" }), true);
  assert.equal(isImageShared({ sharedId: "wallpaper" }), true);
  assert.equal(isImageShared({ sharedAssetIds: ["a", "b"] }), true);
});

test("a project follows every shared version, while unrelated and private images stay editable", () => {
  const assets = [
    { id: "public-1", isPublic: true, sourceProjectId: "source" },
    { id: "public-2", isPublic: true, sourceProjectId: "source" },
    { id: "private", isPublic: false, sourceProjectId: "source" },
    { id: "other", isPublic: true, sourceProjectId: "other-source" },
  ];
  const picture = projectSharing({ projectId: "source" }, assets);
  assert.equal(isImageShared(picture), true);
  assert.deepEqual(sharingTargets(picture).assetIds, ["public-1", "public-2"]);
  assert.equal(isImageShared(projectSharing({ projectId: "source" }, assets.slice(2))), false);
  assert.equal(isImageShared(projectSharing({}, [{ id: "unrelated", isPublic: true }])), false);
});

test("turning sharing off targets all existing resources once and preserves the mobile deep link", () => {
  const picture = projectSharing({ projectId: "source", galleryPostId: "mobile-1",
    galleryPostIds: ["mobile-1", "mobile-2"] }, [
    { id: "wallpaper", isPublic: true, sourceProjectId: "source" },
  ]);
  assert.equal(picture.sharedId, "mobile-1");
  assert.deepEqual(sharingTargets(picture), {
    galleryIds: ["mobile-1", "mobile-2"], assetIds: ["wallpaper"],
  });
  assert.deepEqual(sharingTargets({ assetId: "wallpaper", sharedId: "wallpaper",
    sharedAssetIds: ["wallpaper"] }).assetIds, ["wallpaper"]);
});
