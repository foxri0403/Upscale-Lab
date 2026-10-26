import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  allTagsSelected,
  imageTags,
  matchesSelectedTags,
  selectableTags,
  tagSummary,
  unspecifiedTag,
} from "../src/image-tags.ts";

test("the selector includes every selectable tag from the mobile client", async () => {
  const dart = await readFile(
    new URL("../../mobile/lib/models/gallery_tags.dart", import.meta.url),
    "utf8",
  );
  const values = Array.from(
    dart
      .match(/const selectableGalleryTags = <String>\[([\s\S]*?)\];/)[1]
      .matchAll(/'([^']+)'/g),
    (x) => x[1],
  );
  assert.deepEqual([...selectableTags], values);
  assert.equal(selectableTags.includes(unspecifiedTag), false);
});
test("all tags can be selected; display is capped at five without dropping data", () => {
  const selected = [...selectableTags];
  assert.equal(allTagsSelected(selected), true);
  assert.equal(selected.length, 13);
  assert.equal(tagSummary(selected), selected.slice(0, 5).join(" · ") + " · …");
  assert.equal(selected.length, 13);
});
test("five tags are displayed without an ellipsis", () => {
  assert.equal(
    tagSummary(selectableTags.slice(0, 5)),
    selectableTags.slice(0, 5).join(" · "),
  );
});
test("no selection automatically filters unclassified images", () => {
  assert.equal(tagSummary([]), "장르 없음");
  assert.equal(allTagsSelected([]), false);
  assert.equal(
    matchesSelectedTags({ genre: unspecifiedTag, tags: [] }, []),
    true,
  );
  assert.equal(
    matchesSelectedTags({ genre: "동물", tags: ["동물"] }, []),
    false,
  );
});
test("select all includes unclassified images and partial selection uses any matching tag", () => {
  assert.equal(
    matchesSelectedTags({ genre: unspecifiedTag }, [...selectableTags]),
    true,
  );
  assert.equal(
    matchesSelectedTags({ genre: "애니", tags: ["애니", "동물"] }, [
      "게임",
      "동물",
    ]),
    true,
  );
});
test("unknown tags are never invented and explicit empty tags remain unclassified", () => {
  assert.deepEqual(imageTags({ genre: unspecifiedTag }), []);
  assert.deepEqual(imageTags({ genre: "풍경", tags: [] }), []);
  assert.deepEqual(
    imageTags({ genre: "풍경", tags: ["SF", "SF", "장르 설정되지 않음"] }),
    ["SF"],
  );
});
