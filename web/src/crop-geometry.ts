export type CropRegion = { x: number; y: number; width: number; height: number };
export type CropEdit = { crop: string; cropRegion?: CropRegion };
export const cropPresets = [
  ["original", "원본 비율"], ["wide", "16:9 PC"], ["portrait", "9:16 모바일"],
  ["square", "1:1 정사각형"], ["custom", "사용자 비율"],
] as const;
const clamp = (n: number, min: number, max: number) => Math.max(min, Math.min(max, n));
export function validCrop(region?: CropRegion): region is CropRegion {
  return !!region && Object.values(region).every(Number.isFinite) && region.x >= 0 && region.y >= 0 &&
    region.width > 0 && region.height > 0 && region.x + region.width <= 1 + 1e-8 && region.y + region.height <= 1 + 1e-8;
}
export function fitCrop(width: number, height: number, ratio: number, center = { x: 0.5, y: 0.5 }): CropRegion {
  const w = Math.min(1, height * ratio / width), h = Math.min(1, width / ratio / height);
  return { x: clamp(center.x - w / 2, 0, 1 - w), y: clamp(center.y - h / 2, 0, 1 - h), width: w, height: h };
}
export function cropRegion(width: number, height: number, edit: CropEdit): CropRegion {
  if (edit.crop === "original" || width <= 0 || height <= 0) return { x: 0, y: 0, width: 1, height: 1 };
  if (validCrop(edit.cropRegion)) return { ...edit.cropRegion };
  const ratio = ({ wide: 16 / 9, portrait: 9 / 16, square: 1 } as Record<string, number>)[edit.crop] || width / height;
  return fitCrop(width, height, ratio);
}
export function pixelCrop(width: number, height: number, edit: CropEdit) {
  const region = cropRegion(width, height, edit);
  const x = clamp(Math.round(region.x * width), 0, width - 1), y = clamp(Math.round(region.y * height), 0, height - 1);
  return { x, y, width: clamp(Math.round(region.width * width), 1, width - x), height: clamp(Math.round(region.height * height), 1, height - y) };
}
export function sameCrop(a: CropEdit, b: CropEdit) {
  if (a.crop !== b.crop) return false;
  if (a.crop === "original") return true;
  if (!a.cropRegion || !b.cropRegion) return a.cropRegion === b.cropRegion;
  return (["x", "y", "width", "height"] as const).every(key => a.cropRegion![key] === b.cropRegion![key]);
}
/** The full source stays in source coordinates inside a clipped crop viewport. */
export function cropPresentation(width: number, height: number, edit: CropEdit) {
  const selected = pixelCrop(width, height, edit);
  return { ...selected, plane: { width: `${width / selected.width * 100}%`, height: `${height / selected.height * 100}%`,
    left: `${-selected.x / selected.width * 100}%`, top: `${-selected.y / selected.height * 100}%` } };
}
export function moveCrop(region: CropRegion, dx: number, dy: number): CropRegion {
  return { ...region, x: clamp(region.x + dx, 0, 1 - region.width), y: clamp(region.y + dy, 0, 1 - region.height) };
}
export function resizeCrop(region: CropRegion, handle: string, dx: number, dy: number, width: number, height: number): CropRegion {
  let left = region.x, right = region.x + region.width, top = region.y, bottom = region.y + region.height;
  if (handle.includes("w")) left = clamp(left + dx, 0, right - Math.min(1 / width, region.width));
  if (handle.includes("e")) right = clamp(right + dx, left + Math.min(1 / width, region.width), 1);
  if (handle.includes("n")) top = clamp(top + dy, 0, bottom - Math.min(1 / height, region.height));
  if (handle.includes("s")) bottom = clamp(bottom + dy, top + Math.min(1 / height, region.height), 1);
  return { x: left, y: top, width: right - left, height: bottom - top };
}
export function zoomCrop(region: CropRegion, factor: number, width: number, height: number): CropRegion {
  const applied = clamp(1 / factor, Math.max(1 / width / region.width, 1 / height / region.height), Math.min(1 / region.width, 1 / region.height));
  const w = region.width * applied, h = region.height * applied;
  return { x: clamp(region.x + (region.width - w) / 2, 0, 1 - w), y: clamp(region.y + (region.height - h) / 2, 0, 1 - h), width: w, height: h };
}
