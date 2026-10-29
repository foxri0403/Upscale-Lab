import { useState } from "react";
import { Monitor, Plus, SquarePen, X } from "lucide-react";
import { CreatorAvatar } from "./CreatorAvatar";
import type { Picture } from "./model";
import { imageTags } from "./image-tags";
import { imageFileFormat, imageResolution } from "./image-information";
import { ImageFeatureBadges } from "./ImageFeatureBadges";
import { Busy, Notice, PictureView } from "./ui";

export function ExploreDetails({ picture, importing, own, local, close, save, openStudio, apply }: {
  picture: Picture;
  importing: boolean;
  own: boolean;
  local: boolean;
  close: () => void;
  save: () => void;
  openStudio: () => void;
  apply: () => void;
}) {
  const [measured, setMeasured] = useState<{ source: string; resolution: string | null } | null>(null);
  const source = picture.background || picture.src;
  const known = imageResolution(picture.width, picture.height);
  const resolution = known || (measured?.source === source
    ? measured?.resolution || "해상도 정보 없음" : "해상도 확인 중…");
  const created = new Date(picture.created);
  return (
    <aside id="explore-details" className="explore-details" aria-label="이미지 상세"
      onKeyDown={event => {
        if (event.key === "Escape") { event.stopPropagation(); close(); }
      }}>
      <div className="explore-details-heading">
        <h2>이미지 상세</h2>
        <button className="icon-button" aria-label="이미지 상세 닫기" onClick={close}>
          <X size={20} />
        </button>
      </div>
        <div className="explore-details-content">
          <div className="explore-details-preview">
            <PictureView picture={picture}
              loaded={image => {
                if (image.currentSrc === new URL(source, location.href).href)
                  setMeasured({ source, resolution: imageResolution(image.naturalWidth, image.naturalHeight) });
              }}
              failed={() => setMeasured({ source, resolution: null })} />
          </div>
          <h3>{picture.name}</h3>
          <div className="explore-details-creator"><CreatorAvatar name={picture.creator} source={picture.creatorAvatar} />{picture.creator || "작성자 정보 없음"}</div>
          {created && !Number.isNaN(created.getTime()) && <p className="explore-details-date">
            {created.toLocaleDateString("ko-KR")} 업로드
          </p>}
          <ImageFeatureBadges picture={picture} />
          <section className="explore-details-section" aria-label="이미지 정보">
            <h4>이미지 정보</h4>
            <dl className="image-information">
              <div><dt>해상도</dt><dd aria-live="polite">{resolution}</dd></div>
              <div><dt>파일 형식</dt><dd>{imageFileFormat(picture.src, picture.contentType)}</dd></div>
            </dl>
          </section>
          <section className="explore-details-section" aria-label="전체 태그">
            <h4>태그</h4>
            <div className="explore-details-tags">
              {imageTags(picture).length ? imageTags(picture).map(tag => <span key={tag}>{tag}</span>) : <span>장르 없음</span>}
            </div>
          </section>
          {local && <Notice>로컬 샘플 갤러리입니다. 공개 범위는 현재 브라우저로 한정됩니다.</Notice>}
        </div>
        <div className="explore-details-actions">
          {!own && <button className="button primary full" disabled={importing} onClick={save}>
            {importing ? <Busy /> : <Plus size={18} />} 내 이미지에 저장
          </button>}
          <button className="button secondary full" disabled={importing} onClick={own ? openStudio : apply}>
            {own ? <SquarePen size={18} /> : <Monitor size={18} />}
            {own ? "작업실에서 열기" : "연결된 기기에 적용"}
          </button>
        </div>
    </aside>
  );
}
