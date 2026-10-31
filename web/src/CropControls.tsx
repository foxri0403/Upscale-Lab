import { Check, Minus, Plus, RotateCcw } from "lucide-react";
import type { Edit } from "./model";
import { cropPresets, cropRegion, fitCrop, pixelCrop, zoomCrop } from "./crop-geometry";
import "./crop.css";

export function CropControls({ edit, width, height, change, saving, error }: {
  edit: Edit; width: number; height: number; change: (edit: Edit) => void; saving: boolean; error: string;
}) {
  const ready = width > 0 && height > 0, region = cropRegion(width, height, edit);
  const pixels = ready ? pixelCrop(width, height, edit) : null;
  const center = { x: region.x + region.width / 2, y: region.y + region.height / 2 };
  function select(crop: string) {
    if (crop === "custom") {
      if (edit.crop !== "custom") change({ ...edit, crop, cropRegion: { x: 0, y: 0, width: 1, height: 1 } });
      return;
    }
    if (crop === "original") change({ ...edit, crop, cropRegion: undefined });
    else change({ ...edit, crop, cropRegion: fitCrop(width, height, ({wide:16/9,portrait:9/16,square:1} as Record<string, number>)[crop], center) });
  }
  function dimension(axis: "width" | "height", value: number) {
    if (!Number.isFinite(value) || value < 1) return;
    const w = axis === "width" ? Math.min(width, value) / width : region.width;
    const h = axis === "height" ? Math.min(height, value) / height : region.height;
    change({ ...edit, cropRegion: { x: Math.max(0, Math.min(1 - w, center.x - w / 2)), y: Math.max(0, Math.min(1 - h, center.y - h / 2)), width: w, height: h } });
  }
  return <>
    <h3>이미지 비율</h3>
    <div className="crop-choices">{cropPresets.map(([value, label]) => <button type="button" key={value} aria-pressed={edit.crop === value}
      disabled={!ready} className={edit.crop === value ? "active" : ""} onClick={() => select(value)}><span>{label}</span>{edit.crop === value && <Check size={16} />}</button>)}</div>
    {edit.crop === "custom" && <div className="crop-dimensions">
      <label><span>너비</span><input type="number" aria-label="자르기 너비" disabled={!ready} min={1} max={width} value={pixels?.width ?? ""} onChange={event => dimension("width", event.target.valueAsNumber)} /></label>
      <label><span>높이</span><input type="number" aria-label="자르기 높이" disabled={!ready} min={1} max={height} value={pixels?.height ?? ""} onChange={event => dimension("height", event.target.valueAsNumber)} /></label>
    </div>}
    {edit.crop !== "original" && <div className="crop-area-controls">
      <p>표시 영역 {pixels ? `${pixels.width} × ${pixels.height} px` : "확인 중"}</p>
      <div><button type="button" className="button secondary" disabled={!ready} aria-label="자르기 확대" onClick={() => change({ ...edit, cropRegion: zoomCrop(region, 1.2, width, height) })}><Plus size={17} /></button>
        <button type="button" className="button secondary" disabled={!ready} aria-label="자르기 축소" onClick={() => change({ ...edit, cropRegion: zoomCrop(region, 1 / 1.2, width, height) })}><Minus size={17} /></button>
        <button type="button" className="button secondary" disabled={!ready} onClick={() => change({ ...edit, cropRegion: fitCrop(width, height, region.width * width / (region.height * height)) })}><RotateCcw size={15} />영역 초기화</button></div>
    </div>}
    <p className={`crop-save-state ${error ? "mask-error" : ""}`} role="status">{error
      ? "자동 저장에 실패했습니다. 다시 조정하거나 보관함에 저장해 주세요."
      : saving ? "자르기 자동 저장 중…" : "자르기 변경 사항이 자동으로 저장됩니다."}</p>
  </>;
}
