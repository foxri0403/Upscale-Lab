import { selectableTags, unspecifiedTag } from "./image-tags";
import { validateImageUpload } from "./image-upload";
import { requestBlob } from "./api";
import { pixelCrop, type CropRegion } from "./crop-geometry";
export type Genre = (typeof selectableTags)[number] | typeof unspecifiedTag;
export type Tool = "upscale" | "layers" | "live" | "crop" | "filter" | "tags";
export type HeadRig = {
  enabled: boolean;
  headTilt: number;
  eyeMove: number;
  hairSway: number;
  pivotX: number;
  pivotY: number;
  idle: boolean;
};
export const defaultHeadRig: HeadRig = {
  enabled: false, headTilt: 60, eyeMove: 35, hairSway: 55,
  pivotX: 50, pivotY: 16, idle: true,
};
export type Edit = {
  strength: number;
  filter: string;
  crop: string;
  cropRegion?: CropRegion;
  scale: number;
  foreground: boolean;
  liveEnabled?: boolean;
  rig?: HeadRig;
  mouseEnabled?: boolean;
  gyroEnabled?: boolean;
};
export type Picture = {
  separationDisabled?: boolean;
  isDraft?: boolean;
  id: string;
  name: string;
  src: string;
  genre: Genre;
  kind?: "Live 2D" | "배경 분리" | "업스케일";
  features?: string[];
  exportSources?: { image: string; background?: string; foregrounds?: string[] };
  created: string;
  edit?: Edit;
  savedAt?: string;
  sharedId?: string;
  galleryPostId?: string;
  galleryPostIds?: string[];
  sharedAssetIds?: string[];
  ownerId?: string;
  tags?: string[];
  creator?: string;
  creatorAvatar?: string;
  downloads?: number;
  projectId?: string;
  assetId?: string;
  sourceProjectId?: string;
  background?: string;
  foregrounds?: string[];
  layers?: import("./api").ApiLayer[];
  jobId?: string;
  detailsUntil?: number;
  width?: number;
  height?: number;
  contentType?: string;
};
export type Display = {
  id: string;
  name: string;
  resolution: string;
  current: string;
  desired?: string;
  removing?: boolean;
  connected: boolean;
  nativeId?: string;
  target?: string;
  refreshRateHz?: number | null;
  canApplyWallpaper?: boolean;
  isPrimary?: boolean;
  observation?: string;
};
export type Device = {
  id: string;
  created?: string;
  name: string;
  platform: "Windows" | "Android";
  osVersion?: string;
  physicalDisplayCount?: number;
  online: boolean;
  connection?: "connected" | "connecting" | "disconnected";
  supportsWallpaperRemoval?: boolean;
  displays: Display[];
};
export type Library = {
  images: Picture[];
  devices: Device[];
  assets?: Picture[];
};
export const defaultEdit: Edit = {
  liveEnabled: false,
  rig: { ...defaultHeadRig },
  mouseEnabled: false,
  gyroEnabled: false,
  strength: 70,
  filter: "original",
  crop: "original",
  scale: 1,
  foreground: true,
};
// crypto.randomUUID requires HTTPS; getRandomValues also works on the current HTTP test host.
export function uuid() {
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6] & 15) | 64;
  b[8] = (b[8] & 63) | 128;
  const s = Array.from(b, (x) => x.toString(16).padStart(2, "0")).join("");
  return `${s.slice(0, 8)}-${s.slice(8, 12)}-${s.slice(12, 16)}-${s.slice(16, 20)}-${s.slice(20)}`;
}
export const imageSrc = (key: string) =>
  `/images/${key === "fox" ? "fox.png" : `${key}.jpg`}`;
