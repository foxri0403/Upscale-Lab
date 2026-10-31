export type LayerPreview = "all" | "background" | "subject";
export function visibleLayers(selection: LayerPreview) {
  return { background: selection !== "subject", subject: selection !== "background" };
}
