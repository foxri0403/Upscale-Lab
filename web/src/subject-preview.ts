import type { Job, Project } from "./api";

export type SubjectPreviewApi = {
  json: <T>(path: string, init?: RequestInit) => Promise<T>;
  blob: (path: string, signal: AbortSignal) => Promise<Blob>;
  remove: (path: string) => Promise<unknown>;
};
const pause = (signal: AbortSignal) => new Promise<void>((resolve, reject) => {
  const timer = setTimeout(done, 1800);
  function done() { signal.removeEventListener("abort", cancel); resolve(); }
  function cancel() { clearTimeout(timer); signal.removeEventListener("abort", cancel); reject(signal.reason); }
  signal.addEventListener("abort", cancel, { once: true }); if (signal.aborted) cancel();
});

// The draft owns preview work. The edited project's layers change only on explicit apply.
// Interrupted running drafts are retained for the existing server draft-cleanup policy.
export async function subjectPreview(api: SubjectPreviewApi, image: Blob, signal: AbortSignal,
  changed: (job: Job) => void, wait = pause): Promise<Blob> {
  let projectId: string | undefined, terminal = true;
  try {
    signal.throwIfAborted();
    const form = new FormData(), type = image.type || "image/png";
    form.append("File", image.type ? image : new Blob([image], { type }), `preview.${type.split("/")[1].split(";")[0]}`);
    form.append("Title", "피사체 선택 미리보기"); form.append("IsDraft", "true");
    projectId = (await api.json<Project>("/api/projects", { method: "POST", body: form, signal })).id;
    terminal = false;
    let job = await api.json<Job>(`/api/projects/${projectId}/process`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ operation: "Layers" }), signal,
    });
    const deadline = Date.now() + 20 * 60 * 1000;
    while (true) {
      changed(job);
      if (job.status === "Completed" || job.status === "Failed") {
        terminal = true;
        if (job.status === "Failed") throw new Error(job.errorMessage || "피사체 인식에 실패했습니다.");
        break;
      }
      signal.throwIfAborted(); if (Date.now() > deadline) throw new Error("피사체 인식 대기 시간이 초과됐습니다.");
      await wait(signal);
      job = await api.json<Job>(`/api/projects/${projectId}/processing-status`, { signal });
    }
    const project = await api.json<Project>(`/api/projects/${projectId}`, { signal });
    const foreground = project.layers?.find(layer => layer.layerType !== "Background");
    if (!foreground) throw new Error("피사체 인식 결과가 없습니다.");
    return await api.blob(`/api/projects/${projectId}/content?layerId=${foreground.id}`, signal);
  } finally {
    if (projectId && terminal) await api.remove(`/api/projects/${projectId}`).catch(() => {});
  }
}
