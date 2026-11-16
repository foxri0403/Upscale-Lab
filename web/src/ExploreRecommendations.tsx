import { useCallback, useEffect, useId, useRef, useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import type { Picture } from "./model";
import { PictureView } from "./ui";
import { hasSeeThroughRigLayers } from "./character-processing";
import { normalizeRig, rigPart } from "./character-rig";
import { defaultDemoInput } from "./demo-input";
import { motionStrength } from "./editor-defaults";
import { useCharacterRig } from "./use-character-rig";
import { useMediaQuery } from "./use-media-query";
import { useGyroPermission, useParallax } from "./use-parallax";

type MotionKind = "scene" | "character";
type PreviewInput = { gyro: boolean; gyroReady: boolean; reduced: boolean; kind?: MotionKind };

function motionKind(picture: Picture): MotionKind | undefined {
  if (!picture.foregrounds?.length || picture.edit?.foreground === false) return;
  const layers = picture.layers?.length ? picture.layers : picture.wallpaperParts?.map(part => ({ layerType: part.role }));
  if (picture.background) return hasSeeThroughRigLayers(layers) || picture.features?.includes("Live 2D") ? "character" : "scene";
  if (picture.foregrounds.length >= 2 && (picture.assetId || hasSeeThroughRigLayers(layers))) return "character";
}

function RecommendationMotion({ picture, kind, gyro, gyroReady, reduced, loaded }: {
  picture: Picture; kind: MotionKind; gyro: boolean; gyroReady: boolean; reduced: boolean;
  loaded: (image: HTMLImageElement) => void;
}) {
  const [inView, setInView] = useState(false);
  const [hovered, setHovered] = useState(false);
  const endHover = useCallback(() => setHovered(false), []);
  const sourceKey = JSON.stringify([picture.id, picture.background, picture.foregrounds]);
  const motion = useParallax(!reduced && inView && (gyro || hovered), motionStrength(picture.edit), !gyro,
    gyro, sourceKey, undefined, false, { gyroReady, pointerPadding: 24, onPointerSettled: endHover });
  const settings = { ...normalizeRig(picture.edit?.rig), enabled: true };
  const rig = useCharacterRig(kind === "character" && motion.active, settings, sourceKey, motion.position);

  useEffect(() => {
    const stage = motion.stage.current;
    if (!stage) return;
    const visibleRatio = 0.01;
    const observer = new IntersectionObserver(([entry]) => {
      const visible = entry.isIntersecting && entry.intersectionRatio >= visibleRatio;
      setInView(visible);
      if (!visible) endHover();
    }, { threshold: visibleRatio });
    observer.observe(stage);
    return () => observer.disconnect();
  }, [motion.stage, endHover]);

  const partForSource = kind === "character" ? (source: string) => {
    const layer = picture.layers?.find(item => item.imageUrl === source);
    const file = picture.wallpaperParts?.find(item => item.source === source);
    return rigPart(layer?.layerType ?? file?.role ?? "", layer?.id ?? file?.layerId ?? undefined, settings.partAssignments);
  } : undefined;

  return <div className="recommendation-motion" ref={element => { motion.stage.current = element; rig.stage.current = element; }}
    onPointerEnter={event => { if (!gyro && !reduced && event.pointerType === "mouse") setHovered(true); }}>
    <div className={`recommendation-motion${motion.active ? " recommendation-motion-active" : ""}${!picture.background ? " recommendation-motion-character-only" : ""}`}
      ref={element => { motion.layers.current = element; rig.layers.current = element; }}>
      <PictureView picture={picture} loaded={loaded} partForSource={partForSource} />
    </div>
  </div>;
}

function Recommendation({ picture, selected, toggleDetails, input }: {
  picture: Picture; selected: boolean; toggleDetails: (picture: Picture) => void; input: PreviewInput;
}) {
  const [measured, setMeasured] = useState<{ source: string; ratio: number }>();
  const source = picture.background || picture.src;
  const ratio = (picture.width ?? 0) / (picture.height ?? 0);
  const hasRatio = Number.isFinite(ratio) && ratio > 0;
  const aspectRatio = hasRatio ? ratio : measured?.source === source ? measured.ratio : 16 / 9;
  function measure(image: HTMLImageElement) {
    if (!hasRatio && image.naturalWidth > 0 && image.naturalHeight > 0)
      setMeasured({ source, ratio: image.naturalWidth / image.naturalHeight });
  }
  return <button type="button" className="explore-recommendation" style={{ aspectRatio }}
    onClick={() => toggleDetails(picture)} aria-label={`${picture.name} 상세 보기`}
    aria-pressed={selected} aria-controls={selected ? "explore-details" : undefined}>
    {input.kind ? <RecommendationMotion picture={picture} kind={input.kind} gyro={input.gyro} gyroReady={input.gyroReady}
      reduced={input.reduced} loaded={measure} /> : <PictureView picture={picture} loaded={measure} />}
    <span className="explore-recommendation-title">{picture.name}</span>
  </button>;
}

export function ExploreRecommendations({ pictures, selectedId, toggleDetails, ranked }: {
  pictures: Picture[];
  selectedId?: string;
  toggleDetails: (picture: Picture) => void;
  ranked: boolean;
}) {
  const id = useId();
  const rail = useRef<HTMLDivElement>(null);
  const [edges, setEdges] = useState({ start: true, end: true });
  const coarse = useMediaQuery("(pointer: coarse)");
  const fine = useMediaQuery("(any-pointer: fine)");
  const reduced = useMediaQuery("(prefers-reduced-motion: reduce)");
  const gyro = defaultDemoInput(coarse, fine, navigator.maxTouchPoints) === "gyro";
  const { gyroReady, gyroPending, requestGyro, gyroSupported } = useGyroPermission(gyro && !reduced);
  const [sensorError, setSensorError] = useState("");
  const cards = pictures.map(picture => ({ picture, kind: motionKind(picture) }));
  const order = JSON.stringify(pictures.map(picture => picture.id));

  useEffect(() => {
    const row = rail.current;
    if (!row) return;
    function update() {
      if (!row) return;
      const start = row.scrollLeft <= 1;
      const end = row.scrollLeft + row.clientWidth >= row.scrollWidth - 1;
      setEdges(previous => previous.start === start && previous.end === end ? previous : { start, end });
    }
    const observer = new ResizeObserver(update);
    observer.observe(row);
    for (const card of row.children) observer.observe(card);
    row.addEventListener("scroll", update, { passive: true });
    update();
    return () => { observer.disconnect(); row.removeEventListener("scroll", update); };
  }, [order]);

  if (!pictures.length) return null;
  async function enableGyro() {
    setSensorError("");
    try { await requestGyro(); }
    catch (error) { setSensorError((error as Error).message); }
  }
  function slide(direction: -1 | 1) {
    const row = rail.current;
    if (!row) return;
    const cards = Array.from(row.children, card => (card as HTMLElement).offsetLeft);
    const current = row.scrollLeft;
    const step = Math.max(1, row.clientWidth - 44);
    const next = direction === 1 ? Math.min(current + step, cards.find(left => left > current + 1) ?? row.scrollWidth)
      : Math.max(current - step, cards.reverse().find(left => left < current - 1) ?? 0);
    row.scrollTo({ left: next });
  }
  return <section className="explore-recommendations" aria-labelledby={id}>
    <div className="explore-recommendations-heading">
      <div><h2 id={id}>추천 이미지</h2><p>{ranked ? "좋아요가 많은 배경화면" : "무작위로 둘러보는 배경화면"}</p></div>
      {gyro && !reduced && gyroSupported && !gyroReady && cards.some(card => card.kind) &&
        <button type="button" className="button secondary" disabled={gyroPending} onClick={() => void enableGyro()}>기울기 센서 사용</button>}
    </div>
    {gyro && !reduced && !gyroReady && sensorError && <p className="notice" role="status">{sensorError}</p>}
    <div className="explore-recommendations-carousel">
      <div className="explore-recommendations-rail" ref={rail} id={`${id}-rail`}>
        {cards.map(({ picture, kind }) => <Recommendation key={picture.id} picture={picture} selected={picture.id === selectedId}
          toggleDetails={toggleDetails} input={{ gyro, gyroReady, reduced, kind }} />)}
      </div>
      {!(edges.start && edges.end) && <>
        <button type="button" className="explore-recommendations-arrow previous" aria-label="이전 추천 이미지"
          aria-controls={`${id}-rail`} aria-disabled={edges.start} onClick={() => { if (!edges.start) slide(-1); }}><ChevronLeft size={26} /></button>
        <button type="button" className="explore-recommendations-arrow next" aria-label="다음 추천 이미지"
          aria-controls={`${id}-rail`} aria-disabled={edges.end} onClick={() => { if (!edges.end) slide(1); }}><ChevronRight size={26} /></button>
      </>}
    </div>
  </section>;
}
