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
