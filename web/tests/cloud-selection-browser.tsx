// Real hook and Canvas/PNG regression fixture; no API requests or GPU jobs.
import { useRef, useState, type PointerEvent } from "react";
import { createRoot } from "react-dom/client";
import { flushSync } from "react-dom";
import { SubjectSeparationCanvas, SubjectSeparationControls, useSubjectSeparation } from "../src/SubjectSeparation";
import { loadImage, type Picture } from "../src/model";
import { unspecifiedTag } from "../src/image-tags";

const width = 3200, height = 2000;
function board() { const c = document.createElement("canvas"); c.width = width; c.height = height; return c; }
const source = board(), sc = source.getContext("2d")!;
sc.fillStyle = "#cbd5e1"; sc.fillRect(0, 0, width, height);
sc.fillStyle = "#402515"; sc.fillRect(700, 300, 1800, 1400);
const subject = board(), fc = subject.getContext("2d")!;
fc.fillStyle = "#402515"; fc.fillRect(900, 500, 600, 700);
const picture: Picture = { id: "cloud-guide-regression", name: "선택 도구 검사", src: source.toDataURL(),
  foregrounds: [subject.toDataURL()], subjectLayerId: "baseline", width, height, genre: unspecifiedTag, created: "2026-10-31T00:00:00Z" };

