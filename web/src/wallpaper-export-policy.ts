/** Determine whether an edited project should be frozen as a live layered wallpaper.
 * Character-only PNG stacks are valid; a Background PNG is not required.
 * The crop and filter are baked uniformly into every PNG by the server.
 */
export type WallpaperSource = {
  background?: string; foregrounds?: readonly string[]; layers?: readonly { layerType: string }[];
  features?: readonly string[];
  assetId?: string;
};
export type WallpaperEdits = { foreground: boolean; filter: string; crop: string; scale: number };

export function wallpaperExportPolicy(picture: WallpaperSource, edit: WallpaperEdits) {
  const backgroundAndSubject = !!picture.background && !!picture.foregrounds?.length;
  const projectLayers = picture.layers ?? [];
  // See-through may return a Background PNG alongside the semantic character parts.
  // Do not confuse a plain Background + Body split with a riggable character.
  const hasBackgroundLayer = projectLayers.some(layer => layer.layerType === "Background");
  const rigParts = new Set(["Face", "Eye", "Eyebrow", "Mouth", "Nose", "Hair", "FrontHair", "BackHair"]);
  const hasFaceOrHair = projectLayers.some(layer => rigParts.has(layer.layerType));
  const isLive2D = picture.features?.includes("Live 2D") && !picture.features.includes("배경 분리");
  const characterParts = projectLayers.length >= 2
    ? ((!picture.background && !hasBackgroundLayer) || hasFaceOrHair || isLive2D === true)
    // Imported/saved Live2D wallpaper manifests have PNGs but no project-layer rows.
    : (projectLayers.length === 0 && !!picture.assetId && (picture.foregrounds?.length ?? 0) >= 2);
  const layered = edit.foreground && (backgroundAndSubject || characterParts);
  const rendered = edit.filter !== "original" || edit.crop !== "original" ||
    edit.scale !== 1 || (!layered && characterParts) || (backgroundAndSubject && !edit.foreground);
  return { layered, rendered, characterParts, mode: layered ? "Live2D" as const : "Static" as const };
}
