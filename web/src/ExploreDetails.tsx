import { useState } from "react";
import { Image, Plus, SquarePen, UserRound, X } from "lucide-react";
import type { Picture } from "./model";
import { imageTags } from "./image-tags";
import { imageFileFormat, imageResolution } from "./image-information";
import { ImageFeatureBadges } from "./ImageFeatureBadges";
import { Busy, Notice, PictureView } from "./ui";

export function ExploreDetails({ picture, importing, own, local, close, save, unshare }: {
  picture: Picture | null;
  importing: boolean;
  own: boolean;
  local: boolean;
  close: () => void;
  save: (open: boolean) => void;
  unshare: () => void;
}) {
  const [measured, setMeasured] = useState<{ source: string; resolution: string | null } | null>(null);
  const source = picture?.background || picture?.src;
  const known = picture && imageResolution(picture.width, picture.height);
  const resolution = known || (measured?.source === source
    ? measured?.resolution || "해상도 정보 없음" : "해상도 확인 중…");
  const created = picture && new Date(picture.created);
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
      {picture ? <>
        <div className="explore-details-content">
          <div className="explore-details-preview">
            <PictureView picture={picture}
              loaded={image => {
                if (image.currentSrc === new URL(source!, location.href).href)
                  setMeasured({ source: source!, resolution: imageResolution(image.naturalWidth, image.naturalHeight) });
              }}
              failed={() => setMeasured({ source: source!, resolution: null })} />
          </div>
          <h3>{picture.name}</h3>
          <div className="explore-details-creator"><UserRound size={18} />{picture.creator || "작성자 정보 없음"}</div>
          {created && !Number.isNaN(created.getTime()) && <p className="explore-details-date">
            {created.toLocaleDateString("ko-KR")} 업로드
          </p>}
          <ImageFeatureBadges picture={picture} />
          <section className="explore-details-section" aria-label="이미지 정보">
            <h4>이미지 정보</h4>
            <dl className="image-information">
              <div><dt>해상도</dt><dd aria-live="polite">{resolution}</dd></div>
              <div><dt>파일 형식</dt><dd>{imageFileFormat(picture.src, picture.contentType)}</dd></div>
              <div><dt>다운로드</dt><dd>{(picture.downloads || 0).toLocaleString("ko-KR")}회</dd></div>
            </dl>
          </section>
          <section className="explore-details-section" aria-label="전체 태그">
            <h4>태그</h4>
            <div className="explore-details-tags">
              {imageTags(picture).length ? imageTags(picture).map(tag => <span key={tag}>{tag}</span>) : <span>장르 없음</span>}
            </div>
          </section>
          {own && <button className="text-button danger-text unpublish" onClick={unshare}>이 게시물 공유 취소</button>}
          {local && <Notice>로컬 샘플 갤러리입니다. 공개 범위는 현재 브라우저로 한정됩니다.</Notice>}
          {!local && picture.galleryPostId && <a className="text-button" href={picture.src} target="_blank" rel="noopener noreferrer">원본 이미지 열기</a>}
        </div>
        <div className="explore-details-actions">
          <button className="button primary full" disabled={importing} onClick={() => save(false)}>
            {importing ? <Busy /> : <Plus size={18} />} 내 이미지에 저장
          </button>
          <button className="button secondary full" disabled={importing} onClick={() => save(true)}>
            <SquarePen size={18} /> 작업실에서 열기
          </button>
        </div>
      </> : <div className="explore-details-empty"><Image size={38} /><strong>이미지를 선택하세요</strong><p>이미지를 누르면 여기에 상세 정보가 표시됩니다.</p></div>}
    </aside>
  );
}
