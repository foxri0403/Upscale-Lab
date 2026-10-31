import { Check } from "lucide-react";
import type { LayerPreview } from "./layer-preview";
import type { Edit } from "./model";
import { cropPresentation } from "./crop-geometry";

export function LayerPreviewSelector({ value, change, ready, original, background, foregrounds, edit, width, height }: {
  value: LayerPreview; change: (value: LayerPreview) => void; ready: boolean;
  original: string; background: string; foregrounds: string[]; edit: Edit; width: number; height: number;
}) {
  const crop = cropPresentation(Math.max(1, width), Math.max(1, height), edit);
  const ratio = crop.width / crop.height;
  return <section className="studio-layer-selection" aria-label="분리 결과">
    <h3>레이어</h3>
    <div role="group" aria-label="미리보기 레이어">
      {([{ id: "all", name: "전체" }, { id: "subject", name: "피사체" }, { id: "background", name: "배경" }] as const).map(layer =>
        <button type="button" key={layer.id} className={`layer-row layer-choice ${value === layer.id ? "active" : ""}`}
          aria-label={`${layer.name} 레이어 보기`} aria-pressed={value === layer.id}
          disabled={!ready && layer.id !== "all"} onClick={() => change(layer.id)}>
          <span className={`layer-thumb ${layer.id === "subject" ? "checker" : ""}`}>
            <span className="layer-thumb-window" style={{ width: `min(100cqw, calc(100cqh * ${ratio}))`, aspectRatio: ratio }}>
            <span className="layer-thumb-plane" style={crop.plane}>
            {layer.id === "all" && !ready && <img src={original} alt="" />}
            {ready && layer.id !== "subject" && <img src={background} alt="" />}
            {ready && layer.id !== "background" && foregrounds.map(src => <img key={src} src={src} alt="" />)}
            </span>
            </span>
          </span>
          <span>{layer.name}</span>
          {value === layer.id && <Check size={17} aria-hidden="true" />}
        </button>)}
    </div>
    {!ready && <p className="layer-preview-hint">분리가 완료되면 배경과 피사체를 따로 볼 수 있습니다.</p>}
  </section>;
}
