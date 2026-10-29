import type { Picture } from "./model.ts";

export function imageFeatures(picture: Pick<Picture, "features" | "kind" | "background" | "foregrounds" | "edit">): string[] {
  const features = new Set(picture.features ?? (picture.kind === "업스케일" ? ["업스케일"] : picture.kind === "배경 분리" ? ["배경 분리"] : picture.kind === "Live 2D" ? ["Live 2D"] : []));
  if (picture.background && picture.foregrounds?.length) features.add("배경 분리");
  if (picture.edit?.liveEnabled === true) features.add("Live 2D");
  if (picture.edit?.liveEnabled === false) features.delete("Live 2D");
  return ["업스케일", "배경 분리", "Live 2D"].filter(feature => features.has(feature));
}
