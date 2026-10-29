export const maxImageBytes = 50 * 1024 * 1024;
export const imageAccept = ".jpg,.jpeg,.jpe,.png,.gif,.webp,.bmp,.dib,.tif,.tiff,.heic,.heif,.heics,.heifs,.hif,.avif,.dng,.jp2,.jpx,.wbmp,image/jpeg,image/png,image/gif,image/webp,image/heic,image/heif,image/avif,image/tiff,image/bmp";
export const imageUploadDescription = "JPEG · PNG · GIF · WebP · HEIF/HEIC · AVIF · BMP · TIFF 등 / 최대 50MB";
const extensions: Record<string, string> = {
  jpg: "image/jpeg", jpeg: "image/jpeg", jpe: "image/jpeg", png: "image/png", gif: "image/gif",
  webp: "image/webp", bmp: "image/bmp", dib: "image/bmp", tif: "image/tiff", tiff: "image/tiff",
  heic: "image/heic", heif: "image/heif", hif: "image/heif", heics: "image/heic-sequence", heifs: "image/heif-sequence",
  avif: "image/avif", dng: "image/x-adobe-dng", jp2: "image/jp2", jpx: "image/jpx", wbmp: "image/vnd.wap.wbmp",
};
export function validateImageUpload(file: Pick<File, "name" | "type" | "size">): string {
  if (file.size <= 0 || file.size > maxImageBytes) throw new Error("이미지는 0바이트 초과, 50MB 이하로 선택해 주세요.");
  const type = extensions[file.name.split(".").pop()?.toLowerCase() || ""];
  if (!type || (file.type && file.type !== "application/octet-stream" && !file.type.startsWith("image/")))
    throw new Error("JPEG, PNG, GIF, WebP, HEIF 등 지원하는 이미지 파일을 선택해 주세요.");
  return type;
}
