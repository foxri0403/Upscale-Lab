import type { Job } from "./api";
import { processingPercent, processingStage } from "./processing-status";

/** Shared stage-based progress display for Upscale, background separation and Live2D. */
export function JobProgress({ job, checkingJob = false }: {
  job?: Job | null;
  checkingJob?: boolean;
}) {
  const percent = processingPercent(job);
  const stage = processingStage(job, checkingJob);
  return <div className="studio-job-progress" role="status" aria-live="polite">
    <div className="studio-job-progress-heading">
      <span>진행률</span>
      <strong className="studio-job-progress-percent">{percent}%</strong>
    </div>
    <progress className="studio-processing-progress" max={100} value={percent} aria-label="이미지 처리 진행률" />
    <p className="studio-job-progress-stage">현재 작업: {stage}</p>
    <p className="studio-job-progress-note">퍼센트는 처리 단계 기준의 참고값이며 AI 내부의 정확한 연산 완료율을 나타내지 않습니다.</p>
  </div>;
}
