import type { Job } from "./api";

type ProcessingJob = Pick<Job, "status" | "progress"> & Partial<Pick<Job, "operation" | "errorMessage" | "keepBackground">>;

/** The API reports milestone-based progress, not GPU-internal fractional work. */
export function processingPercent(job: ProcessingJob | null | undefined): number {
  if (job?.status === "Completed") return 100;
  if (!job || job.status === "Queued") return 0;
  const value = Number(job.progress);
  return Number.isFinite(value) ? Math.max(0, Math.min(99, Math.round(value))) : 0;
}

export function processingStage(job: ProcessingJob | null | undefined, checkingJob = false): string {
  if (!job) return checkingJob ? "진행 중인 작업 확인 중" : "작업 요청 중";
  if (job.status === "Cancelled") return "작업 취소됨 · 사용한 토큰은 반환되지 않습니다.";
  if (job.status === "Failed") return job.errorMessage || "처리 실패";
  if (job.status === "Completed") return "처리 완료";
  if (job.status === "Queued") return "작업 대기 중";
  const progress = processingPercent(job);
  if (progress >= 80) return "처리 결과 저장 중";
  if (progress >= 70) return "AI 처리 결과 수신 및 확인 중";
  if (job.operation === "CharacterLayers" && job.keepBackground && progress >= 15)
    return progress < 40 ? "AI 배경 분리 중" : "피사체 Live 2D 파츠 생성 중 (See-through)";
  if (progress >= 45) {
    if (job.operation === "CharacterLayers") return "캐릭터 파츠 생성 중 (See-through) · 시간이 걸릴 수 있습니다.";
    if (job.operation === "Upscale") return "AI 업스케일 처리 중";
    return "AI 배경 분리 중";
  }
  if (progress >= 20) return "GPU 할당 대기 중 · 준비 상황에 따라 시간이 걸릴 수 있습니다.";
  if (progress >= 15) return "입력 이미지 준비 중";
  return "이미지 불러오는 중";
}

export function processingStatus(job: ProcessingJob): string {
  if (job.status === "Cancelled" || job.status === "Failed") return processingStage(job);
  return `${processingPercent(job)}% · ${processingStage(job)}`;
}
