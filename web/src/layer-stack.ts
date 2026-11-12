import type { ApiLayer } from "./api";

// Every array in this module is ordered bottom -> top.

export function currentLayerIds(layers: readonly ApiLayer[]): string[] {
  return [...layers].sort((a, b) => a.layerOrder - b.layerOrder).map(layer => layer.id);
}

export function automaticLayerIds(layers: readonly ApiLayer[]): string[] {
  return [...layers].sort((a, b) =>
    (a.automaticOrder ?? a.layerOrder) - (b.automaticOrder ?? b.layerOrder) ||
    a.layerOrder - b.layerOrder).map(layer => layer.id);
}

export function isCompleteStack(ids: readonly string[], layers: readonly ApiLayer[]): boolean {
  return ids.length === layers.length && new Set(ids).size === ids.length &&
    layers.every(layer => ids.includes(layer.id));
}

export function moveLayer(ids: readonly string[], id: string, offset: -1 | 1): string[] {
  const from = ids.indexOf(id);
  const to = from + offset;
  if (from < 0 || to < 0 || to >= ids.length) return [...ids];
  const next = [...ids];
  [next[from], next[to]] = [next[to], next[from]];
  return next;
}