const samples: [string, string, Genre, Picture["kind"]][] = [
  ["fox", "숲속의 여우", "동물", "Live 2D"],
  ["lake", "푸른 호수", "풍경", "업스케일"],
  ["city", "도시의 밤", "풍경", "Live 2D"],
  ["stars", "별빛", "SF", "업스케일"],
  ["ocean", "잔잔한 바다", "풍경", undefined],
  ["forest", "아침의 숲", "풍경", "Live 2D"],
  ["mountain", "설산", "풍경", undefined],
  ["sunset", "노을", "풍경", undefined],
];
export function sampleImages(): Picture[] {
  return samples.map(([id, name, genre, kind], i) => ({
    id,
    name,
    genre,
    kind,
    src: imageSrc(id),
    created: new Date(Date.UTC(2026, 9, 26, 8, 0, 8 - i)).toISOString(),
  }));
}
export function sampleLibrary(): Library {
  return {
    images: sampleImages(),
    devices: [
      {
        id: "pc",
        name: "내 PC",
        platform: "Windows",
        online: true,
        displays: [
          {
            id: "monitor1",
            name: "모니터 1",
            resolution: "2560 × 1440",
            current: "fox",
            connected: true,
          },
          {
            id: "monitor2",
            name: "모니터 2",
            resolution: "1920 × 1080",
            current: "lake",
            connected: true,
          },
        ],
      },
      {
        id: "phone",
        name: "내 휴대폰",
        platform: "Android",
        online: false,
        displays: [
          {
            id: "home",
            name: "홈 화면",
            resolution: "1080 × 2400",
            current: "ocean",
            desired: "fox",
            connected: true,
          },
          {
            id: "lock",
            name: "잠금 화면",
            resolution: "1080 × 2400",
            current: "lake",
            connected: true,
          },
        ],
      },
    ],
  };
}
export const community = sampleImages().map((p, i) => ({
  ...p,
  id: `community-${p.id}`,
  creator: [
    "haru",
    "mori",
    "urban",
    "stella",
    "yuki",
    "green",
    "alpine",
    "sol",
  ][i],
  downloads: [128, 96, 84, 72, 61, 58, 43, 39][i],
}));
export function read<T>(key: string, fallback: T): T {
  try {
    const value = localStorage.getItem(key);
    return value ? (JSON.parse(value) as T) : fallback;
  } catch {
    return fallback;
  }
}
export function write(key: string, value: unknown) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    throw new Error(
      "브라우저 저장 공간이 부족합니다. 추가한 이미지를 정리한 뒤 다시 시도해 주세요.",
    );
  }
}
export function filterStyle(filter: string): string {
  return (
    (
      {
        original: "none",
        vivid: "saturate(1.4) contrast(1.08)",
        warm: "sepia(.22) saturate(1.15)",
        mono: "grayscale(1)",
        soft: "contrast(.88) brightness(1.1)",
      } as Record<string, string>
    )[filter] || "none"
  );
}
export async function loadImage(src: string): Promise<HTMLImageElement> {
  const img = new Image();
  if (!src.startsWith("data:") && !src.startsWith("blob:"))
    img.crossOrigin = "anonymous";
  img.src = src;
  await img.decode();
  return img;
}
export async function uploadImage(file: File): Promise<Picture> {
  validateImageUpload(file);
  const url = URL.createObjectURL(file);
  try {
    const img = await loadImage(url);
    const factor = Math.min(1, 1600 / Math.max(img.width, img.height));
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(img.width * factor);
    canvas.height = Math.round(img.height * factor);
    canvas.getContext("2d")!.drawImage(img, 0, 0, canvas.width, canvas.height);
    return {
      id: crypto.randomUUID(),
      name: file.name.replace(/\.[^.]+$/, ""),
      src: canvas.toDataURL("image/jpeg", 0.84),
      genre: unspecifiedTag,
      tags: [],
      width: canvas.width,
      height: canvas.height,
      contentType: "image/jpeg",
      created: new Date().toISOString(),
    };
  } finally {
    URL.revokeObjectURL(url);
  }
}
export async function renderImage(picture: Picture, edit: Edit): Promise<Blob> {
  const temporary: string[] = [];
  const load = async (source: string, endpoint?: string) => {
    if (!endpoint) return loadImage(source);
    const url = URL.createObjectURL(await requestBlob(endpoint));
    temporary.push(url);
    return loadImage(url);
  };
  try {
  const characterLayers = !picture.background && (picture.layers?.length || 0) >= 2
    ? picture.layers!.slice().sort((a, b) => a.layerOrder - b.layerOrder) : null;
  const img = await load(picture.background || picture.src, picture.background ? picture.exportSources?.background : picture.exportSources?.image);
  let source: CanvasImageSource = img;
  if (characterLayers && edit.foreground) {
    const composite = document.createElement("canvas");
    const first = await load(characterLayers[0].imageUrl,
      picture.projectId ? `/api/projects/${picture.projectId}/content?layerId=${characterLayers[0].id}` : undefined);
    composite.width = first.width;
    composite.height = first.height;
    const context = composite.getContext("2d")!;
    context.drawImage(first, 0, 0);
    for (const layer of characterLayers.slice(1))
      context.drawImage(await load(layer.imageUrl,
        picture.projectId ? `/api/projects/${picture.projectId}/content?layerId=${layer.id}` : undefined), 0, 0, composite.width, composite.height);
    source = composite;
  }
  if (picture.background && picture.foregrounds?.length) {
    const composite = document.createElement("canvas");
    composite.width = img.width;
    composite.height = img.height;
    const context = composite.getContext("2d")!;
    context.drawImage(img, 0, 0);
    if (edit.foreground)
      for (const [index, src] of picture.foregrounds.entries())
        context.drawImage(await load(src, picture.exportSources?.foregrounds?.[index]), 0, 0, img.width, img.height);
    source = composite;
  }
  const selected = pixelCrop(characterLayers && edit.foreground ? (source as HTMLCanvasElement).width : img.width,
    characterLayers && edit.foreground ? (source as HTMLCanvasElement).height : img.height, edit);
  const sw = selected.width, sh = selected.height;
  const scale = Math.min(edit.scale, 4096 / Math.max(sw, sh));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(sw * scale);
  canvas.height = Math.round(sh * scale);
  const ctx = canvas.getContext("2d")!;
  ctx.filter = filterStyle(edit.filter);
  ctx.imageSmoothingQuality = "high";
  ctx.drawImage(
    source,
    selected.x,
    selected.y,
    sw,
    sh,
    0,
    0,
    canvas.width,
    canvas.height,
  );
  return await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob(
      (b) =>
        b
          ? resolve(b)
          : reject(
              new Error(
                "파일 생성에 실패했습니다. S3 CORS 설정을 확인해 주세요.",
              ),
            ),
      "image/png",
    ),
  );
  } finally { temporary.forEach(url => URL.revokeObjectURL(url)); }
}
export async function downloadImage(
  picture: Picture,
  edit: Edit,
): Promise<void> {
  const blob = await renderImage(picture, edit);
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `${picture.name.replace(/[<>:"/\\|?*]/g, "_")}.png`;
  a.style.display = "none";
  document.body.appendChild(a);
  try { a.click(); } finally { a.remove(); }
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}
