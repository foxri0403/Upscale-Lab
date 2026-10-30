import type { Job, Project } from "./api";
import type { Picture } from "./model";
import { unspecifiedTag } from "./image-tags";

const prefix = "/__separation-lab";
let session: Promise<{ accessToken: string }> | undefined;
const headers = { "X-Upscale-Separation-Lab": "1" };
async function bootstrap() {
  if (!session) session = fetch(prefix + "/api/lab/session", { headers, cache: "no-store" })
    .then(async r => { if (!r.ok) throw new Error("로컬 분리 API에 연결하지 못했습니다. 테스트 실행 스크립트를 확인해 주세요."); return r.json(); })
    .catch(error => { session = undefined; throw error; });
  return session;
}
export async function labRequest(path: string, init: RequestInit = {}, retry = true): Promise<Response> {
  const token = await bootstrap();
  const h = new Headers(init.headers); h.set("X-Upscale-Separation-Lab", "1"); h.set("Authorization", "Bearer " + token.accessToken);
  const response = await fetch(prefix + path, { ...init, headers: h, cache: "no-store", signal: init.signal || AbortSignal.timeout(180000) });
  if (response.status === 401 && retry) { session = undefined; return labRequest(path, init, false); }
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail || Object.values(problem.errors || {}).flat()[0] || `요청을 처리하지 못했습니다. (${response.status})`);
  }
  return response;
}
export async function labJson<T>(path: string, init: RequestInit = {}): Promise<T> { return (await labRequest(path, init)).json(); }
export const labPost = <T>(path: string, body: unknown, signal?: AbortSignal) => labJson<T>(path, {
  method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body), signal,
});
export type LabPicture = Picture & { objectUrls: string[] };
export async function labPicture(projectId: string, signal?: AbortSignal): Promise<LabPicture> {
  const project = await labJson<Project>(`/api/projects/${projectId}`, { signal });
  const urls: string[] = [];
  const content = async (query = "") => {
    const response = await labRequest(`/api/projects/${projectId}/content${query}`, { signal });
    const url = URL.createObjectURL(await response.blob()); urls.push(url); return url;
  };
  try {
    const src = await content(); const layers = [...project.layers || []].sort((a, b) => a.layerOrder - b.layerOrder);
    const subjects = layers.filter(x => x.layerType !== "Background"), background = layers.find(x => x.layerType === "Background");
    const foregrounds = await Promise.all(subjects.map(x => content(`?layerId=${x.id}`)));
    return { id: project.id, projectId: project.id, src, name: project.title, created: project.createdAt,
      genre: unspecifiedTag, width: project.originalWidth, height: project.originalHeight,
      background: background ? await content(`?layerId=${background.id}`) : undefined,
      foregrounds, subjectLayerId: subjects[0]?.id, kind: subjects.length ? "배경 분리" : undefined, objectUrls: urls };
  } catch (error) { urls.forEach(url => URL.revokeObjectURL(url)); throw error; }
}
export function releaseLabPicture(picture: LabPicture) { picture.objectUrls.forEach(url => URL.revokeObjectURL(url)); }
export async function waitForLabJob(projectId: string, signal: AbortSignal, changed: (job: Job) => void) {
  const deadline = Date.now() + 20 * 60 * 1000;
  while (Date.now() < deadline) {
    signal.throwIfAborted();
    const job = await labJson<Job>(`/api/projects/${projectId}/processing-status`, { signal }); changed(job);
    if (job.status === "Completed") return job;
    if (job.status === "Failed") throw new Error(job.errorMessage || "분리 작업에 실패했습니다. 선택 영역을 유지한 상태로 다시 시도할 수 있습니다.");
    await new Promise<void>((resolve, reject) => {
      const done = () => { signal.removeEventListener("abort", cancel); resolve(); };
      const timer = setTimeout(done, 1800);
      const cancel = () => { clearTimeout(timer); signal.removeEventListener("abort", cancel); reject(signal.reason); };
      signal.addEventListener("abort", cancel, { once: true });
      if (signal.aborted) cancel();
    });
  }
  throw new Error("분리 결과를 기다리는 시간이 초과됐습니다. 새로고침 후 작업 상태를 확인해 주세요.");
}