function Fixture() {
  const editor = useSubjectSeparation(picture, true, false, undefined, true), current = useRef(editor); current.current = editor;
  const [report, setReport] = useState(""), [running, setRunning] = useState(false);
  const expected = useRef(board());
  function draw(points: Array<[number, number]>, tool: "brush" | "eraser" | "lasso") {
    flushSync(() => current.current.setMode(tool));
    const canvas = document.querySelector("canvas[aria-label='피사체 선택 영역 편집']") as HTMLCanvasElement;
    const bounds = canvas.getBoundingClientRect();
    // Feed source-pixel samples through the real handlers; capture is tested separately by manual canvas input.
    const target = { getBoundingClientRect: () => bounds, focus: () => {}, setPointerCapture: () => {},
      hasPointerCapture: () => false, releasePointerCapture: () => {} };
    const event = ([x, y]: [number, number]) => ({ currentTarget: target, pointerId: 1, isPrimary: true, button: 0,
      clientX: bounds.left + x / width * bounds.width, clientY: bounds.top + y / height * bounds.height,
      preventDefault() {}, nativeEvent: { getCoalescedEvents: () => [] } }) as unknown as PointerEvent<HTMLCanvasElement>;
    flushSync(() => current.current.events.onPointerDown(event(points[0])));
    for (const point of points.slice(1)) current.current.events.onPointerMove(event(point));
    const start = performance.now();
    flushSync(() => current.current.events.onPointerUp(event(points.at(-1)!)));
    const commitMs = performance.now() - start;
    const c = expected.current.getContext("2d")!;
    c.fillStyle = c.strokeStyle = tool === "eraser" ? "#00ff00" : "#ff0000";
    c.beginPath(); c.moveTo(...points[0]); for (const point of points.slice(1)) c.lineTo(...point);
    if (tool === "lasso") { c.closePath(); c.fill("evenodd"); }
    else { c.lineWidth = 150; c.lineCap = c.lineJoin = "round"; c.stroke(); c.beginPath(); c.arc(...points[0], 75, 0, Math.PI * 2); c.fill(); }
    return commitMs;
  }
  async function pixels() {
    const start = performance.now(), blob = await current.current.exportSelection(), url = URL.createObjectURL(blob);
    try {
      const image = await loadImage(url), output = board(), c = output.getContext("2d")!; c.drawImage(image, 0, 0);
      const actual = c.getImageData(0, 0, width, height).data, reference = expected.current.getContext("2d")!.getImageData(0, 0, width, height).data;
      let mismatches = 0, invalidChannels = 0, added = 0, erased = 0;
      for (let i = 0; i < actual.length; i += 4) {
        // Independent Canvas rasterization can differ by one at antialiased path edges.
        if (Math.abs(actual[i] - reference[i]) > 1 || Math.abs(actual[i + 1] - reference[i + 1]) > 1) mismatches++;
        if (actual[i + 2] !== 0 || actual[i + 3] !== 255) invalidChannels++;
        if (actual[i]) added++; if (actual[i + 1]) erased++;
      }
      return { mismatches, invalidChannels, added, erased, width: image.naturalWidth, height: image.naturalHeight, exportAndCheckMs: performance.now() - start };
    } finally { URL.revokeObjectURL(url); }
  }
  async function run() {
    setRunning(true); setReport("");
    try {
      flushSync(() => current.current.reset());
      const c = expected.current.getContext("2d")!; c.fillStyle = "black"; c.fillRect(0, 0, width, height);
      // Dense freehand rectangle, deliberately unrelated to the high-contrast image edges.
      const path: Array<[number, number]> = [];
      for (let x = 400; x <= 2800; x += 10) path.push([x, 200]);
      for (let y = 210; y <= 1800; y += 10) path.push([2800, y]);
      for (let x = 2790; x >= 400; x -= 10) path.push([x, 1800]);
      for (let y = 1790; y >= 200; y -= 10) path.push([400, y]);
      const lassoMs = draw(path, "lasso"), lasso = await pixels();
      if (lasso.mismatches || lasso.invalidChannels || current.current.tracing) throw new Error("그린 올가미와 내보낸 영역이 다릅니다.");
      flushSync(() => current.current.undo());
      let undoEmpty = false; try { await current.current.exportSelection(); } catch { undoEmpty = true; }
      if (!undoEmpty) throw new Error("실행 취소 후 선택이 남았습니다.");
      flushSync(() => current.current.redo());
      const redo = await pixels(); if (redo.mismatches) throw new Error("다시 실행이 선택을 변경했습니다.");
      const eraseMs = draw([[800, 900], [2400, 900]], "eraser");
      const brushMs = draw([[1600, 600], [1600, 1200]], "brush");
      const mixed = await pixels(); if (mixed.mismatches || mixed.invalidChannels || !mixed.erased) throw new Error("브러쉬·지우개 채널이 다릅니다.");
      flushSync(() => current.current.reset());
      const repeated: number[] = [];
      for (let i = 0; i < 50; i++) repeated.push(draw([[500, 300 + i * 25], [2700, 300 + i * 25]], "brush"));
      const sorted = [...repeated].sort((a, b) => a - b);
      setReport(JSON.stringify({ passed: true, lassoPoints: path.length, lassoMs, brushMs, eraseMs, lasso, redo, mixed, undoEmpty,
        repeatedStrokes: repeated.length, firstTenMaxMs: Math.max(...repeated.slice(0, 10)), lastTenMaxMs: Math.max(...repeated.slice(-10)), p95CommitMs: sorted[Math.floor(sorted.length * .95)],
        hintCount: document.querySelectorAll(".separation-tool-hint").length }, null, 2));
    } catch (error) { setReport(JSON.stringify({ passed: false, error: (error as Error).message })); }
    finally { setRunning(false); }
  }
  return <main style={{ fontFamily: "sans-serif", padding: 16 }}>
    <h1>클라우드 선택 도구 회귀 검사</h1>
    <button disabled={!editor.ready || running} onClick={() => void run()}>전체 회귀 검사</button>
    <p role="status">{editor.ready ? "준비 완료" : "로딩 중"} · 이력 {editor.history.cursor}/{editor.history.length} · {editor.tracing ? "추적 중" : "즉시 표시"}</p>
    <div style={{ display: "flex", gap: 24 }}><div style={{ position: "relative", width: 640, height: 400 }}><SubjectSeparationCanvas editor={editor} disabled={false} filter="none" /></div>
      <div style={{ width: 300 }}><SubjectSeparationControls editor={editor} disabled={false} hasResult selectTool={() => {}} /></div></div>
    <pre aria-label="회귀 검사 결과">{report}</pre>
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
