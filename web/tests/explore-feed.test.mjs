import test from "node:test";
import assert from "node:assert/strict";
import { galleryPicture, mergeExploreFeed } from "../src/explore-feed.ts";
import { matchesSelectedTags, selectableTags } from "../src/image-tags.ts";

const post = {
  id: "mobile-post",
  userId: "another-account",
  username: "mobile-author",
  title: "모바일 공유 이미지",
  imageUrl: "https://example.invalid/original.jpg",
  projectId: "another-accounts-project",
  isPublic: true,
  tag: "애니",
  tags: ["애니", "동물"],
  downloadCount: 12,
  createdAt: "2026-10-20T00:00:00Z",
};

test("mobile posts remain visible when the new wallpaper feed is empty", () => {
  const feed = mergeExploreFeed([], [post]);
  assert.equal(feed.length, 1);
  assert.equal(feed[0].name, post.title);
  assert.equal(feed[0].src, post.imageUrl);
  assert.equal(feed[0].creator, post.username);
  assert.equal(feed[0].downloads, 12);
});

test("shared gallery access does not grant ownership of the author's project", () => {
  const picture = galleryPicture(post);
  assert.equal(picture.galleryPostId, post.id);
  assert.equal(picture.ownerId, post.userId);
  assert.equal(picture.projectId, undefined);
  assert.equal(picture.assetId, undefined);
});

test("private mobile posts are excluded even if supplied by an API response", () => {
  assert.deepEqual(mergeExploreFeed([], [{ ...post, isPublic: false }]), []);
});

test("legacy and new resources with the same UUID are distinct entries", () => {
  const wallpaper = {
    id: "asset:mobile-post",
    assetId: post.id,
    name: "웹 배경화면",
    src: "/web.png",
    genre: "풍경",
    created: post.createdAt,
  };
  const feed = mergeExploreFeed([wallpaper], [post]);
  assert.equal(feed.length, 2);
  assert.deepEqual(
    new Set(feed.map((p) => p.id)),
    new Set(["asset:mobile-post", "gallery:mobile-post"]),
  );
});

test("all of a mobile post's genres participate in filtering", () => {
  const picture = galleryPicture(post);
  assert.equal(matchesSelectedTags(picture, ["애니"]), true);
  assert.equal(matchesSelectedTags(picture, ["동물"]), true);
  assert.equal(matchesSelectedTags(picture, ["풍경"]), false);
  assert.equal(matchesSelectedTags(picture, [...selectableTags]), true);
});

test("older single-tag posts keep their genre instead of becoming landscapes", () => {
  assert.equal(
    galleryPicture({ ...post, tags: undefined, tag: "게임" }).genre,
    "게임",
  );
  assert.equal(
    galleryPicture({ ...post, tags: [], tag: "" }).genre,
    "장르 설정되지 않음",
  );
});

test("repeated posts are deduplicated without removing other authors' posts", () => {
  const feed = mergeExploreFeed(
    [],
    [post, post, { ...post, id: "second-post", userId: "third-account" }],
  );
  assert.equal(feed.length, 2);
});
