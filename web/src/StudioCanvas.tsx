import { useEffect, useRef, useState, type RefObject } from "react";
import { Expand, Hand } from "lucide-react";
import { filterStyle, type Edit, type Picture, type Tool } from "./model";
import { imageFileFormat, imageResolution } from "./image-information";
import { imageFeatures } from "./image-features";
import { visibleLayers, type LayerPreview } from "./layer-preview";
import { CropEditor } from "./CropEditor";
import { cropPresentation } from "./crop-geometry";
import { previewStageStyle, PreviewPaletteButton } from "./StudioPreview";
import { SubjectSeparationCanvas, type SubjectSeparation } from "./SubjectSeparation";
import { usePreviewPan } from "./use-preview-pan";

export function StudioCanvas({ picture, edit, tool, layered, background, foregrounds, selectedLayer = "all", saveState, unsaved, onDimensions, motion, onEdit, readOnly = false, separation, published }: {
  picture: Picture; edit: Edit; tool: Tool; layered: boolean;
  background: string; foregrounds: string[];
  selectedLayer?: LayerPreview;
  saveState: string; unsaved: boolean;
  onDimensions: (value: { src: string; width: number; height: number }) => void;
  motion: { active: boolean; stage: RefObject<HTMLDivElement | null>; layers: RefObject<HTMLDivElement | null> };
  onEdit: (value: Edit) => void; readOnly?: boolean;
  separation: SubjectSeparation;
  published: boolean;
}) {
  const [pixels, setPixels] = useState({ src: "", width: 0, height: 0 });
  const [zoom, setZoom] = useState("fit");
  const [darkPalette, setDarkPalette] = useState(false);
  const [hand, setHand] = useState(false);
  const inline = tool === "layers" && !published && selectedLayer !== "background" && separation.view !== "motion";
  const pan = usePreviewPan(tool === "layers" && zoom !== "fit", !inline || hand);
  useEffect(() => { if (zoom === "fit" || tool !== "layers") setHand(false); }, [zoom, tool]);
  const palette = { dark: darkPalette, toggle: () => setDarkPalette(value => !value) };
  const visible = visibleLayers(selectedLayer);
  const moving = motion.active;
  const dimensionsCallback = useRef(onDimensions);
  dimensionsCallback.current = onDimensions;
  const source = layered && !inline && (tool === "layers" || tool === "crop") ? background : picture.src;
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
  const selected = cropPresentation(width, height, edit);
  const croppedWidth = tool === "crop" ? width : selected.width;
  const croppedHeight = tool === "crop" ? height : selected.height;
  const cropped = edit.crop !== "original";
  const measured = pixels.src === source || !!(picture.width && picture.height);
  return (
    <section className="studio-workspace" aria-label="이미지 미리보기">
      <div className={`studio-canvas-viewport ${darkPalette ? "studio-palette-dark" : ""}`}>
        <div className="studio-ruler studio-ruler-x" aria-hidden="true">
          {Array.from({ length: 11 }, (_, i) => <span key={i}>{Math.round(croppedWidth * i / 10).toLocaleString("en-US")}</span>)}
        </div>
        <div className="studio-ruler studio-ruler-y" aria-hidden="true">
          {Array.from({ length: 7 }, (_, i) => <span key={i}>{Math.round(croppedHeight * i / 6).toLocaleString("en-US")}</span>)}
        </div>
        {tool === "crop" ? <CropEditor picture={layered ? { ...picture, background, foregrounds } : picture} edit={edit} change={onEdit} disabled={readOnly} zoom={zoom} palette={palette} rulers /> : <div ref={pan.root} {...pan.events} className={`studio-canvas-center ${pan.canPan ? "preview-can-pan" : ""} ${pan.dragging ? "preview-panning" : ""}`}>
          <div className="studio-pan-surface" style={{ width: zoom === "fit" ? "100%" : croppedWidth * Number(zoom) / 100 + 44, height: zoom === "fit" ? "100%" : croppedHeight * Number(zoom) / 100 + 44 }}>
          <PreviewPaletteButton {...palette} />
          <div className={`canvas-stage studio-preview-stage ${cropped ? "crop-stage" : ""} ${layered && selectedLayer === "subject" && tool === "layers" ? "subject-only" : ""}`}
            ref={motion.stage} style={previewStageStyle(croppedWidth, croppedHeight, zoom)}>
            <div className="studio-image-plane" style={selected.plane}>
            {inline ? <SubjectSeparationCanvas editor={separation} disabled={readOnly || !layered || hand || pan.canPan || separation.tracing} filter={filterStyle(edit.filter)} /> : layered && tool === "layers" ? (
              <div className={`live-canvas ${moving ? "motion-active" : ""}`} ref={motion.layers} style={{ filter: filterStyle(edit.filter) }}>
                {visible.background && <img src={background} alt="배경 레이어" />}
                {visible.subject && foregrounds.map((src, i) => <img key={src}
                  className="subject-layer" src={src} alt={`피사체 레이어 ${i + 1}`} />)}
              </div>
            ) : <img src={picture.src} alt={picture.name} style={{ filter: filterStyle(edit.filter) }} />}
            </div>
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
          {tool === "layers" && <button aria-label="이미지 이동" title="드래그로 이동 · 스페이스 키 또는 마우스 휠 버튼으로도 이동" aria-pressed={hand} disabled={zoom === "fit" || separation.drawing} onClick={() => setHand(value => !value)}><Hand size={16} /><span>이동</span></button>}
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
