import type { Job } from "./api";
import type { ReactNode } from "react";
import { JobProgress } from "./JobProgress";
import { processingLabel } from "./processing-status";
import { Busy, Modal } from "./ui";

export function ProcessingDialog({ job, operation, checkingJob = false, cancelPrompt = false,
  cancelling = false, canCancel, onCancelPrompt, onCancel, footer }: {
  job?: Job | null;
  operation: string;
  checkingJob?: boolean;
  cancelPrompt?: boolean;
  cancelling?: boolean;
  canCancel: boolean;
  onCancelPrompt: (show: boolean) => void;
  onCancel: () => void;
  footer?: ReactNode;
}) {
  return <Modal title={cancelPrompt ? "작업을 취소할까요?" : "이미지 처리 중"} close={() => {}} dismissible={false}>
    {cancelPrompt ? <>
      <p className="studio-cancel-warning" role="alert">작업을 취소하면 결과를 받을 수 없으며, 사용한 토큰은 반환되지 않습니다.</p>
      <div className="studio-processing-actions">
        <button type="button" className="button secondary" disabled={cancelling} onClick={() => onCancelPrompt(false)}>작업 계속하기</button>
        <button type="button" className="button danger" disabled={cancelling} onClick={onCancel}>{cancelling ? <Busy /> : null}토큰 반환 없이 취소</button>
      </div>
    </> : <>
      <p className="studio-processing-status"><Busy /> {checkingJob && !job ? "진행 중인 작업 확인 중" : `${processingLabel(job?.operation ?? operation)} 처리 중`}</p>
      <JobProgress job={job} operation={operation} checkingJob={checkingJob} />
      <p className="notice">작업이 끝날 때까지 다른 기능이나 화면으로 전환할 수 없습니다.</p>
      <button type="button" className="button secondary full" disabled={!canCancel} onClick={() => onCancelPrompt(true)}>작업 취소</button>
    </>}
    {footer}
  </Modal>;
}
