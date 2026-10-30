import { useEffect, useRef, useState } from "react";
import { ArrowLeft, Download, Hand, ImagePlus, Layers3, LoaderCircle, Scan, Sparkles } from "lucide-react";
import type { Job, Project } from "./api";
import { SubjectSeparationCanvas, SubjectSeparationControls, useSubjectSeparation } from "./SubjectSeparation";
import { labJson, labPicture, labPost, labRequest, labSubjectPreview, releaseLabPicture, waitForLabJob, type LabPicture } from "./separation-lab-client";
import { validateImageUpload } from "./image-upload";
import { previewStageStyle } from "./StudioPreview";
import { usePreviewPan } from "./use-preview-pan";
import { SmartSelectionClient } from "./smart-selection-client";
import "./separation-lab.css";

type View = "selection" | "subject" | "background" | "composite";
export function SeparationLab() {
  const [picture, setPicture] = useState<LabPicture>();
  const [view, setView] = useState<View>("selection"), [zoom, setZoom] = useState("fit"), [hand, setHand] = useState(false);
  const [busy, setBusy] = useState(false), [job, setJob] = useState<Job>(), [error, setError] = useState("");
  const [connection, setConnection] = useState<"checking" | "ready" | "failed">("checking");
  const [message, setMessage] = useState("이미지를 선택한 뒤 자동 분리를 실행해 주세요.");
  const smartClient = useRef<SmartSelectionClient | null>(null);
  const input = useRef<HTMLInputElement>(null), active = useRef<AbortController | null>(null), current = useRef(picture);
  current.current = picture;
  const separated = !!picture?.background && !!picture.foregrounds?.length;
  const pan = usePreviewPan(zoom !== "fit", hand);
  const editor = useSubjectSeparation(picture, !!picture, busy || pan.canPan, {
    refine: (input, progress) => { smartClient.current ||= new SmartSelectionClient(() => {}); return smartClient.current.refine(input, progress); },
    onRefined: () => {
      setMessage("선택 결과를 확인한 뒤 수정 사항 적용을 눌러 레이어를 갱신하세요.");
    },
  });
  const width = picture?.width || 1536, height = picture?.height || 1024;
  useEffect(() => { editor.setView(view === "selection" ? "overlay" : "cutout"); }, [view]);
  useEffect(() => () => { smartClient.current?.dispose(); smartClient.current = null; }, []);
  useEffect(() => {
    let alive = true;
    labJson("/api/projects/capabilities").then(() => { if (alive) setConnection("ready"); })
      .catch(e => { if (alive) { setConnection("failed"); setError(e.message); } });
    return () => { alive = false; active.current?.abort(); if (current.current) releaseLabPicture(current.current); };
  }, []);
  function replace(next: LabPicture) {
    const previous = current.current; current.current = next; setPicture(next);
    if (previous) setTimeout(() => releaseLabPicture(previous), 1500);
  }
  async function open(file?: File) {
    if (!file || busy || editor.tracing) return;
    if (editor.dirty && !window.confirm("적용하지 않은 선택 영역을 버리고 다른 이미지를 열까요?")) return;
    setBusy(true); setError(""); active.current?.abort(); const controller = new AbortController(); active.current = controller;
    try {
      const type = validateImageUpload(file), form = new FormData();
      form.append("File", new File([file], file.name, { type })); form.append("Title", file.name.replace(/\.[^.]+$/, "")); form.append("IsDraft", "false");
      const project = await labJson<Project>("/api/projects", { method: "POST", body: form, signal: controller.signal });
      const next = await labPicture(project.id, controller.signal); const previous = current.current;
      replace(next); setZoom("fit"); setHand(false); setView("selection"); setJob(undefined); editor.reload();
      setMessage("이미지를 불러왔습니다. 자동 분리로 피사체와 배경을 나누세요.");
      if (previous?.projectId) await labRequest(`/api/projects/${previous.projectId}`, { method: "DELETE" }).catch(() => {});
    } catch (e) { if (!controller.signal.aborted) setError((e as Error).message); }
    finally { if (!controller.signal.aborted) setBusy(false); if (input.current) input.current.value = ""; }
  }
  async function process(manual: boolean) {
    if (!picture?.projectId || busy || editor.tracing || editor.drawing || editor.polygonPoints) return;
    if (!manual) {
      if (editor.startLocked) return;
      setView("selection"); setHand(false);
      if (editor.hasGuides) { await editor.recognize(); return; }
      setBusy(true); setError(""); active.current?.abort(); const controller = new AbortController(); active.current = controller;
      try {
        const foreground = editor.hasReference ? undefined : await labSubjectPreview(picture.projectId, controller.signal, setJob);
        controller.signal.throwIfAborted(); await editor.previewAutomatic(foreground);
      } catch (e) { if (!controller.signal.aborted) setError((e as Error).message); }
      finally { if (!controller.signal.aborted) { setBusy(false); setJob(undefined); } }
      return;
    }
    if (!editor.dirty || !editor.recognized) return;
    setBusy(true); setJob(undefined); setError(""); active.current?.abort(); const controller = new AbortController(); active.current = controller;
    try {
      const form = new FormData(); form.append("mask", await editor.exportMask(), "subject-mask.png"); form.append("baseLayerId", picture.subjectLayerId || "00000000-0000-0000-0000-000000000000");
      const started = await labJson<Job>(`/api/projects/${picture.projectId}/subject-mask`, { method: "POST", body: form, signal: controller.signal });
      setJob(started); setMessage(manual ? "선택 영역으로 피사체와 배경을 다시 만들고 있습니다." : "피사체를 인식하고 가려진 배경을 채우고 있습니다.");
      await waitForLabJob(picture.projectId, controller.signal, setJob);
      replace(await labPicture(picture.projectId, controller.signal));
      editor.markApplied();
      setMessage(manual ? "AI가 찾은 피사체 경계로 두 레이어를 갱신했습니다. 배경·합성 보기도 확인해 보세요." : "자동 분리를 마쳤습니다. 브러시나 올가미로 대략 표시하면 AI가 피사체 경계를 다시 찾습니다.");
    } catch (e) { if (!controller.signal.aborted) { setError((e as Error).message); setMessage("기존 결과와 수정 중인 선택 영역은 유지됩니다."); } }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }
  async function sample() {
    try { const blob = await (await fetch("/images/fox.png")).blob(); await open(new File([blob], "여우 샘플.png", { type: "image/png" })); }
    catch (e) { setError((e as Error).message); }
  }
  async function cleanup() {
    if (busy || editor.dirty) return;
    setBusy(true); setError("");
    try { await labPost("/api/lab/cleanup", {}); if (current.current) releaseLabPicture(current.current); current.current = undefined; setPicture(undefined); setJob(undefined); setMessage("테스트 파일을 정리했습니다. 새 이미지를 열 수 있습니다."); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  async function download(kind: "subject" | "background" | "mask") {
    if (!picture || busy) return;
    try {
      const temporary = kind === "mask" ? URL.createObjectURL(await editor.exportMask()) : undefined;
      const url = temporary || (kind === "subject" ? picture.foregrounds?.[0] : picture.background); if (!url) return;
      const a = document.createElement("a"); a.href = url; a.download = `${picture.name}-${kind}.png`; a.click();
      if (temporary) setTimeout(() => URL.revokeObjectURL(temporary), 1000);
    } catch (e) { setError((e as Error).message); }
  }
  return <main className="separation-lab">
    <header className="lab-header"><a href="/" aria-label="Upscale Lab 홈"><ArrowLeft size={19} /></a><div><p>UPSCALE LAB · LOCAL TEST</p><h1>피사체 분리 실험실</h1></div><span className={`lab-connection ${connection}`}><i />{connection === "ready" ? "분리 서비스 연결됨" : connection === "checking" ? "연결 확인 중" : "연결 확인 필요"}</span></header>
    <div className="lab-layout">
      <section className="lab-workspace" aria-label="분리 미리보기">
        <div className="lab-toolbar"><div className="lab-tabs" role="group" aria-label="미리보기 방식">
          {([["selection", "선택 영역"], ["subject", "피사체"], ["background", "배경"], ["composite", "합성"]] as const).map(([id, label]) => <button key={id} type="button" aria-pressed={view === id} disabled={!separated || busy || editor.drawing || editor.tracing} onClick={() => setView(id)}>{label}</button>)}
        </div><div className="lab-zoom"><button type="button" aria-label="화면 이동" aria-pressed={hand} disabled={zoom === "fit"} onClick={() => setHand(!hand)}><Hand size={17} /></button><select aria-label="미리보기 배율" value={zoom} onChange={e => { setZoom(e.target.value); setHand(false); }}><option value="fit">화면 맞춤</option>{[50, 100, 150, 200].map(x => <option key={x} value={x}>{x}%</option>)}</select></div></div>
        <div className={`lab-viewport ${pan.canPan ? "preview-can-pan" : ""}`} ref={pan.root} {...pan.events}>
          {!picture ? <div className="lab-empty"><Layers3 size={46} /><h2>피사체를 선택하고, 배경을 완성하세요</h2><p>자동 분리 후 브러시·올가미로 선택 영역을 고칠 수 있습니다.</p><button className="button primary" disabled={busy || connection !== "ready"} onClick={() => input.current?.click()}><ImagePlus size={18} />이미지 선택</button><button type="button" className="lab-sample" disabled={busy || connection !== "ready"} onClick={() => void sample()}>여우 샘플로 시작</button></div> : <div className="lab-pan-surface" style={{ width: zoom === "fit" ? "100%" : width * Number(zoom) / 100 + 44, height: zoom === "fit" ? "100%" : height * Number(zoom) / 100 + 44 }}>
            <div className={`lab-plane lab-view-${view}`} style={previewStageStyle(width, height, zoom)}>
              {view === "background" && picture.background ? <img src={picture.background} alt="피사체 뒤 영역을 채운 배경" /> : separated || view === "selection" ? <>
                {view === "composite" && <img className="lab-composite-background" src={picture.background} alt="합성 배경" />}
                <SubjectSeparationCanvas editor={editor} disabled={busy || pan.canPan} filter="none" />
              </> : <img src={picture.src} alt="분리 전 원본" />}
            </div>
          </div>}
        </div>
        <footer className="lab-status"><span role="status">{(busy || editor.tracing) && <LoaderCircle size={15} className="lab-spinner" />}{editor.tracing ? editor.smartStatus : message}</span><span>{picture ? `${width.toLocaleString()} × ${height.toLocaleString()} px` : "PNG · JPEG · WebP · HEIC"}</span></footer>
        {(error || editor.error) && <div className="lab-error" role="alert">{error || editor.error}</div>}
      </section>
      <aside className="lab-sidebar">
        <section className="lab-card"><span className="lab-step">01</span><h2>이미지 준비</h2><p>{picture?.name || "수정할 사진 한 장을 선택하세요."}</p><input ref={input} type="file" accept="image/*" hidden onChange={e => void open(e.target.files?.[0])} /><button className="button secondary full" disabled={busy || connection !== "ready"} onClick={() => input.current?.click()}><ImagePlus size={17} />{picture ? "다른 이미지 선택" : "이미지 선택"}</button>{picture && <button className="lab-sample" disabled={busy} onClick={() => void sample()}>여우 샘플 열기</button>}</section>
        <section className="lab-card"><span className="lab-step">02</span><h2>피사체 자동 분리</h2><p>피사체를 인식해 투명한 전경과 채워진 배경으로 나눕니다.</p><button className="button primary full" disabled={!picture || busy || !editor.ready || editor.startLocked || editor.drawing || editor.tracing || editor.polygonPoints > 0 || connection !== "ready"} onClick={() => void process(false)}><Scan size={18} />배경·피사체 분리 시작</button>{busy && job && <div className="lab-progress"><progress value={job.progress} max="100" /><span>{job.status === "Queued" ? "GPU 작업 대기 중" : `분리 처리 중 · ${job.progress}%`}</span></div>}</section>
        <section className="lab-card lab-edit"><span className="lab-step">03</span><h2>선택 영역 수정</h2>
          <SubjectSeparationControls editor={editor} disabled={busy} hasResult={!!picture} canApply={!!picture?.projectId}
            apply={() => void process(true)} selectTool={() => { setView("selection"); setHand(false); }} />
          {editor.dirty && <p className="lab-pending"><Sparkles size={14} />{editor.tracing ? "선택한 범위에서 피사체를 인식하고 있습니다." : editor.recognized ? "수정 사항 적용을 눌러 레이어를 갱신하세요." : "피사체 다시 분리로 그린 영역에서 피사체를 찾으세요."}</p>}
        </section>
        <section className="lab-card"><h2>결과 확인</h2><div className="lab-exports"><button disabled={!separated || busy || editor.dirty} onClick={() => void download("subject")}><Download size={15} />피사체 PNG</button><button disabled={!separated || busy || editor.dirty} onClick={() => void download("background")}><Download size={15} />배경 PNG</button><button disabled={!separated || busy || editor.drawing || editor.tracing} onClick={() => void download("mask")}><Download size={15} />선택 마스크</button></div><button className="lab-cleanup" disabled={busy || editor.dirty || !picture} onClick={() => void cleanup()}>테스트 파일 정리</button></section>
      </aside>
    </div><p className="lab-footnote">로컬 테스트 화면입니다. 선택 경계 인식은 브라우저에서, 자동 분리·배경 채움은 RunPod에서 처리합니다. 테스트 이미지는 전용 임시 계정 영역에 저장됩니다.</p>
  </main>;
}
