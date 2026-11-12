import type { ApiLayer } from "./api";

/** The preview selection is UI-only; it never modifies the saved layer order. */
export function selectedLivePartId(layers: readonly ApiLayer[], requestedId: string | null): string | null {
  return requestedId && layers.some(layer => layer.id === requestedId) ? requestedId : null;
}

/** Stacking order is bottom-to-top. One selected layer retains its full-canvas position. */
export function livePreviewLayers(layers: readonly ApiLayer[], selectedId: string | null): ApiLayer[] {
  const ordered = [...layers].sort((a, b) => a.layerOrder - b.layerOrder);
  return selectedId ? ordered.filter(layer => layer.id === selectedId) : ordered;
}
