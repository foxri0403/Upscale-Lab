import {
  request,
  post,
  type Project,
  type Asset,
  type Manifest,
  type CloudDevice,
  type CloudState,
  type Job,
  type ApiLayer,
  type SavedEdit,
} from "./api";
import {
  defaultEdit,
  read,
  renderImage,
  type Picture,
  type Library,
} from "./model";
import {
  galleryPicture,
  mergeExploreFeed,
  type GalleryPost,
} from "./explore-feed";
import { unspecifiedTag } from "./image-tags";
import { isImageShared, projectSharing } from "./image-sharing";
import { mapDeviceLibrary } from "./device-state";
import { galleryImages } from "./gallery-library";
import { validateImageUpload } from "./image-upload";

const manifests = new Map<string, { picture: Picture; until: number }>();
export const preferencesKey = (userId: string) =>
  `upscale.cloud.edits.v1.${userId}`;
export function projectPicture(p: Project): Picture {
  const result = p.imageResult;
  const background = p.layers?.find(
    (l) => l.layerType === "Background",
  )?.imageUrl;
  return {
    id: p.id,
    projectId: p.id,
    isDraft: p.isDraft ?? false,
    detailsUntil: p.layers !== undefined ? Date.now() + 5 * 60000 : undefined,
    edit: p.edit ? { strength: p.edit.strength, filter: p.edit.filter, crop: p.edit.crop,
      cropRegion: p.edit.cropRegion || undefined,
      scale: result ? 1 : p.edit.scale, foreground: p.edit.foreground, liveEnabled: p.edit.liveEnabled,
      mouseEnabled: p.edit.mouseEnabled ?? false, gyroEnabled: p.edit.gyroEnabled ?? false, rig: p.edit.rig } : undefined,
    savedAt: p.edit?.savedAt,
    name: p.title,
    src: result?.imageUrl || p.originalImageUrl,
    genre: unspecifiedTag,
    tags: p.edit?.tags || [],
    width: result?.width || p.originalWidth,
    height: result?.height || p.originalHeight,
    contentType: result ? "image/png" : p.originalContentType,
    jobId: result?.jobId,
    exportSources: {
      image: `/api/projects/${p.id}/content${result ? `?jobId=${encodeURIComponent(result.jobId)}` : ""}`,
      background: p.layers?.find(layer => layer.layerType === "Background") ? `/api/projects/${p.id}/content?layerId=${p.layers.find(layer => layer.layerType === "Background")!.id}` : undefined,
      foregrounds: p.layers?.filter(layer => layer.layerType !== "Background").sort((a, b) => a.layerOrder - b.layerOrder)
        .map(layer => `/api/projects/${p.id}/content?layerId=${layer.id}`),
    },
    created: p.createdAt,
    background,
    layers: p.layers?.slice().sort((a, b) => a.layerOrder - b.layerOrder),
    foregrounds: p.layers
      ?.filter((l) => l.layerType !== "Background")
      .sort((a, b) => a.layerOrder - b.layerOrder)
      .map((l) => l.imageUrl),
    kind: result ? "업스케일" : (p.layers?.length || p.layerCount || 0) > 1 ? "배경 분리" : undefined,
    features: p.features,
  };
}
export async function assetPicture(a: Asset, fresh = false): Promise<Picture> {
  const cached = manifests.get(a.id);
  if (!fresh && cached && cached.until > Date.now())
    return {
      ...cached.picture,
      sourceProjectId: a.sourceProjectId ?? undefined,
      name: a.title,
      edit: a.edit ? { strength: a.edit.strength, filter: a.edit.filter, crop: a.edit.crop,
        cropRegion: a.edit.cropRegion || undefined,
        scale: a.edit.scale, foreground: a.edit.foreground, liveEnabled: a.edit.liveEnabled,
        mouseEnabled: a.edit.mouseEnabled ?? false, gyroEnabled: a.edit.gyroEnabled ?? false, rig: a.edit.rig } : undefined,
      tags: a.edit?.tags || [], features: a.features,
      savedAt: a.edit?.savedAt,
      creator: a.username, creatorAvatar: a.profileImageUrl || undefined, ownerId: a.userId,
      sharedId: a.isPublic ? a.id : undefined,
    };
  const m = await request<Manifest>(`/api/wallpapers/${a.id}/manifest`);
  const files = [...m.files].sort((a, b) => a.order - b.order);
  const picture: Picture = {
    id: `asset:${a.id}`,
    assetId: a.id,
    edit: a.edit ? { strength: a.edit.strength, filter: a.edit.filter, crop: a.edit.crop,
      cropRegion: a.edit.cropRegion || undefined,
      scale: a.edit.scale, foreground: a.edit.foreground, liveEnabled: a.edit.liveEnabled,
      mouseEnabled: a.edit.mouseEnabled ?? false, gyroEnabled: a.edit.gyroEnabled ?? false, rig: a.edit.rig } : undefined,
    savedAt: a.edit?.savedAt,
    creator: a.username, creatorAvatar: a.profileImageUrl || undefined, ownerId: a.userId,
    sourceProjectId: a.sourceProjectId ?? undefined,
    name: a.title,
    src: files[0]?.source || "",
    genre: unspecifiedTag,
    tags: a.edit?.tags || [],
    features: a.features,
    exportSources: {
      image: `/api/wallpapers/${a.id}/content/${files[0]?.order ?? 0}`,
      background: a.mode === "Live2D" ? `/api/wallpapers/${a.id}/content/${files.find(file => file.role === "Background")?.order ?? 0}` : undefined,
      foregrounds: a.mode === "Live2D" ? files.filter(file => file.role !== "Background").map(file => `/api/wallpapers/${a.id}/content/${file.order}`) : undefined,
    },
    width: m.width,
    height: m.height,
    contentType: files[0]?.contentType,
    created: a.createdAt,
    kind: a.mode === "Live2D" ? "배경 분리" : undefined,
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
export async function fetchDevices() {
  const devices = await request<CloudDevice[]>("/api/devices");
  const states = await Promise.all(devices.map(d => request<CloudState>(`/api/devices/${d.id}/state`)));
  return mapDeviceLibrary(states, devices);
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
  const [states, ownPictures, publicPictures] = await Promise.all([
    Promise.all(
      devices.map((d) => request<CloudState>(`/api/devices/${d.id}/state`)),
    ),
    Promise.all(assets.map((a) => assetPicture(a))),
    Promise.all(publicAssets.map((a) => assetPicture(a))),
  ]);
  const owned = new Set(assets.map((a) => a.id));
  const preferences = read<Record<string, Partial<Picture>>>(
    preferencesKey(userId),
    {},
  );
  const { devices: mappedDevices } = mapDeviceLibrary(states, devices);
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
  const images = galleryImages(projectsWithMetadata, ownPictures).map((p) => ({
    ...p,
    ...(p.savedAt ? {} : preferences[p.id]),
    src: p.src,
    background: p.background,
    foregrounds: p.foregrounds,
  }));
  return {
    library: {
      images,
      devices: mappedDevices,
      assets: ownPictures,
    },
    publicImages: mergeExploreFeed(
      publicPictures.map((p) => ({
        ...p,
        ownerId: p.ownerId || (owned.has(p.assetId!) ? userId : undefined),
        creator: p.creator || (owned.has(p.assetId!) ? "내 공유 이미지" : "공유 배경화면"),
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
      "공유 원본 이미지를 가져올 수 없습니다. 잠시 후 다시 시도해 주세요.",
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
    false,
  );
}
export async function uploadProject(file: File, isDraft = true): Promise<Picture> {
  const contentType = validateImageUpload(file);
    const form = new FormData();
    form.append("File", new File([file], file.name, { type: contentType }));
    form.append("Title", file.name.replace(/\.[^.]+$/, "").slice(0, 160));
    form.append("IsDraft", String(isDraft));
    return projectPicture(
      await request<Project>("/api/projects", { method: "POST", body: form, signal: AbortSignal.timeout(180000) }),
    );
}
export async function loadProjectPicture(id: string): Promise<Picture> {
  return projectPicture(await request<Project>(`/api/projects/${encodeURIComponent(id)}`));
}
export async function resetProjectEffects(picture: Picture, feature: "All" | "Upscale" | "Layers"): Promise<Picture> {
  const detail = await post<Project>(`/api/projects/${picture.projectId}/effects/reset`, { feature });
  return { ...picture, ...projectPicture(detail), genre: picture.genre, tags: detail.edit?.tags ?? picture.tags };
}
export function discardDraft(picture: Picture) {
  return picture.isDraft && picture.projectId
    ? request(`/api/projects/${picture.projectId}/draft`, { method: "DELETE", keepalive: true })
    : Promise.resolve();
}
export async function detailPicture(p: Picture): Promise<Picture> {
  if (!p.projectId) return p;
  const detail = await request<Project>(`/api/projects/${p.projectId}`);
  const result = {
    ...p,
    ...projectPicture(detail),
    genre: p.genre,
    tags: detail.edit?.tags ?? p.tags,
    detailsUntil: Date.now() + 5 * 60000,
  };
  if (
    !detail.imageResult && detail.latestJob?.status === "Completed" &&
    detail.latestJob.hasImageResult
  ) {
    const download = await post<{ url: string }>(
      `/api/projects/${p.projectId}/jobs/${detail.latestJob.jobId}/download-url`,
      {},
    );
    result.src = download.url;
    result.jobId = detail.latestJob.jobId;
    if (result.edit) result.edit = { ...result.edit, scale: 1 };
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
  const layered = !!picture.background && !!picture.foregrounds?.length && edit.foreground;
  // Character-only layers do not have a Background role. Until the Android Live2D
  // pipeline supports those manifests, publish their current ordered composite as
  // a static PNG instead of silently publishing the original uploaded photo.
  const characterParts = !picture.background && (picture.layers?.length || 0) >= 2;
  // Bake actual crop/filter settings into a new source before freezing a static version.
  const rendered =
    edit.filter !== "original" ||
    edit.crop !== "original" ||
    edit.scale !== 1 ||
    characterParts ||
    (!!picture.background && !edit.foreground);
  if (picture.assetId && layered && !picture.projectId)
    return post<Asset>(`/api/wallpapers/${picture.assetId}/snapshot`, {
      motionEnabled: !!edit.mouseEnabled || !!edit.gyroEnabled, strength: edit.strength,
      mouseEnabled: !!edit.mouseEnabled, gyroEnabled: !!edit.gyroEnabled,
      render: { strength: edit.strength, filter: edit.filter, crop: edit.crop, scale: edit.scale, foreground: edit.foreground, cropRegion: edit.cropRegion },
    });
  let temporaryProjectId: string | undefined;
  if ((!layered && rendered) || !projectId) {
    const blob = await renderImage(picture, edit);
    const created = await uploadProject(
      new File([blob], `${picture.name}.png`, { type: "image/png" }),
    );
    projectId = created.projectId;
    temporaryProjectId = created.projectId;
  }
  const mode =
    layered
      ? "Live2D"
      : "Static";
  try {
    return await post<Asset>("/api/wallpapers", {
    projectId,
    title: picture.name,
    mode,
    motionEnabled: !!edit.mouseEnabled || !!edit.gyroEnabled,
    mouseEnabled: !!edit.mouseEnabled,
    gyroEnabled: !!edit.gyroEnabled,
    render: mode === "Live2D" ? { strength: edit.strength, filter: edit.filter, crop: edit.crop, scale: edit.scale, foreground: edit.foreground, cropRegion: edit.cropRegion } : undefined,
    processingJobId: !rendered && mode === "Static" ? picture.jobId : undefined,
    originProjectId: picture.projectId,
    reuseExisting: true,
    });
  } finally {
    // The asset owns copies of these pixels. This freshly uploaded intermediate is not a library image.
    if (temporaryProjectId && temporaryProjectId !== picture.projectId)
      await request(`/api/projects/${temporaryProjectId}`, { method: "DELETE" }).catch(() => {});
  }
}
export async function saveImageEdit(picture: Picture): Promise<SavedEdit> {
  const path = picture.assetId ? `/api/wallpapers/${picture.assetId}`
    : picture.projectId ? `/api/projects/${picture.projectId}` : null;
  if (!path) throw new Error("저장할 이미지를 확인해 주세요.");
  return request<SavedEdit>(`${path}/edit`, {
    method: "PUT", body: JSON.stringify({ ...(picture.edit || defaultEdit), title: picture.name, tags: picture.tags || [] }),
  });
}
export async function processProject(
  picture: Picture,
  operation: string,
  targetLongEdge?: number,
) {
  if (!picture.projectId)
    throw new Error(
      "저장된 결과물은 먼저 원본 프로젝트를 선택해 처리해 주세요.",
    );
  return post<Job>(`/api/projects/${picture.projectId}/process`, {
    operation,
    ...(operation === "Upscale" ? { targetLongEdge } : {}),
  });
}
