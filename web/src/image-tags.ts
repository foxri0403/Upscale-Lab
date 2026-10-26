import type { Picture } from "./model.ts";

// Match mobile/lib/models/gallery_tags.dart; unclassified is a state, not an option.
export const selectableTags = [
  "게임",
  "남자",
  "동물",
  "레트로",
  "밀리터리",
  "비행기",
  "스포츠",
  "애니",
  "여자",
  "자동차",
  "풍경",
  "픽셀 아트",
  "SF",
] as const;
export const unspecifiedTag = "장르 설정되지 않음";

export function imageTags(picture: Pick<Picture, "tags" | "genre">): string[] {
  const values = picture.tags ?? [picture.genre];
  return [
    ...new Set(
      values
        .map((t) => t?.trim())
        .filter(
          (t): t is string => !!t && t !== unspecifiedTag && t !== "장르 없음",
        ),
    ),
  ];
}
export function tagSummary(tags: readonly string[]): string {
  return tags.length
    ? tags.slice(0, 5).join(" · ") + (tags.length > 5 ? " · …" : "")
    : "장르 없음";
}
export function allTagsSelected(selected: readonly string[]): boolean {
  return selectableTags.every((t) => selected.includes(t));
}
export function matchesSelectedTags(
  picture: Picture,
  selected: readonly string[],
): boolean {
  if (allTagsSelected(selected)) return true;
  const tags = imageTags(picture);
  return selected.length
    ? tags.some((t) => selected.includes(t))
    : tags.length === 0;
}
