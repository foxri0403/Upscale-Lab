import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { filterStyle, type Edit, type Picture } from "./model";
import { cropRegion, moveCrop, resizeCrop, type CropRegion } from "./crop-geometry";
import "./crop.css";

const handles = { nw: "왼쪽 위", n: "위쪽", ne: "오른쪽 위", e: "오른쪽", se: "오른쪽 아래", s: "아래쪽", sw: "왼쪽 아래", w: "왼쪽" };
export function CropEditor({ picture, edit, change, disabled = false, zoom = "fit", rulers = false }: {
  picture: Picture; edit: Edit; change: (edit: Edit) => void; disabled?: boolean; zoom?: string; rulers?: boolean;
}) {
  const root = useRef<HTMLDivElement>(null), stage = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 320, height: 240 }), [pixels, setPixels] = useState({ src: "", width: 0, height: 0 });
  const drag = useRef<{ id: number; x: number; y: number; width: number; height: number; region: CropRegion; cancel: CropRegion; handle: string } | null>(null);
  const source = picture.background || picture.src;
  useEffect(() => { const observer = new ResizeObserver(([entry]) => setSize({ width: entry.contentRect.width, height: entry.contentRect.height })); observer.observe(root.current!); return () => observer.disconnect(); }, []);
  useEffect(() => { const img = new Image(); img.onload = () => setPixels({ src: source, width: img.naturalWidth, height: img.naturalHeight }); img.src = source; return () => { img.onload = null; drag.current = null; }; }, [source]);
  const width = pixels.src === source ? pixels.width : picture.width || 1920, height = pixels.src === source ? pixels.height : picture.height || 1080;
  const region = cropRegion(width, height, edit), active = edit.crop !== "original", editable = active && !disabled;
  const fitted = Math.max(.0001, Math.min((size.width - 48) / width, (size.height - 48) / height));
  const scale = zoom === "fit" ? fitted : Math.max(.05, Number(zoom) / 100 || fitted), stageWidth = width * scale, stageHeight = height * scale;
  function emit(next: CropRegion) { change({ ...edit, cropRegion: next }); }
  function start(event: ReactPointerEvent<HTMLDivElement>) {
    if (!editable || !event.isPrimary || event.button !== 0) return;
    event.preventDefault(); const bounds = stage.current!.getBoundingClientRect();
    const target = event.target as HTMLElement, handle = target.closest<HTMLElement>("[data-crop-handle]")?.dataset.cropHandle || "move";
    let selected = region;
    if (!target.closest(".crop-selection")) selected = moveCrop(region, (event.clientX - bounds.left) / bounds.width - region.x - region.width / 2, (event.clientY - bounds.top) / bounds.height - region.y - region.height / 2);
    drag.current = { id: event.pointerId, x: event.clientX, y: event.clientY, width: bounds.width, height: bounds.height, region: selected, cancel: region, handle };
    event.currentTarget.setPointerCapture(event.pointerId); stage.current?.querySelector<HTMLElement>(".crop-selection")?.focus({ preventScroll: true }); emit(selected);
  }
  function move(event: ReactPointerEvent<HTMLDivElement>) {
    const value = drag.current; if (!value || value.id !== event.pointerId || !editable) return;
    const dx = (event.clientX - value.x) / value.width, dy = (event.clientY - value.y) / value.height;
    emit(value.handle === "move" ? moveCrop(value.region, dx, dy) : resizeCrop(value.region, value.handle, dx, dy, width, height));
  }
  function finish(event: ReactPointerEvent<HTMLDivElement>, cancelled = false) {
    if (drag.current?.id !== event.pointerId) return; if (cancelled) emit(drag.current.cancel); drag.current = null;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  }
  function key(event: ReactKeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape" && drag.current) { event.preventDefault(); emit(drag.current.cancel); drag.current = null; return; }
    if (!editable || !["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
    event.preventDefault(); const step = event.shiftKey ? 10 : 1, dx = (event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0) / width, dy = (event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0) / height;
    const handle = (event.target as HTMLElement).dataset.cropHandle;
    emit(handle ? resizeCrop(region, handle, dx, dy, width, height) : moveCrop(region, dx, dy));
  }
  return <div ref={root} className={`crop-editor ${rulers ? "crop-editor-rulers" : ""}`} aria-label="원본 이미지 자르기">
    <div className="crop-editor-surface" style={{ width: Math.max(size.width, stageWidth + 48), height: Math.max(size.height, stageHeight + 48) }}>
      <div ref={stage} className={`crop-image-stage ${editable ? "crop-editable" : ""}`} style={{ width: stageWidth, height: stageHeight }}
        onPointerDown={start} onPointerMove={move} onPointerUp={event => finish(event)} onPointerCancel={event => finish(event, true)} onKeyDown={key}>
        <div className="crop-source-image" style={{ filter: filterStyle(edit.filter) }}><img src={source} alt={picture.name} draggable={false} />
          {picture.background && edit.foreground && picture.foregrounds?.map(src => <img key={src} src={src} alt="" draggable={false} />)}</div>
        {active && <>
          <div className="crop-excluded" aria-hidden="true" style={{ left: 0, top: 0, width: "100%", height: `${region.y * 100}%` }} />
          <div className="crop-excluded" aria-hidden="true" style={{ left: 0, top: `${(region.y + region.height) * 100}%`, width: "100%", bottom: 0 }} />
          <div className="crop-excluded" aria-hidden="true" style={{ left: 0, top: `${region.y * 100}%`, width: `${region.x * 100}%`, height: `${region.height * 100}%` }} />
          <div className="crop-excluded" aria-hidden="true" style={{ left: `${(region.x + region.width) * 100}%`, top: `${region.y * 100}%`, right: 0, height: `${region.height * 100}%` }} />
          <div className="crop-selection" role="group" aria-label="자르기 선택 영역" aria-disabled={disabled} tabIndex={editable ? 0 : -1}
            style={{ left: `${region.x * 100}%`, top: `${region.y * 100}%`, width: `${region.width * 100}%`, height: `${region.height * 100}%` }}>
            <div className="crop-guides" aria-hidden="true"><i /><i /><i /><i /></div>
            {edit.crop === "custom" && !disabled && Object.entries(handles).map(([handle, name]) => <button key={handle} type="button" data-crop-handle={handle} className={`crop-handle crop-handle-${handle}`} aria-label={`${name} 크기 조절`} />)}
          </div>
        </>}
      </div>
    </div>
  </div>;
}
