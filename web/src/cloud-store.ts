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
    genre: "풍경",
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
    genre: "풍경",
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
  const [projects, devices, assets, publicAssets] = await Promise.all([
    request<Project[]>("/api/projects"),
    request<CloudDevice[]>("/api/devices"),
    request<Asset[]>("/api/wallpapers"),
    request<Asset[]>("/api/wallpapers/shared"),
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
          s.platform === "Android"
            ? w.target === "Lock"
              ? "잠금 화면"
              : "홈 화면"
            : d.name,
        resolution: `${d.width} × ${d.height}`,
        connected: d.isConnected,
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
  const images = [...projects.map(projectPicture), ...ownPictures].map((p) => ({
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
    publicImages: publicPictures.map((p) => ({
      ...p,
      creator: owned.has(p.assetId!) ? "내 공유 이미지" : "공유 배경화면",
    })),
  };
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
  if (mode === "Live2D") {
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
