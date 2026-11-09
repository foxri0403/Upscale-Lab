export const maxImageBytes = 50 * 1024 * 1024;
export const imageAccept = ".jpg,.jpeg,.jpe,.png,.webp,.bmp,.dib,.tif,.tiff,.heic,.heif,.hif,.avif,.dng,.jp2,.jpx,.wbmp,image/jpeg,image/png,image/webp,image/heic,image/heif,image/avif,image/tiff,image/bmp";
export const imageUploadDescription = "정적 이미지 · JPEG, PNG, WebP, HEIF/HEIC, AVIF, BMP, TIFF 등 · 최대 50MB";
const extensions: Record<string, string> = {
  jpg: "image/jpeg", jpeg: "image/jpeg", jpe: "image/jpeg", png: "image/png", gif: "image/gif",
  webp: "image/webp", bmp: "image/bmp", dib: "image/bmp", tif: "image/tiff", tiff: "image/tiff",
  heic: "image/heic", heif: "image/heif", hif: "image/heif", heics: "image/heic-sequence", heifs: "image/heif-sequence",
  avif: "image/avif", dng: "image/x-adobe-dng", jp2: "image/jp2", jpx: "image/jpx", wbmp: "image/vnd.wap.wbmp",
};
export function validateImageUpload(file: Pick<File, "name" | "type" | "size">,
  formatError = "JPEG, PNG, GIF, WebP, HEIF 등 지원하는 이미지 파일을 선택해 주세요."): string {
  if (file.size <= 0 || file.size > maxImageBytes) throw new Error("이미지는 0바이트 초과, 50MB 이하로 선택해 주세요.");
  const type = extensions[file.name.split(".").pop()?.toLowerCase() || ""];
  if (!type || (file.type && file.type !== "application/octet-stream" && !file.type.startsWith("image/")))
    throw new Error(formatError);
  return type;
}

const sequenceBrands = new Set(["avis", "msf1", "hevc", "hevx", "hevm", "hevs"]);

function fourCC(bytes: Uint8Array, offset: number) {
  return String.fromCharCode(...bytes.subarray(offset, offset + 4));
}

async function hasAnimation(file: File) {
  const header = new Uint8Array(await file.slice(0, 24).arrayBuffer());
  if (fourCC(header, 0) === "GIF8") return true;
  if (fourCC(header, 0) === "RIFF" && fourCC(header, 8) === "WEBP") {
    // The extended WebP header marks animation in the second-lowest flag bit.
    return fourCC(header, 12) === "VP8X" && (header[20] & 0x02) !== 0;
  }
  if (header.length < 8) return false;
  const fields = new DataView(header.buffer);
  if (fields.getUint32(0) === 0x89504e47 && fields.getUint32(4) === 0x0d0a1a0a) {
    const bytes = new Uint8Array(await file.arrayBuffer());
    const chunks = new DataView(bytes.buffer);
    // APNG requires an animation-control chunk before the first image data chunk.
    for (let offset = 8; offset + 12 <= bytes.length; offset += chunks.getUint32(offset) + 12) {
      const type = fourCC(bytes, offset + 4);
      if (type === "acTL") return true;
      if (type === "IDAT" || type === "IEND") break;
    }
  }
  if (fourCC(header, 4) === "ftyp") {
    const size = fields.getUint32(0);
    const start = size === 1 ? 16 : 8;
    if (header.length < start + 8) return false;
    const end = size === 1 ? Number(fields.getBigUint64(8)) : size || file.size;
    if (end < start + 8 || end > file.size) return false;
    const brands = new Uint8Array(await file.slice(start, end).arrayBuffer());
    if (sequenceBrands.has(fourCC(brands, 0))) return true;
    // Skip the minor version between the major brand and the compatible brands.
    for (let offset = 8; offset + 4 <= brands.length; offset += 4) {
      if (sequenceBrands.has(fourCC(brands, offset))) return true;
    }
  }
  return false;
}

export async function validateStudioImageUpload(file: File) {
  const format = validateImageUpload(file, "JPEG, PNG, WebP, HEIF 등 지원하는 정적 이미지 파일을 선택해 주세요.");
  const movingFormat = /^image\/(gif|apng|\w+-sequence)$/i;
  if (movingFormat.test(format) || movingFormat.test(file.type) || await hasAnimation(file)) {
    throw new Error("GIF 등 움직이는 이미지는 첨부할 수 없습니다. 정적 이미지를 선택해 주세요.");
  }
}
