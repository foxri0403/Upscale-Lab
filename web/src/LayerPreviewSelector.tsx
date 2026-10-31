import { Check } from "lucide-react";
import type { LayerPreview } from "./layer-preview";
import type { ApiLayer } from "./api";

export function LayerPreviewSelector({ value, change, ready, original, background, foregrounds, layers }: {
  value: LayerPreview; change: (value: LayerPreview) => void; ready: boolean;
  original: string; background: string; foregrounds: string[]; layers?: ApiLayer[];
}) {
  const ordered = layers?.slice().sort((a, b) => a.layerOrder - b.layerOrder);
  const hasBackground = ordered ? ordered.some(layer => layer.layerType === "Background") : !!background;
  return <section className="studio-layer-selection" aria-label="분리 결과">
    <h3>레이어</h3>
    <div role="group" aria-label="미리보기 레이어">
      {([{ id: "all", name: "전체" }, { id: "subject", name: hasBackground ? "피사체" : "분리 파츠" }, { id: "background", name: "배경" }] as const).filter(layer => layer.id !== "background" || hasBackground).map(layer =>
        <button type="button" key={layer.id} className={`layer-row layer-choice ${value === layer.id ? "active" : ""}`}
          aria-label={`${layer.name} 레이어 보기`} aria-pressed={value === layer.id}
          disabled={!ready && layer.id !== "all"} onClick={() => change(layer.id)}>
          <span className={`layer-thumb ${layer.id === "subject" ? "checker" : ""}`}>
            {layer.id === "all" && !ready && <img src={original} alt="" />}
            {ready && ordered?.length ? ordered.filter(item => layer.id === "all" ||
              (layer.id === "background" ? item.layerType === "Background" : item.layerType !== "Background"))
              .map(item => <img key={item.id} src={item.imageUrl} alt="" />) : <>
              {ready && layer.id !== "subject" && !!background && <img src={background} alt="" />}
              {ready && layer.id !== "background" && foregrounds.map(src => <img key={src} src={src} alt="" />)}
            </>}
          </span>
          <span>{layer.name}</span>
          {value === layer.id && <Check size={17} aria-hidden="true" />}
        </button>)}
    </div>
    {!ready && <p className="layer-preview-hint">분리가 완료되면 배경과 피사체를 따로 볼 수 있습니다.</p>}
  </section>;
}
