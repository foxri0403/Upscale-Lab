import { useEffect, useId, useRef, useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import type { Picture } from "./model";
import { PictureView } from "./ui";
import { EngagementBadges, PlatformBadges } from "./GalleryBadges";

function Recommendation({ picture, open }: { picture: Picture; open: (picture: Picture) => void }) {
  const [measured, setMeasured] = useState<{ source: string; ratio: number }>();
  const source = picture.background || picture.src;
  const ratio = (picture.width ?? 0) / (picture.height ?? 0);
  const hasRatio = Number.isFinite(ratio) && ratio > 0;
  const aspectRatio = hasRatio ? ratio : measured?.source === source ? measured.ratio : 16 / 9;
  return <button type="button" className="explore-recommendation" style={{ aspectRatio }}
    onClick={() => open(picture)} aria-label={`${picture.name} 상세 보기`}>
    <PictureView picture={picture} loaded={image => {
      if (!hasRatio && image.naturalWidth > 0 && image.naturalHeight > 0)
        setMeasured({ source, ratio: image.naturalWidth / image.naturalHeight });
    }} />
    <span className="explore-recommendation-title">{picture.name}</span>
    <PlatformBadges picture={picture} />
    <EngagementBadges picture={picture} />
  </button>;
}

export function ExploreRecommendations({ pictures, open, ranked }: {
  pictures: Picture[];
  open: (picture: Picture) => void;
  ranked: boolean;
}) {
  const id = useId();
  const rail = useRef<HTMLDivElement>(null);
  const [edges, setEdges] = useState({ start: true, end: true });
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
    </div>
    <div className="explore-recommendations-carousel">
      <div className="explore-recommendations-rail" ref={rail} id={`${id}-rail`}>
        {pictures.map(picture => <Recommendation key={picture.id} picture={picture} open={open} />)}
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
