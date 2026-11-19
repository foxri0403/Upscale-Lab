// Uses the production dialog with synthetic API milestones; no GPU connection.
import { useState } from "react";
import { createRoot } from "react-dom/client";
import { CharacterMotionControls } from "../src/CharacterMotionControls";
import { ProcessingDialog } from "../src/ProcessingDialog";
import { defaultHeadRig, type HeadRig } from "../src/model";
import "../src/styles.css";
import "../src/responsive.css";
import "../src/studio.css";

const params = new URLSearchParams(location.search);
function Fixture() {
  const [rig, setRig] = useState<HeadRig>({ ...defaultHeadRig, faceEnabled: false, bodyMode: "fixed" });
  const [cancelPrompt, setCancelPrompt] = useState(false);
  const [cancelled, setCancelled] = useState(false);
  const operation = params.get("operation") || "Upscale";
  const status = params.get("status") || "Processing";
  const checkingJob = status === "Checking";
  return <main style={{ padding: 24 }}>
    <h1>처리 팝업 UI 검사</h1>
    <div style={{ width: "min(100%, 340px)" }}>
      <CharacterMotionControls rig={rig} onChange={setRig} disabled={params.has("disabled")} />
    </div>
    {params.has("dialog") && !cancelled && <ProcessingDialog
      job={checkingJob || status === "Starting" ? null : {
        jobId: "simulated-job", operation, status, progress: Number(params.get("progress") || 0),
        hasImageResult: false, keepBackground: params.has("keepBackground"),
      }} operation={operation} checkingJob={checkingJob} cancelPrompt={cancelPrompt}
      canCancel={status === "Processing" || status === "Queued"}
      onCancelPrompt={setCancelPrompt} onCancel={() => setCancelled(true)} />}
    {cancelled && <p role="status">가상 작업 취소됨</p>}
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
