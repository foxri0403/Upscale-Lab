import { useId, useRef } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import type { Picture } from "./model";
import { PictureView } from "./ui";
import { EngagementBadges, PlatformBadges } from "./GalleryBadges";

export function ExploreRecommendations({ pictures, open, ranked }: {
  pictures: Picture[];
  open: (picture: Picture) => void;
  ranked: boolean;
}) {
  const id = useId();
  const rail = useRef<HTMLDivElement>(null);
  if (!pictures.length) return null;
  function slide(direction: number) {
    const row = rail.current;
    if (row?.firstElementChild) {
      const step = row.firstElementChild.getBoundingClientRect().width + parseFloat(getComputedStyle(row).columnGap);
      row.scrollBy({ left: step * direction });
    }
  }
  return <section className="explore-recommendations" aria-labelledby={id}>
    <div className="explore-recommendations-heading">
      <div><h2 id={id}>추천 이미지</h2><p>{ranked ? "좋아요가 많은 배경화면" : "무작위로 둘러보는 배경화면"}</p></div>
      {pictures.length > 1 && <div className="explore-recommendations-controls">
        <button type="button" className="icon-button" aria-label="이전 추천 이미지" onClick={() => slide(-1)}><ChevronLeft size={20} /></button>
        <button type="button" className="icon-button" aria-label="다음 추천 이미지" onClick={() => slide(1)}><ChevronRight size={20} /></button>
      </div>}
    </div>
    <div className="explore-recommendations-rail" ref={rail}>
      {pictures.map(picture => <button type="button" key={picture.id} className="explore-recommendation"
        onClick={() => open(picture)} aria-label={`${picture.name} 상세 보기`}>
        <PictureView picture={picture} />
        <span className="explore-recommendation-title">{picture.name}</span>
        <PlatformBadges picture={picture} />
        <EngagementBadges picture={picture} />
      </button>)}
    </div>
  </section>;
}
