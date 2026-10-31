// Browser fixture: the real editing hook and Canvas export, without API calls or GPU jobs.
import { useState } from "react";
import { createRoot } from "react-dom/client";
import { SubjectSeparationCanvas, SubjectSeparationControls, useSubjectSeparation } from "../src/SubjectSeparation";
import { loadImage, type Picture } from "../src/model";
import { unspecifiedTag } from "../src/image-tags";

const width = 3200, height = 2000;
function canvas() {
  const result = document.createElement("canvas"); result.width = width; result.height = height; return result;
}
const source = canvas(), sourceContext = source.getContext("2d")!;
sourceContext.fillStyle = "#d5dce8"; sourceContext.fillRect(0, 0, width, height);
sourceContext.fillStyle = "#a57948"; sourceContext.fillRect(700, 300, 1800, 1400);
const subject = canvas(), subjectContext = subject.getContext("2d")!, pixels = sourceContext.getImageData(0, 0, width, height);
const baseline = new Uint8Array(width * height);
for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
  const i = y * width + x;
  baseline[i] = x >= 700 && x < 2500 && y >= 300 && y < 1700 ? 1 + (x * 17 + y * 31) % 255 : 0;
  pixels.data[i * 4 + 3] = baseline[i];
}
subjectContext.putImageData(pixels, 0, 0);
const original: Picture = { id: "partial-selection-fixture", name: "3200 × 2000 알파 회귀 검사", src: source.toDataURL(),
  width, height, genre: unspecifiedTag, created: "2026-10-31T00:00:00Z" };
const separated: Picture = { ...original, background: original.src, foregrounds: [subject.toDataURL()], subjectLayerId: "automatic-result" };
function Fixture() {
  const [picture, setPicture] = useState(original), [result, setResult] = useState("");
  const [busy, setBusy] = useState(false), [layer, setLayer] = useState(0);
  const hasResult = !!picture.foregrounds?.length;
  const editor = useSubjectSeparation(picture, true, !hasResult || busy);
  async function inspect(commit = false) {
    setBusy(true);
    let url: string | undefined;
    try {
      url = URL.createObjectURL(await editor.exportMask());
      const image = await loadImage(url), output = canvas(), c = output.getContext("2d")!;
      c.drawImage(image, 0, 0); const data = c.getImageData(0, 0, width, height).data;
      let added = 0, removed = 0, changedAutomaticPixels = 0, nonOpaque = 0;
      let left = width, right = -1, top = height, bottom = -1;
      for (let i = 0; i < baseline.length; i++) {
        const value = data[i * 4];
        nonOpaque += data[i * 4 + 3] !== 255 ? 1 : 0;
        if (value === baseline[i]) continue;
        added += value > baseline[i] ? 1 : 0; removed += value < baseline[i] ? 1 : 0;
        changedAutomaticPixels += baseline[i] > 0 ? 1 : 0;
        const x = i % width, y = Math.floor(i / width);
        left = Math.min(left, x); right = Math.max(right, x); top = Math.min(top, y); bottom = Math.max(bottom, y);
      }
      setResult(JSON.stringify({ width: image.naturalWidth, height: image.naturalHeight, added, removed, changedAutomaticPixels,
        nonOpaque, changedBounds: right < 0 ? null : { left, top, right, bottom } }, null, 2));
      if (commit) {
        const rgba = sourceContext.getImageData(0, 0, width, height);
        for (let i = 0; i < baseline.length; i++) rgba.data[i * 4 + 3] = data[i * 4];
        c.putImageData(rgba, 0, 0);
        setPicture({ ...separated, foregrounds: [output.toDataURL()], subjectLayerId: `applied-${layer + 1}` });
        setLayer(layer + 1); editor.markApplied();
      }
    } catch (error) { setResult((error as Error).message); }
    finally { if (url) URL.revokeObjectURL(url); setBusy(false); }
  }
  return <main style={{ fontFamily: "sans-serif", padding: 16 }}>
    <h1>자동 분리 후 부분 수정</h1>
    <p>고정된 3200 × 2000 원본 알파를 사용합니다. 네트워크 추론을 실행하지 않습니다.</p>
    <button disabled={busy} onClick={() => { setPicture(original); editor.reload(); }}>분리 전 이미지</button>{" "}
    <button disabled={busy} onClick={() => { setPicture(separated); editor.reload(true); }}>자동 결과 불러오기</button>{" "}
    <button disabled={!editor.ready || busy} onClick={() => void inspect()}>내보낸 픽셀 검사</button>{" "}
    <button disabled={!editor.ready || busy || !editor.dirty} onClick={() => void inspect(true)}>현재 선택을 새 결과로 확정</button>
    <p role="status">{editor.ready ? "준비 완료" : "로딩 중"} · {editor.dirty ? "미적용 수정 있음" : "적용 완료"} · 이력 {editor.history.cursor}/{editor.history.length}</p>
    <div style={{ display: "flex", gap: 24, alignItems: "start" }}>
      <div style={{ position: "relative", width: 640, height: 400, flexShrink: 0 }}>
        <SubjectSeparationCanvas editor={editor} disabled={!hasResult || busy} filter="none" />
      </div>
      <div style={{ width: 340 }}>
        <SubjectSeparationControls editor={editor} disabled={busy} hasResult={hasResult} apply={() => void inspect()}
          selectTool={() => editor.setView("overlay")} />
        <pre aria-label="내보낸 픽셀 검사 결과">{result}</pre>
      </div>
    </div>
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
