/** Determine whether an edited project should be frozen as a live layered wallpaper.
 * Character-only PNG stacks are valid; a Background PNG is not required.
 * The crop and filter are baked uniformly into every PNG by the server.
 */
export type WallpaperSource = {
  background?: string; foregrounds?: readonly string[]; layers?: readonly { layerType: string }[];
  assetId?: string;
};
export type WallpaperEdits = { foreground: boolean; filter: string; crop: string; scale: number };

export function wallpaperExportPolicy(picture: WallpaperSource, edit: WallpaperEdits) {
  const backgroundAndSubject = !!picture.background && !!picture.foregrounds?.length;
  const characterParts = !picture.background && ((picture.layers?.length ?? 0) >= 2 ||
    // Imported/saved Live2D wallpaper manifests have files but no project-layer rows.
    (!!picture.assetId && (picture.foregrounds?.length ?? 0) >= 2));
  const layered = edit.foreground && (backgroundAndSubject || characterParts);
  const rendered = edit.filter !== "original" || edit.crop !== "original" ||
    edit.scale !== 1 || (!layered && characterParts) || (backgroundAndSubject && !edit.foreground);
  return { layered, rendered, characterParts, mode: layered ? "Live2D" as const : "Static" as const };
}
