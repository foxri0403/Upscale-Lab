/** Map the Studio's local choice to the authenticated project-processing API. */
export type SeparationMode = "background" | "character";
export function separationOperation(mode: SeparationMode): "Layers" | "CharacterLayers" {
  return mode === "character" ? "CharacterLayers" : "Layers";
}
export function separationAvailable(
  mode: SeparationMode,
  capabilities: { layers: boolean; characterLayers?: boolean } | null,
): boolean {
  if (mode === "character") return capabilities?.characterLayers === true;
  return capabilities?.layers !== false;
}

/** A See-through result can contain its own background PNG. A semantic face/hair
 * part distinguishes it from the ordinary two-layer subject separation.
 * The existing CharacterLayers/RunPod model is used for both photo & illustration. */
export function hasSeeThroughRigLayers(layers?: readonly { layerType: string }[], features?: readonly string[]): boolean {
  if (!layers || layers.length < 2) return false;
  if (features?.includes("Live 2D") && !features.includes("배경 분리")) return true;
  if (!layers.some(layer => layer.layerType === "Background")) return true;
  const faceOrHair = new Set(["Face", "Eye", "Eyebrow", "Mouth", "Nose", "Hair", "FrontHair", "BackHair"]);
  return layers.some(layer => faceOrHair.has(layer.layerType));
}
