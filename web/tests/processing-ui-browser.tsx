// Uses the production dialog with synthetic API milestones; no GPU connection.
import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { CharacterMotionControls } from "../src/CharacterMotionControls";
import { ProcessingDialog } from "../src/ProcessingDialog";
import { defaultHeadRig, type HeadRig } from "../src/model";
import "../src/styles.css";
import "../src/responsive.css";
import "../src/studio.css";
import "./local-preview.css";
import { processingLabel } from "../src/processing-status";

const params = new URLSearchParams(location.search);
function Fixture() {
  const [rig, setRig] = useState<HeadRig>({ ...defaultHeadRig, faceEnabled: false, bodyMode: "fixed" });
  const [cancelPrompt, setCancelPrompt] = useState(false);
  const [cancelled, setCancelled] = useState(false);
  const [operation, setOperation] = useState(params.get("operation") || "Upscale");
  const [status, setStatus] = useState(params.get("status") || "Processing");
  const initial = Number(params.get("progress") || 0);
  const [progress, setProgress] = useState(Number.isFinite(initial) ? Math.max(0, Math.min(100, initial)) : 0);
  const [keepBackground, setKeepBackground] = useState(params.has("keepBackground"));
  const [show, setShow] = useState(params.has("dialog"));
  const [playing, setPlaying] = useState(false);
  useEffect(() => {
    if (!playing || cancelPrompt) return;
    const timer = window.setInterval(() => setProgress(value => Math.min(100, value + 5)), 600);
    return () => clearInterval(timer);
  }, [playing, cancelPrompt]);
  useEffect(() => {
    if (!playing) return;
    if (progress >= 100) {
      setPlaying(false); setShow(false); setStatus("Completed");
    } else setStatus(progress === 0 ? "Queued" : "Processing");
  }, [playing, progress]);
  function start(auto: boolean) {
    setCancelPrompt(false); setCancelled(false);
    if (auto) { setProgress(0); setStatus("Queued"); }
    setPlaying(auto); setShow(true);
  }
  const checkingJob = status === "Checking";
  return <main className="local-preview">
    <h1>처리 팝업 UI 검사</h1>
    <p><a href="/tests/local-preview.html">로컬 테스트 시작 화면</a></p>
    <p>기능과 진행률을 선택하거나 자동 재생을 시작해 실제 처리 팝업을 확인하세요.</p>
    <fieldset>
      <legend>가상 작업 설정</legend>
      <label>기능 <select aria-label="처리 기능" value={operation} onChange={event => setOperation(event.target.value)}>
        {["Upscale", "Layers", "CharacterLayers"].map(value => <option key={value} value={value}>{processingLabel(value)}</option>)}
      </select></label>
      <label>진행률 <input aria-label="가상 진행률" type="range" min={0} max={100} step={1} value={progress}
        onChange={event => setProgress(event.currentTarget.valueAsNumber)} /><output>{progress}%</output></label>
      <label>상태 <select aria-label="작업 상태" value={status} onChange={event => setStatus(event.target.value)}>
        {[["Processing", "처리 중"], ["Queued", "대기"], ["Checking", "기존 작업 확인"], ["Starting", "요청 중"], ["Completed", "완료"]].map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </select></label>
      {operation === "CharacterLayers" && <label><input type="checkbox" checked={keepBackground}
        onChange={event => setKeepBackground(event.target.checked)} />배경 유지</label>}
      <div className="local-preview-actions">
        <button className="button primary" type="button" onClick={() => start(true)}>0~100% 자동 재생</button>
        <button className="button secondary" type="button" onClick={() => start(false)}>선택한 상태로 팝업 열기</button>
      </div>
    </fieldset>
    <div style={{ width: "min(100%, 340px)" }}>
      <CharacterMotionControls rig={rig} onChange={setRig} disabled={params.has("disabled")} />
    </div>
    {show && !cancelled && <ProcessingDialog
      job={checkingJob || status === "Starting" ? null : {
        jobId: "simulated-job", operation, status, progress,
        hasImageResult: false, keepBackground,
      }} operation={operation} checkingJob={checkingJob} cancelPrompt={cancelPrompt}
      canCancel={status === "Processing" || status === "Queued"}
      onCancelPrompt={setCancelPrompt} onCancel={() => { setCancelled(true); setPlaying(false); setShow(false); }}
      footer={!playing && !params.has("dialog") ? <button className="text-button" type="button" onClick={() => setShow(false)}>테스트 화면으로 돌아가기</button> : undefined} />}
    {!show && status === "Completed" && <p role="status">가상 작업 100% 완료</p>}
    {cancelled && <p role="status">가상 작업 취소됨</p>}
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
