import {
  request,
  post,
  type Project,
  type Asset,
  type Manifest,
  type CloudDevice,
  type CloudState,
  type CloudCommand,
  type Job,
  type ApiLayer,
} from "./api";
import {
  defaultEdit,
  read,
  loadImage,
  renderImage,
  type Picture,
  type Library,
  type Device,
  type Command,
} from "./model";
import {
  galleryPicture,
  mergeExploreFeed,
  type GalleryPost,
} from "./explore-feed";
import { unspecifiedTag } from "./image-tags";
import { isImageShared, projectSharing } from "./image-sharing";

const manifests = new Map<string, { picture: Picture; until: number }>();
export const preferencesKey = (userId: string) =>
  `upscale.cloud.edits.v1.${userId}`;
export function projectPicture(p: Project): Picture {
  const background = p.layers?.find(
    (l) => l.layerType === "Background",
  )?.imageUrl;
  return {
    id: p.id,
    projectId: p.id,
    name: p.title,
    src: p.originalImageUrl,
    genre: unspecifiedTag,
    tags: [],
    width: p.originalWidth,
    height: p.originalHeight,
    contentType: p.originalContentType,
    created: p.createdAt,
    background,
    foregrounds: p.layers
      ?.filter((l) => l.layerType !== "Background")
      .sort((a, b) => a.layerOrder - b.layerOrder)
      .map((l) => l.imageUrl),
    kind: (p.layers?.length || p.layerCount || 0) > 1 ? "Live 2D" : undefined,
  };
}
export async function assetPicture(a: Asset, fresh = false): Promise<Picture> {
  const cached = manifests.get(a.id);
  if (!fresh && cached && cached.until > Date.now())
    return {
      ...cached.picture,
      name: a.title,
      sharedId: a.isPublic ? a.id : undefined,
    };
  const m = await request<Manifest>(`/api/wallpapers/${a.id}/manifest`);
  const files = [...m.files].sort((a, b) => a.order - b.order);
  const picture: Picture = {
    id: `asset:${a.id}`,
    assetId: a.id,
    name: a.title,
    src: files[0]?.source || "",
    genre: unspecifiedTag,
    tags: [],
    width: m.width,
    height: m.height,
    contentType: files[0]?.contentType,
    created: a.createdAt,
    kind: a.mode === "Live2D" ? "Live 2D" : undefined,
    sharedId: a.isPublic ? a.id : undefined,
    background:
      a.mode === "Live2D"
        ? files.find((f) => f.role === "Background")?.source
        : undefined,
    foregrounds:
      a.mode === "Live2D"
        ? files.filter((f) => f.role !== "Background").map((f) => f.source)
        : undefined,
  };
  manifests.set(a.id, { picture, until: Date.now() + 5 * 60000 });
  return picture;
}
export function clearCloudCache() {
  manifests.clear();
}
export async function fetchLibrary(
  userId: string,
): Promise<{ library: Library; publicImages: Picture[] }> {
  const [projects, devices, assets, publicAssets, gallery] = await Promise.all([
    request<Project[]>("/api/projects"),
    request<CloudDevice[]>("/api/devices"),
    request<Asset[]>("/api/wallpapers"),
    request<Asset[]>("/api/wallpapers/shared"),
    request<GalleryPost[]>("/api/gallery"),
  ]);
  const [states, commands, ownPictures, publicPictures] = await Promise.all([
    Promise.all(
      devices.map((d) => request<CloudState>(`/api/devices/${d.id}/state`)),
    ),
    Promise.all(
      devices.map((d) =>
        request<CloudCommand[]>(`/api/devices/${d.id}/commands`),
      ),
    ),
    Promise.all(assets.map((a) => assetPicture(a))),
    Promise.all(publicAssets.map((a) => assetPicture(a))),
  ]);
  const owned = new Set(assets.map((a) => a.id));
  const preferences = read<Record<string, Partial<Picture>>>(
    preferencesKey(userId),
    {},
  );
  const mappedDevices: Device[] = states.map((s) => ({
    id: s.id,
    name: s.name,
    platform: s.platform,
    osVersion: s.osVersion,
    manufacturer: s.manufacturer,
    model: s.model,
    architecture: s.architecture,
    physicalDisplayCount: s.displays.filter(d => d.isConnected).length,
    online: s.recentlySeen,
    displays: s.displays.flatMap((d) => {
      const assignments = d.wallpapers.length
        ? d.wallpapers
        : [
            {
              target: s.platform === "Android" ? "Home" : "Desktop",
              desiredAssetId: undefined,
              reportedAssetId: undefined,
              observation: "Unknown",
            },
          ];
      return assignments.map((w) => ({
        id: `${d.id}:${w.target}`,
        nativeId: d.id,
        target: w.target,
        name:
          s.platform === "Android" && d.canApplyWallpaper !== false
            ? w.target === "Lock"
              ? "잠금 화면"
              : "홈 화면"
            : d.name,
        resolution: `${d.width} × ${d.height}`,
        connected: d.isConnected,
        refreshRateHz: d.refreshRateHz,
        canApplyWallpaper: d.canApplyWallpaper ?? true,
        isPrimary: d.isPrimary,
        current: w.reportedAssetId ? `asset:${w.reportedAssetId}` : "",
        desired:
          w.desiredAssetId && w.desiredAssetId !== w.reportedAssetId
            ? `asset:${w.desiredAssetId}`
            : undefined,
      }));
    }),
  }));
  const mappedCommands: Command[] = commands.flatMap((list, i) =>
    list.map(
      (c) =>
        ({
          id: c.id,
          device: states[i].name,
          display:
            states[i].platform === "Android"
              ? c.target === "Lock"
                ? "잠금 화면"
                : "홈 화면"
              : states[i].displays.find((d) => d.id === c.displayId)?.name ||
                "화면",
          picture: assets.find((a) => a.id === c.assetId)?.title || "배경화면",
          status:
            c.status === "Applied"
              ? "완료"
              : ["Failed", "Expired", "Superseded"].includes(c.status)
                ? "실패"
                : "대기",
          created: "",
          detail: c.error || c.status,
        }) as Command,
    ),
  );
  // Project previews and immutable saved wallpapers are distinct server resources.
  const projectsWithMetadata = projects.map((project) => {
    const picture = projectPicture(project);
    const publications = gallery.filter(
      (post) =>
        post.userId === userId &&
        post.projectId === project.id &&
        post.isPublic,
    );
    const published = publications[0];
    if (!published) return projectSharing(picture, assets);
    const metadata = galleryPicture(published);
    return projectSharing({
      ...picture,
      genre: metadata.genre,
      tags: metadata.tags,
      galleryPostId: published.id,
      galleryPostIds: publications.map((post) => post.id),
      sharedId: published.id,
    }, assets);
  });
  const images = [...projectsWithMetadata, ...ownPictures].map((p) => ({
    ...p,
    ...preferences[p.id],
    src: p.src,
    background: p.background,
    foregrounds: p.foregrounds,
  }));
  return {
    library: {
      images,
      devices: mappedDevices,
      commands: mappedCommands,
      assets: ownPictures,
    },
    publicImages: mergeExploreFeed(
      publicPictures.map((p) => ({
        ...p,
        ownerId: owned.has(p.assetId!) ? userId : undefined,
        creator: owned.has(p.assetId!) ? "내 공유 이미지" : "공유 배경화면",
      })),
      gallery,
    ),
  };
}
export async function importGalleryPicture(picture: Picture): Promise<Picture> {
  if (!picture.galleryPostId) throw new Error("공유 게시물을 확인해 주세요.");
  // Get a fresh read-only preview URL. Importing is not a completed file download
  // and must not increment the mobile gallery's download counter.
  const { url } = await request<{ url: string }>(
    `/api/gallery/${picture.galleryPostId}/preview-url`,
  );
  let response: Response;
  try {
    response = await fetch(url, { signal: AbortSignal.timeout(30000) });
  } catch {
    throw new Error(
      "원본 이미지를 바로 가져올 수 없습니다. ‘원본 이미지 열기’에서 파일을 내려받은 뒤 ‘이미지 추가’로 등록해 주세요.",
    );
  }
  if (!response.ok) throw new Error("공유 원본 이미지에 접근할 수 없습니다.");
  const blob = await response.blob();
  const extension = (
    { "image/jpeg": "jpg", "image/png": "png", "image/webp": "webp" } as Record<
      string,
      string
    >
  )[blob.type];
  if (!extension)
    throw new Error("이 공유 이미지 형식은 웹 가져오기를 지원하지 않습니다.");
  return uploadProject(
    new File([blob], `${picture.name}.${extension}`, { type: blob.type }),
  );
}
export async function uploadProject(file: File): Promise<Picture> {
  if (!["image/jpeg", "image/png", "image/webp"].includes(file.type))
    throw new Error("JPG, PNG, WebP 이미지를 선택해 주세요.");
  if (file.size > 15 * 1024 * 1024)
    throw new Error("15MB 이하의 이미지를 선택해 주세요.");
  const url = URL.createObjectURL(file);
  try {
    const img = await loadImage(url);
    const form = new FormData();
    form.append("File", file);
    form.append("Title", file.name.replace(/\.[^.]+$/, "").slice(0, 160));
    form.append("OriginalWidth", String(img.width));
    form.append("OriginalHeight", String(img.height));
    return projectPicture(
      await request<Project>("/api/projects", { method: "POST", body: form }),
    );
  } finally {
    URL.revokeObjectURL(url);
  }
}
export async function detailPicture(p: Picture): Promise<Picture> {
  if (!p.projectId) return p;
  const detail = await request<Project>(`/api/projects/${p.projectId}`);
  const result = {
    ...p,
    ...projectPicture(detail),
    genre: p.genre,
    tags: p.tags,
    detailsUntil: Date.now() + 5 * 60000,
  };
  if (
    detail.latestJob?.status === "Completed" &&
    detail.latestJob.hasImageResult
  ) {
    const download = await post<{ url: string }>(
      `/api/projects/${p.projectId}/jobs/${detail.latestJob.jobId}/download-url`,
      {},
    );
    result.src = download.url;
    result.jobId = detail.latestJob.jobId;
    result.kind = "업스케일";
    result.width = detail.latestJob.resultWidth;
    result.height = detail.latestJob.resultHeight;
    result.contentType = "image/png";
  }
  return result;
}
export async function createAsset(picture: Picture): Promise<Asset> {
  const edit = picture.edit || defaultEdit;
  let projectId = picture.projectId;
  // Bake actual crop/filter settings into a new source before freezing a static version.
  const rendered =
    edit.filter !== "original" ||
    edit.crop !== "original" ||
    edit.scale !== 1 ||
    (!!picture.background && !edit.foreground);
  if (picture.assetId && !rendered && !picture.projectId)
    return {
      id: picture.assetId,
      title: picture.name,
      mode: picture.kind === "Live 2D" ? "Live2D" : "Static",
      isPublic: !!picture.sharedId,
      createdAt: picture.created,
    };
  if (rendered || !projectId) {
    const blob = await renderImage(picture, edit);
    const created = await uploadProject(
      new File([blob], `${picture.name}.png`, { type: "image/png" }),
    );
    projectId = created.projectId;
  }
  const mode =
    !rendered && picture.background && picture.foregrounds?.length
      ? "Live2D"
      : "Static";
  if (mode === "Live2D" && !isImageShared(picture)) {
    const layers = await request<ApiLayer[]>(
      `/api/projects/${projectId}/layers`,
    );
    await Promise.all(
      layers
        .filter((l) => l.layerType !== "Background")
        .map((l) =>
          request(`/api/projects/${projectId}/layers/${l.id}`, {
            method: "PATCH",
            body: JSON.stringify({
              movementX: edit.strength / 5,
              movementY: edit.strength / 8,
            }),
          }),
        ),
    );
  }
  return post<Asset>("/api/wallpapers", {
    projectId,
    title: picture.name,
    mode,
    processingJobId: !rendered && mode === "Static" ? picture.jobId : undefined,
    originProjectId: picture.projectId,
  });
}
export async function processProject(
  picture: Picture,
  operation: string,
  factor: number,
) {
  if (!picture.projectId)
    throw new Error(
      "저장된 결과물은 먼저 원본 프로젝트를 선택해 처리해 주세요.",
    );
  return post<Job>(`/api/projects/${picture.projectId}/process`, {
    operation,
    upscaleFactor: factor === 4 ? 4 : 2,
  });
}
