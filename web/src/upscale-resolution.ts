export const upscaleResolutions = [
  { edge: 1920, name: "FHD" },
  { edge: 2560, name: "QHD" },
  { edge: 3840, name: "4K" },
] as const;

export function upscaleDimensions(width: number, height: number, edge: number) {
  if (!Number.isInteger(width) || !Number.isInteger(height) || width < 1 || height < 1 ||
      !upscaleResolutions.some(value => value.edge === edge)) return null;
  const longest = Math.max(width, height);
  return { width: Math.max(1, Math.round(width * edge / longest)),
    height: Math.max(1, Math.round(height * edge / longest)) };
}
