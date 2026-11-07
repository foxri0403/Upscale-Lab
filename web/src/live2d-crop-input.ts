/** A crop saved for wallpaper presentation is NOT automatically an AI input crop. */
export const maxLive2dInputBytes = 16 * 1024 * 1024;
export function live2dCropRequired(edit: { crop: string }): boolean {
  return edit.crop !== "original";
}
export function croppedLive2dFileName(title: string, crop: string, extension: "png" | "jpg"): string {
  const safe = title.replace(/[<>:"/\\|?*\x00-\x1f]/g, "_").trim().slice(0, 110) || "이미지";
  const label = ({ wide: "16-9", portrait: "9-16", square: "1-1", custom: "사용자비율" } as Record<string, string>)[crop] || "자르기";
  return `${safe}-${label}-Live2D.${extension}`;
}
