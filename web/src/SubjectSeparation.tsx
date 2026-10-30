import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from "react";
import { Brush, Eraser, Lasso, Undo2, Redo2, RotateCcw } from "lucide-react";
import { loadImage, type Picture } from "./model";
import { requestBlob } from "./api";
import { canvasPng, maskPoint, MaskStrokePainter, type MaskPoint, type MaskStroke } from "./subject-mask";
import { MagneticLasso } from "./vendor/photocraft-magnetic-lasso";
import type { SmartSelection } from "./smart-selection";
import "./subject-mask.css";

type Operation = { kind: "brush"; stroke: MaskStroke } | { kind: "lasso"; points: MaskPoint[]; mode: MaskStroke["mode"] } | { kind: "clear" } | { kind: "smart"; mask: HTMLCanvasElement };
type Active = { pointerId: number; points: MaskPoint[]; mode: MaskStroke["mode"]; painter?: MaskStrokePainter; stroke?: MaskStroke; startPointCount?: number; closePolygon?: boolean };
type Session = {
  source: HTMLImageElement; subjects: HTMLImageElement[]; initial: HTMLCanvasElement; mask: HTMLCanvasElement;
  scratch: HTMLCanvasElement; operations: Operation[]; cursor: number; active: Active | null; magnetic?: MagneticLasso;
  polygon?: { points: MaskPoint[]; mode: MaskStroke["mode"] };
};
function board(width: number, height: number) {
  const c = document.createElement("canvas"); c.width = width; c.height = height; return c;
}
function subjectMask(subjects: HTMLImageElement[], width: number, height: number) {
  const canvas = board(width, height), c = canvas.getContext("2d", { willReadFrequently: true })!;
  for (const image of subjects) c.drawImage(image, 0, 0, width, height);
  const pixels = c.getImageData(0, 0, width, height);
  for (let i = 0; i < pixels.data.length; i += 4) {
    const gray = pixels.data[i + 3];
    pixels.data[i] = pixels.data[i + 1] = pixels.data[i + 2] = gray; pixels.data[i + 3] = 255;
  }
  c.putImageData(pixels, 0, 0); return canvas;
}
function applyOperation(c: CanvasRenderingContext2D, op: Operation, sx: number, sy: number) {
  if (op.kind === "smart") {
    c.drawImage(op.mask, 0, 0, c.canvas.width, c.canvas.height);
  } else if (op.kind === "clear") {
    c.fillStyle = "black"; c.fillRect(0, 0, c.canvas.width, c.canvas.height);
  } else if (op.kind === "brush") {
    const painter = new MaskStrokePainter(c, op.stroke, sx, sy);
    for (const p of op.stroke.points) painter.append(p);
  } else {
    c.beginPath(); op.points.forEach((p, i) => i ? c.lineTo(p.x * sx, p.y * sy) : c.moveTo(p.x * sx, p.y * sy));
    c.closePath(); c.fillStyle = op.mode === "erase" ? "black" : "white"; c.fill("evenodd");
  }
}
export function useSubjectSeparation(picture: Picture | undefined, enabled: boolean, disabled: boolean, smart?: SmartSelection) {
  const canvas = useRef<HTMLCanvasElement | null>(null), session = useRef<Session | null>(null);
  const frame = useRef(0), generation = useRef(0), loaded = useRef("");
  const [ready, setReady] = useState(false), [error, setError] = useState("");
  const [mode, setMode] = useState<"brush" | "eraser" | "lasso">("brush");
  const [selectionMode, setSelectionMode] = useState<"restore" | "erase">("restore");
  const [size, setSize] = useState(30);
  const [history, setHistory] = useState<{ cursor: number; length: number; operation: Operation | null }>({ cursor: 0, length: 0, operation: null });
  const [applied, setApplied] = useState<Operation | null>(null), [epoch, setEpoch] = useState(0);
  const [smartStatus, setSmartStatus] = useState("");
  const smartRef = useRef(smart); smartRef.current = smart;
  const [drawing, setDrawing] = useState(false), [tracing, setTracing] = useState(false);
  const [polygonPoints, setPolygonPoints] = useState(0);
  const [view, setView] = useState<"overlay" | "cutout" | "motion">("overlay");
  const [cursor, setCursor] = useState<{ x: number; y: number; diameter: number; erasing: boolean } | null>(null);
  const viewRef = useRef(view); viewRef.current = view;
  const disabledRef = useRef(disabled); disabledRef.current = disabled;
  const key = picture ? smart ? `${picture.id}:smart:${picture.width}:${picture.height}:${epoch}` : `${picture.id}:${picture.src}:${picture.subjectLayerId || ""}:${picture.separationDisabled || false}` : "";
  function render() {
    const s = session.current, target = canvas.current; if (!s || !target) return;
    if (target.width !== s.mask.width || target.height !== s.mask.height) { target.width = s.mask.width; target.height = s.mask.height; }
    const c = target.getContext("2d")!, sc = s.scratch.getContext("2d", { willReadFrequently: true })!;
    c.clearRect(0, 0, target.width, target.height); c.globalCompositeOperation = "source-over";
    c.drawImage(s.source, 0, 0, target.width, target.height);
    sc.drawImage(s.mask, 0, 0); const pixels = sc.getImageData(0, 0, s.mask.width, s.mask.height);
    const values = new Uint8Array(s.mask.width * s.mask.height);
    for (let i = 0; i < values.length; i++) values[i] = pixels.data[i * 4];
    const w = s.mask.width, h = s.mask.height, rim = Math.max(1, Math.ceil(w / Math.max(1, target.getBoundingClientRect().width)));
    for (let n = 0; n < values.length; n++) {
      const i = n * 4, selection = values[n], x = n % w, y = Math.floor(n / w);
      const boundary = viewRef.current !== "cutout" && selection > 127 && (x < rim || y < rim || x >= w - rim || y >= h - rim ||
        values[n - rim] <= 127 || values[n + rim] <= 127 || values[n - rim * w] <= 127 || values[n + rim * w] <= 127);
      const white = boundary && (Math.floor((x + y) / (rim * 6)) % 2 === 0);
      pixels.data[i] = white ? 255 : 136; pixels.data[i + 1] = white ? 255 : 65; pixels.data[i + 2] = white ? 255 : 210;
      pixels.data[i + 3] = viewRef.current === "cutout" ? selection : boundary ? 255 : Math.round(selection * .30);
    }
    sc.putImageData(pixels, 0, 0); c.globalCompositeOperation = viewRef.current === "cutout" ? "destination-in" : "source-over";
    c.drawImage(s.scratch, 0, 0); c.globalCompositeOperation = "source-over";
    const points = s.active?.points || s.polygon?.points;
    if (points && !s.active?.painter) {
      c.beginPath(); points.forEach((p, i) => { const x = p.x / s.source.naturalWidth * target.width, y = p.y / s.source.naturalHeight * target.height; if (i) c.lineTo(x, y); else c.moveTo(x, y); });
      c.strokeStyle = "#fff"; c.lineWidth = 2; c.setLineDash([5, 4]); c.stroke(); c.setLineDash([]);
      if (s.polygon) {
        c.beginPath(); c.arc(points[0].x / s.source.naturalWidth * target.width, points[0].y / s.source.naturalHeight * target.height, 6, 0, Math.PI * 2);
        c.fillStyle = "#8850e0"; c.fill(); c.stroke();
      }
    }
  }
  function schedule() { if (!frame.current) frame.current = requestAnimationFrame(() => { frame.current = 0; render(); }); }
  function replay() {
    const s = session.current; if (!s) return;
    const c = s.mask.getContext("2d")!; c.drawImage(s.initial, 0, 0);
    for (const op of s.operations.slice(0, s.cursor)) applyOperation(c, op, s.mask.width / s.source.naturalWidth, s.mask.height / s.source.naturalHeight);
    setHistory({ cursor: s.cursor, length: s.operations.length, operation: s.operations[s.cursor - 1] || null }); schedule();
  }
  useEffect(() => {
    if (loaded.current && loaded.current !== key) {
      generation.current++; loaded.current = ""; session.current = null;
      setHistory({ cursor: 0, length: 0, operation: null }); setApplied(null); setReady(false); setDrawing(false); setTracing(false); setPolygonPoints(0);
    }
    if (!picture || !enabled || (loaded.current === key && session.current)) return;
    let alive = true; const urls: string[] = [], gen = ++generation.current;
    setReady(false); setError(""); setHistory({ cursor: 0, length: 0, operation: null }); setApplied(null); setDrawing(false); setTracing(false); setPolygonPoints(0);
    const load = async (src: string, endpoint?: string) => {
      if (!endpoint) return loadImage(src);
      const url = URL.createObjectURL(await requestBlob(endpoint)); urls.push(url); return loadImage(url);
    };
    (async () => {
      const source = await load(picture.src, picture.exportSources?.image);
      const subjects = await Promise.all((picture.separationDisabled ? [] : picture.foregrounds || []).map((src, i) => load(src, picture.exportSources?.foregrounds?.[i])));
      if (!alive || gen !== generation.current) return;
      if (source.naturalWidth * source.naturalHeight > 64_000_000) throw new Error("최대 64메가픽셀까지 선택할 수 있습니다.");
      const scale = Math.min(1, 1600 / Math.max(source.naturalWidth, source.naturalHeight));
      const initial = subjectMask(subjects, Math.max(1, Math.round(source.naturalWidth * scale)), Math.max(1, Math.round(source.naturalHeight * scale)));
      const mask = board(initial.width, initial.height); mask.getContext("2d")!.drawImage(initial, 0, 0);
      session.current = { source, subjects, initial, mask, scratch: board(mask.width, mask.height), operations: [], cursor: 0, active: null };
      loaded.current = key; setSize(Math.max(1, Math.min(1000, Math.round(Math.max(source.naturalWidth, source.naturalHeight) * (smartRef.current ? .18 : .025)))));
      setReady(true); schedule();
    })().catch(e => { if (alive) setError(e.message || "이미지를 불러오지 못했습니다."); }).finally(() => urls.forEach(url => URL.revokeObjectURL(url)));
    return () => { alive = false; };
  }, [key, enabled]);
  useEffect(() => () => { generation.current++; session.current = null; cancelAnimationFrame(frame.current); frame.current = 0; }, []);
  useEffect(() => { schedule(); }, [view, enabled]);
  function updateCursor(event: PointerEvent<HTMLCanvasElement>) {
    if (mode === "lasso" || disabled) { setCursor(null); return; }
    const r = event.currentTarget.getBoundingClientRect(), s = session.current;
    if (s) setCursor({ x: event.clientX - r.left, y: event.clientY - r.top, diameter: size / s.source.naturalWidth * r.width,
      erasing: s.active?.mode === "erase" || mode === "eraser" || selectionMode === "erase" || (mode === "brush" && (event.buttons & 2) !== 0) });
  }
  function down(event: PointerEvent<HTMLCanvasElement>) {
    const rightErase = mode === "brush" && event.button === 2;
    const s = session.current; if (!s || !ready || disabled || tracing || s.active || !event.isPrimary || (event.button !== 0 && !rightErase)) return;
    event.preventDefault(); event.currentTarget.focus({ preventScroll: true }); setError(""); setDrawing(true);
    const p = maskPoint(event.clientX, event.clientY, event.currentTarget.getBoundingClientRect(), s.source.naturalWidth, s.source.naturalHeight);
    const stroke: MaskStroke = { mode: mode === "eraser" || rightErase ? "erase" : selectionMode, size, hardness: 100, points: [p] };
    if (mode === "lasso") {
      const previous = s.polygon, first = previous?.points[0];
      const closePolygon = !!first && previous!.points.length >= 3 && Math.hypot(p.x - first.x, p.y - first.y) < 12 * s.source.naturalWidth / event.currentTarget.getBoundingClientRect().width;
      const points = previous ? [...previous.points, ...(closePolygon ? [] : [p])] : [p];
      s.active = { pointerId: event.pointerId, points, startPointCount: points.length, closePolygon, mode: previous?.mode || stroke.mode };
    } else s.active = { pointerId: event.pointerId, points: stroke.points, mode: stroke.mode, stroke,
      painter: new MaskStrokePainter(s.mask.getContext("2d")!, stroke, s.mask.width / s.source.naturalWidth, s.mask.height / s.source.naturalHeight) };
    s.active.painter?.append(p); event.currentTarget.setPointerCapture(event.pointerId); updateCursor(event); schedule();
  }
  function move(event: PointerEvent<HTMLCanvasElement>) {
    updateCursor(event); const a = session.current?.active; if (!a || a.pointerId !== event.pointerId) return;
    const coalesced = event.nativeEvent.getCoalescedEvents?.() || [], samples = coalesced.length ? coalesced : [event];
    const s = session.current!, r = event.currentTarget.getBoundingClientRect();
    for (const e of samples) {
      const p = maskPoint(e.clientX, e.clientY, r, s.source.naturalWidth, s.source.naturalHeight), previous = a.points.at(-1)!;
      if (Math.hypot(p.x - previous.x, p.y - previous.y) < (a.painter ? .3 : Math.max(1, s.source.naturalWidth / 900 * 3))) continue;
      if (a.points.length >= 12_000) break;
      a.points.push(p); a.painter?.append(p);
    }
    schedule();
  }
  async function finish(event: PointerEvent<HTMLCanvasElement>, cancelled = false) {
    const s = session.current, a = s?.active; if (!s || !a || a.pointerId !== event.pointerId) return;
    s.active = null; setDrawing(false); setCursor(null);
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
    if (cancelled || disabledRef.current) { cancelPolygon(); replay(); return; }
    if (!a.stroke && !a.closePolygon && a.points.length === a.startPointCount) {
      s.polygon = { points: a.points, mode: a.mode }; setPolygonPoints(a.points.length); schedule(); return;
    }
    await commit(s, a);
  }
  async function commit(s: Session, a: Active) {
    const gen = generation.current; s.polygon = undefined; setPolygonPoints(0);
    try {
      let op: Operation;
      if (a.stroke) op = { kind: "brush", stroke: a.stroke };
      else if (smartRef.current) {
        if (a.points.length < 3) { replay(); return; }
        op = { kind: "lasso", mode: a.mode, points: a.points };
      }
      else {
        if (a.points.length < 3) { replay(); return; }
        setTracing(true); await new Promise(resolve => setTimeout(resolve, 0));
        if (!s.magnetic) {
          const scale = Math.min(1, 900 / Math.max(s.source.naturalWidth, s.source.naturalHeight));
          const small = board(Math.max(1, Math.round(s.source.naturalWidth * scale)), Math.max(1, Math.round(s.source.naturalHeight * scale)));
          const c = small.getContext("2d", { willReadFrequently: true })!; c.drawImage(s.source, 0, 0, small.width, small.height);
          s.magnetic = new MagneticLasso(small.width, small.height, c.getImageData(0, 0, small.width, small.height).data);
        }
        const magnetic = s.magnetic, sx = magnetic.width / s.source.naturalWidth, sy = magnetic.height / s.source.naturalHeight;
        const guide = a.points.map(p => ({ x: p.x * sx, y: p.y * sy })), first = magnetic.snap(guide[0]), path: MaskPoint[] = [first];
        for (let i = 1; i <= guide.length; i++) {
          const end = i === guide.length ? first : magnetic.snap(guide[i]); path.push(...magnetic.trace(path.at(-1)!, end).slice(1));
          if (path.length > 100_000) throw new Error("올가미 영역을 나누어 선택해 주세요.");
          if (i % 12 === 0) { await new Promise(resolve => setTimeout(resolve, 0)); if (gen !== generation.current || disabledRef.current) return; }
        }
        op = { kind: "lasso", mode: a.mode, points: path.map(p => ({ x: p.x / sx, y: p.y / sy })) };
      }
      if (smartRef.current && (op.kind === "brush" || op.kind === "lasso")) {
        setTracing(true); setSmartStatus("피사체 경계를 찾는 중…"); replay();
        const guide = board(s.mask.width, s.mask.height), source = board(s.mask.width, s.mask.height);
        const gc = guide.getContext("2d", { willReadFrequently: true })!, sc = source.getContext("2d", { willReadFrequently: true })!;
        gc.fillStyle = "black"; gc.fillRect(0, 0, guide.width, guide.height);
        const positive: Operation = op.kind === "brush" ? { kind: "brush", stroke: { ...op.stroke, mode: "restore" } } : { ...op, mode: "restore" };
        applyOperation(gc, positive, s.mask.width / s.source.naturalWidth, s.mask.height / s.source.naturalHeight);
        sc.drawImage(s.source, 0, 0, source.width, source.height);
        const gray = (c: HTMLCanvasElement) => {
          const data = c.getContext("2d", { willReadFrequently: true })!.getImageData(0, 0, c.width, c.height).data, out = new Uint8Array(c.width * c.height);
          for (let i = 0; i < out.length; i++) out[i] = data[i * 4]; return out;
        };
        const result = await smartRef.current.refine({ imageKey: picture!.id, width: s.mask.width, height: s.mask.height,
          pixels: sc.getImageData(0, 0, source.width, source.height).data, guide: gray(guide), prior: gray(s.mask), reference: gray(s.initial),
          mode: op.kind === "brush" ? op.stroke.mode : op.mode, tool: op.kind === "brush" ? "brush" : "lasso" }, message => { if (gen === generation.current) setSmartStatus(message); });
        if (gen !== generation.current || disabledRef.current) return;
        if (result.alpha.length !== s.mask.width * s.mask.height) throw new Error("선택 AI의 캔버스 크기가 일치하지 않습니다.");
        const output = board(s.mask.width, s.mask.height), context = output.getContext("2d")!, pixels = context.createImageData(output.width, output.height);
        for (let i = 0; i < result.alpha.length; i++) { pixels.data[i * 4] = pixels.data[i * 4 + 1] = pixels.data[i * 4 + 2] = result.alpha[i]; pixels.data[i * 4 + 3] = 255; }
        context.putImageData(pixels, 0, 0); op = { kind: "smart", mask: output };
      }
      if (gen !== generation.current || disabledRef.current) return;
      s.operations = [...s.operations.slice(0, s.cursor), op]; s.cursor++; replay();
      if (op.kind === "smart") smartRef.current?.onRefined?.();
    } catch (e) { if (gen === generation.current) { setError((e as Error).message); replay(); } }
    finally { if (gen === generation.current) setTracing(false); }
  }
  async function exportMask() {
    const s = session.current; if (!s || !ready || tracing || s.active || s.polygon) throw new Error("선택 영역을 완성한 뒤 분리해 주세요.");
    const native = subjectMask(s.subjects, s.source.naturalWidth, s.source.naturalHeight), c = native.getContext("2d")!;
    for (const op of s.operations.slice(0, s.cursor)) applyOperation(c, op, 1, 1);
    const image = c.getImageData(0, 0, native.width, native.height); let kept = 0;
    for (let i = 0; i < image.data.length; i += 4) kept += image.data[i];
    const ratio = kept / (native.width * native.height * 255);
    if (ratio < .001 || ratio > .98) throw new Error("피사체와 배경 영역이 모두 남도록 선택해 주세요.");
    return canvasPng(native);
  }
  function cancelPolygon() { const s = session.current; if (s) s.polygon = undefined; setPolygonPoints(0); schedule(); }
  function keyDown(event: KeyboardEvent<HTMLCanvasElement>) {
    const s = session.current;
    if (!s?.polygon || disabledRef.current || drawing || tracing) return;
    if (event.key === "Escape") { event.preventDefault(); cancelPolygon(); }
    else if (event.key === "Enter" && s.polygon.points.length >= 3) {
      event.preventDefault(); void commit(s, { pointerId: -1, points: s.polygon.points, mode: s.polygon.mode });
    }
  }
  function reset() { const s = session.current; if (!s || drawing || tracing) return; cancelPolygon(); s.operations = []; s.cursor = 0; replay(); smartRef.current?.onRefined?.(); }
  function clearSelection() {
    const s = session.current; if (!s || !ready || drawing || tracing || disabledRef.current) return;
    cancelPolygon(); s.operations = [...s.operations.slice(0, s.cursor), { kind: "clear" }]; s.cursor++; replay();
  }
  return { ready, error, mode, setMode: (value: typeof mode) => { cancelPolygon(); setMode(value); },
    selectionMode, setSelectionMode: (value: typeof selectionMode) => { cancelPolygon(); setSelectionMode(value); }, size, setSize, history, drawing, tracing, polygonPoints, view, setView, cursor, smart: !!smart, smartStatus,
    dirty: history.operation !== applied || drawing || tracing || polygonPoints > 0, exportMask, reset, clearSelection,
    markApplied: () => { const s = session.current; if (s) setApplied(s.operations[s.cursor - 1] || null); }, reload: () => setEpoch(value => value + 1),
    undo: () => { const s = session.current; if (s?.polygon) cancelPolygon(); else if (s && s.cursor > 0) { s.cursor--; replay(); smartRef.current?.onRefined?.(); } },
    redo: () => { const s = session.current; if (s && s.cursor < s.operations.length) { s.cursor++; replay(); smartRef.current?.onRefined?.(); } },
    bindCanvas: (target: HTMLCanvasElement | null) => { canvas.current = target; if (target) schedule(); },
    events: { onKeyDown: keyDown, onPointerDown: down, onPointerMove: move, onPointerUp: (e: PointerEvent<HTMLCanvasElement>) => void finish(e),
      onPointerCancel: (e: PointerEvent<HTMLCanvasElement>) => void finish(e, true), onLostPointerCapture: (e: PointerEvent<HTMLCanvasElement>) => void finish(e, true), onPointerLeave: () => setCursor(null) } };
}
export type SubjectSeparation = ReturnType<typeof useSubjectSeparation>;
export function SubjectSeparationControls({ editor, disabled, hasResult, selectTool, apply, canApply, applyLabel = "수정 사항 적용" }: {
  editor: SubjectSeparation; disabled: boolean; hasResult: boolean; selectTool: () => void;
  apply: () => void; canApply: boolean; applyLabel?: string;
}) {
  return <fieldset className="separation-controls photo-settings" disabled={disabled || !hasResult || !editor.ready || editor.drawing || editor.tracing}>
    <h3>작업 도구</h3><div className="separation-tools" role="group" aria-label="피사체 선택 도구">
      {([["brush", "브러쉬", Brush], ["eraser", "지우개", Eraser], ["lasso", "올가미", Lasso]] as const).map(([id, label, Icon]) =>
        <button key={id} type="button" aria-pressed={editor.mode === id} className={editor.mode === id ? "active" : ""} onClick={() => { editor.setMode(id); selectTool(); }}><Icon size={18} />{label}</button>)}
    </div>
    {editor.mode !== "eraser" && <div className="separation-operation" role="group" aria-label="선택 영역 추가 또는 빼기">
      <button type="button" aria-pressed={editor.selectionMode === "restore"} onClick={() => editor.setSelectionMode("restore")}>{editor.smart ? "피사체 선택" : "영역 추가"}</button>
      <button type="button" aria-pressed={editor.selectionMode === "erase"} onClick={() => editor.setSelectionMode("erase")}>{editor.smart ? "제외할 영역" : "영역 빼기"}</button>
    </div>}
    {editor.mode !== "lasso" && <>
      <label>크기 <output>{editor.size}px</output><input aria-label="선택 도구 크기" type="range" min="1" max="1000" value={editor.size} onChange={e => editor.setSize(Number(e.target.value))} /></label>
    </>}
    {editor.smart && <p className="separation-tool-hint">{editor.mode === "eraser" || editor.selectionMode === "erase" ? "잘못 포함된 부분을 대략 표시하면 AI가 남길 피사체 경계를 다시 찾습니다." : "배경이 함께 들어가도 괜찮습니다. 피사체 중심을 대략 그리면 AI가 실제 경계를 찾습니다."}</p>}
    {editor.mode === "lasso" && <p className="separation-tool-hint">드래그해 둘러싸거나 클릭으로 꼭짓점을 추가하세요. 첫 점 클릭 또는 Enter로 닫기 · Esc 취소</p>}
    <div className="separation-history"><button type="button" aria-label="선택 실행 취소" disabled={!editor.history.cursor && !editor.polygonPoints} onClick={editor.undo}><Undo2 size={17} /></button>
      <button type="button" aria-label="선택 다시 실행" disabled={editor.history.cursor === editor.history.length} onClick={editor.redo}><Redo2 size={17} /></button>
      <button type="button" disabled={!editor.dirty} onClick={editor.reset}><RotateCcw size={15} />선택 초기화</button></div>
    <button type="button" className="separation-clear" onClick={() => { editor.clearSelection(); editor.setMode("brush"); editor.setSelectionMode("restore"); selectTool(); }}>선택 비우고 다시 선택</button>
    <button type="button" className="button primary full subject-edit-apply" disabled={!canApply || !editor.dirty || editor.polygonPoints > 0} onClick={apply}>{applyLabel}</button>
  </fieldset>;
}
export function SubjectSeparationCanvas({ editor, disabled, filter }: { editor: SubjectSeparation; disabled: boolean; filter: string }) {
  return <div className={`separation-board ${disabled ? "separation-disabled" : ""}`}>
    <canvas ref={editor.bindCanvas} {...editor.events} onPointerDown={e => { if (!disabled) editor.events.onPointerDown(e); }} onContextMenu={e => { if (!disabled) e.preventDefault(); }} aria-label="피사체 선택 영역 편집" tabIndex={disabled ? -1 : 0} style={{ opacity: editor.ready ? 1 : 0, filter }} />
    {!editor.ready && <div className="separation-status" role="status">{editor.error || "이미지를 불러오는 중…"}</div>}
    {editor.cursor && !disabled && editor.mode !== "lasso" && <span className={`mask-brush ${editor.cursor.erasing ? "eraser" : "brush"}`} style={{ left: editor.cursor.x, top: editor.cursor.y, width: editor.cursor.diameter, height: editor.cursor.diameter }} />}
    {editor.tracing && <div className="separation-tracing" role="status">{editor.smart ? editor.smartStatus || "피사체 경계를 찾는 중…" : "배경 경계를 따라 선택 중…"}</div>}
  </div>;
}
