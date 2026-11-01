import type { Job } from "./api";

export function processingStatus(job: Pick<Job, "status" | "progress" | "operation" | "errorMessage">): string {
  if (job.status === "Failed") return job.errorMessage || "처리 실패";
  if (job.status === "Completed") return "처리 완료";
  if (job.status === "Queued") return "작업 대기 중";
  if (job.progress === 20 && job.operation !== "Upscale")
    return "GPU 할당 대기 중 · 준비 상황에 따라 몇 분 걸릴 수 있습니다.";
  if (job.progress === 45 && job.operation === "CharacterLayers")
    return "캐릭터 파츠 생성 중 · 여러 파츠를 처리하므로 시간이 걸릴 수 있습니다.";
  return `${job.progress < 20 ? "이미지 준비 중" : job.progress >= 70 ? "결과 저장 중" : "AI 처리 중"} · ${job.progress}%`;
}
