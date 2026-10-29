import { useEffect, useRef, useState, type RefObject } from "react";
import { Expand } from "lucide-react";
import { filterStyle, type Edit, type Picture, type Tool } from "./model";
import { imageFileFormat, imageResolution } from "./image-information";
import { imageFeatures } from "./image-features";
import { visibleLayers, type LayerPreview } from "./layer-preview";
import { CropEditor } from "./CropEditor";
import { pixelCrop } from "./crop-geometry";

export function StudioCanvas({ picture, edit, tool, layered, background, foregrounds, selectedLayer = "all", saveState, unsaved, onDimensions, motion, onEdit, readOnly = false }: {
  picture: Picture; edit: Edit; tool: Tool; layered: boolean;
  background: string; foregrounds: string[];
  selectedLayer?: LayerPreview;
  saveState: string; unsaved: boolean;
  onDimensions: (value: { src: string; width: number; height: number }) => void;
  motion: { active: boolean; stage: RefObject<HTMLDivElement | null>; layers: RefObject<HTMLDivElement | null> };
  onEdit: (value: Edit) => void; readOnly?: boolean;
}) {
  const viewport = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState({ width: 0, height: 0 });
  const [pixels, setPixels] = useState({ src: "", width: 0, height: 0 });
  const [zoom, setZoom] = useState("fit");
  const visible = visibleLayers(selectedLayer);
  const moving = motion.active;
  const dimensionsCallback = useRef(onDimensions);
  dimensionsCallback.current = onDimensions;
  const source = layered && tool === "layers" ? background : picture.src;
  useEffect(() => {
    const element = viewport.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setSize({
      width: entry.contentRect.width, height: entry.contentRect.height,
    }));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  useEffect(() => {
    setZoom("fit");
    const image = new Image();
    image.onload = () => {
      const value = { src: source, width: image.naturalWidth, height: image.naturalHeight };
      setPixels(value);
      dimensionsCallback.current(value);
    };
    image.src = source;
    return () => { image.onload = null; };
  }, [source]);
  const width = pixels.src === source ? pixels.width : picture.width || 1920;
  const height = pixels.src === source ? pixels.height : picture.height || 1080;
  const selected = pixelCrop(width, height, edit);
  const croppedWidth = tool === "crop" ? width : selected.width;
  const croppedHeight = tool === "crop" ? height : selected.height;
  const fitted = Math.min(Math.max(1, size.width - 48) / croppedWidth,
    Math.max(1, size.height - 48) / croppedHeight);
  const scale = zoom === "fit" ? fitted : Number(zoom) / 100;
  const stageWidth = Math.max(1, croppedWidth * scale);
  const stageHeight = Math.max(1, croppedHeight * scale);
  const cropped = edit.crop !== "original";
  const measured = pixels.src === source || !!(picture.width && picture.height);
  return (
    <section className="studio-workspace" aria-label="이미지 미리보기">
      <div className="studio-canvas-viewport" ref={viewport}>
        <div className="studio-ruler studio-ruler-x" aria-hidden="true">
          {Array.from({ length: 11 }, (_, i) => <span key={i}>{Math.round(croppedWidth * i / 10).toLocaleString("en-US")}</span>)}
        </div>
        <div className="studio-ruler studio-ruler-y" aria-hidden="true">
          {Array.from({ length: 7 }, (_, i) => <span key={i}>{Math.round(croppedHeight * i / 6).toLocaleString("en-US")}</span>)}
        </div>
        {tool === "crop" ? <CropEditor picture={layered ? { ...picture, background, foregrounds } : picture} edit={edit} change={onEdit} disabled={readOnly} zoom={zoom} rulers /> : <div className="studio-canvas-center">
          <div className={`canvas-stage ${cropped ? "crop-stage" : ""} ${layered && selectedLayer === "subject" && tool === "layers" ? "subject-only" : ""}`}
            ref={motion.stage} style={{ width: stageWidth, height: stageHeight }}>
            <div className="studio-image-plane" style={{ width: width * scale, height: height * scale, left: -selected.x * scale, top: -selected.y * scale }}>
            {layered && tool === "layers" ? (
              <div className={`live-canvas ${moving ? "motion-active" : ""}`} ref={motion.layers} style={{ filter: filterStyle(edit.filter) }}>
                {visible.background && <img src={background} alt="배경 레이어" />}
                {visible.subject && foregrounds.map((src, i) => <img key={src}
                  className="subject-layer" src={src} alt={`피사체 레이어 ${i + 1}`} />)}
              </div>
            ) : <img src={picture.src} alt={picture.name} style={{ filter: filterStyle(edit.filter) }} />}
            </div>
          </div>
        </div>}
      </div>
      <div className="studio-canvas-status">
        <div className="studio-file-meta"><span>{measured ? imageResolution(width, height) : "해상도 확인 중…"}</span>
          <span>{imageFileFormat(picture.src, picture.contentType)}</span>
          <span className={`save-state ${unsaved ? "unsaved" : ""}`} role="status"><i />{saveState}</span>
          {imageFeatures(picture).map(feature => <span key={feature} className="studio-feature-badge">{feature}</span>)}
        </div>
        <div className="studio-zoom">
          <button aria-label="화면 맞춤" title="화면 맞춤" aria-pressed={zoom === "fit"} onClick={() => setZoom("fit")}><Expand size={16} /> 화면 맞춤</button>
          <select aria-label="미리보기 확대 비율" value={zoom === "fit" ? "" : zoom} onChange={(event) => setZoom(event.target.value)}>
            <option value="" disabled hidden>배율</option>
            {Array.from({ length: 8 }, (_, index) => (index + 1) * 25).map(value => <option key={value} value={value}>{value}%</option>)}
          </select>
        </div>
      </div>
    </section>
  );
}
