export function imageFileFormat(source: string, contentType?: string): string {
  const dataType = source.match(/^data:([^;,]+)[;,]/i)?.[1];
  const mime = (dataType || contentType || "").split(";")[0].trim().toLowerCase();
  const formats: Record<string, string> = {
    "image/jpeg": "JPEG", "image/png": "PNG", "image/webp": "WebP",
    "image/gif": "GIF", "image/avif": "AVIF", "image/bmp": "BMP",
    "image/tiff": "TIFF", "image/svg+xml": "SVG",
  };
  if (mime) return formats[mime] || (mime.startsWith("image/") ? mime : "정보 없음");
  // Compatibility with older project responses and bundled demo images.
  // Read only the actual source path, never the user's editable image title.
  try {
    const extension = new URL(source, "https://image.invalid/").pathname
      .match(/\.([a-z0-9]+)$/i)?.[1].toLowerCase();
    const extensions: Record<string, string> = {
      jpg: "JPEG", jpeg: "JPEG", png: "PNG", webp: "WebP",
      gif: "GIF", avif: "AVIF", bmp: "BMP", tiff: "TIFF", svg: "SVG",
    };
    return extensions[extension || ""] || "정보 없음";
  } catch { return "정보 없음"; }
}

export function imageResolution(width?: number, height?: number): string | null {
  if (typeof width !== "number" || typeof height !== "number" ||
    !Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0)
    return null;
  return `${width.toLocaleString("en-US")} × ${height.toLocaleString("en-US")} px`;
}
