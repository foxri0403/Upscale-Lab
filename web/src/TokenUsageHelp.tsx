import { useState } from "react";
import { Modal } from "./ui";
import { TokenUsageTable } from "./TokenUsageTable";

export function TokenUsageHelp() {
  const [open, setOpen] = useState(false);
  return <>
    <button type="button" className="token-usage-link" aria-haspopup="dialog" onClick={() => setOpen(true)}>
      소모 토큰량 확인
    </button>
    {open && <Modal title="작업별 토큰 사용량" className="token-usage-dialog" close={() => setOpen(false)}>
      <TokenUsageTable compact />
    </Modal>}
  </>;
}
