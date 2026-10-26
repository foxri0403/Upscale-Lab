export const genres = [
  "풍경",
  "동물",
  "도시",
  "인물",
  "우주",
  "애니",
  "게임",
  "여자",
  "남자",
  "픽셀 아트",
  "레트로",
  "SF",
  "스포츠",
  "자동차",
  "비행기",
  "밀리터리",
  "장르 설정되지 않음",
] as const;
export type Genre = (typeof genres)[number];
export type Tool = "upscale" | "layers" | "live" | "crop" | "filter";
export type Edit = {
  strength: number;
  filter: string;
  crop: string;
  scale: number;
  foreground: boolean;
};
export type Picture = {
  id: string;
  name: string;
  src: string;
  genre: Genre;
  kind?: "Live 2D" | "업스케일";
  created: string;
  edit?: Edit;
  sharedId?: string;
  galleryPostId?: string;
  ownerId?: string;
  tags?: string[];
  creator?: string;
  downloads?: number;
  projectId?: string;
  assetId?: string;
  background?: string;
  foregrounds?: string[];
  jobId?: string;
  detailsUntil?: number;
};
export type Display = {
  id: string;
  name: string;
  resolution: string;
  current: string;
  desired?: string;
  connected: boolean;
  nativeId?: string;
  target?: string;
};
export type Device = {
  id: string;
  name: string;
  platform: "Windows" | "Android";
  osVersion?: string;
  online: boolean;
  displays: Display[];
};
export type Command = {
  id: string;
  device: string;
  display: string;
  picture: string;
  status: "대기" | "완료" | "실패";
  created: string;
  detail?: string;
};
export type Library = {
  images: Picture[];
  devices: Device[];
  commands: Command[];
  assets?: Picture[];
};
export const defaultEdit: Edit = {
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
  ["city", "도시의 밤", "도시", "Live 2D"],
  ["stars", "별빛", "우주", "업스케일"],
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
    commands: [
      {
        id: "sample-1",
        device: "내 휴대폰",
        display: "홈 화면",
        picture: "숲속의 여우",
        status: "대기",
        created: new Date().toISOString(),
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
  if (!["image/jpeg", "image/png", "image/webp"].includes(file.type))
    throw new Error("JPG, PNG, WebP 이미지를 선택해 주세요.");
  if (file.size > 15 * 1024 * 1024)
    throw new Error("15MB 이하의 이미지를 선택해 주세요.");
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
      genre: "풍경",
      created: new Date().toISOString(),
    };
  } finally {
    URL.revokeObjectURL(url);
  }
}
export async function renderImage(picture: Picture, edit: Edit): Promise<Blob> {
  const img = await loadImage(picture.background || picture.src);
  let source: CanvasImageSource = img;
  if (picture.background && picture.foregrounds?.length) {
    const composite = document.createElement("canvas");
    composite.width = img.width;
    composite.height = img.height;
    const context = composite.getContext("2d")!;
    context.drawImage(img, 0, 0);
    if (edit.foreground)
      for (const src of picture.foregrounds)
        context.drawImage(await loadImage(src), 0, 0, img.width, img.height);
    source = composite;
  }
  let sw = img.width,
    sh = img.height;
  const ratio =
    edit.crop === "original"
      ? sw / sh
      : (
          { wide: 16 / 9, square: 1, portrait: 9 / 16 } as Record<
            string,
            number
          >
        )[edit.crop];
  if (sw / sh > ratio) sw = sh * ratio;
  else sh = sw / ratio;
  const scale = Math.min(edit.scale, 4096 / Math.max(sw, sh));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(sw * scale);
  canvas.height = Math.round(sh * scale);
  const ctx = canvas.getContext("2d")!;
  ctx.filter = filterStyle(edit.filter);
  ctx.imageSmoothingQuality = "high";
  ctx.drawImage(
    source,
    (img.width - sw) / 2,
    (img.height - sh) / 2,
    sw,
    sh,
    0,
    0,
    canvas.width,
    canvas.height,
  );
  return new Promise<Blob>((resolve, reject) =>
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
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}
