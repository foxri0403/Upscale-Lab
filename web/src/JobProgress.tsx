import type { Job } from "./api";
import { processingLabel, processingPercent, processingStage } from "./processing-status";

/** Shared stage-based progress display for Upscale, background separation and Live2D. */
export function JobProgress({ job, checkingJob = false, operation }: {
  job?: Job | null;
  checkingJob?: boolean;
  operation?: string;
}) {
  const percent = processingPercent(job);
  const stage = processingStage(job, checkingJob);
  return <div className="studio-job-progress" role="status" aria-live="polite">
    <div className="studio-job-progress-heading">
      <span>{processingLabel(job?.operation ?? operation)} 진행률</span>
      <strong className="studio-job-progress-percent">{percent}%</strong>
    </div>
    <progress className="studio-processing-progress" max={100} value={percent} aria-label="이미지 처리 진행률" />
    <p className="studio-job-progress-stage">현재 작업: {stage}</p>
  </div>;
}
