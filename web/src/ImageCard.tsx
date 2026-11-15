import { useEffect, useRef, useState } from "react";
import {
  Check,
  MoreVertical,
  Pencil,
  Trash2,
  Upload,
} from "lucide-react";
import { PictureView } from "./ui";
import type { Picture } from "./model";
import { isImageShared } from "./image-sharing";
import { ImageSharingToggle } from "./ImageSharingToggle";
import { ImageFeatureBadges } from "./ImageFeatureBadges";
import { CreatorAvatar } from "./CreatorAvatar";
import { CardTagPreview, EngagementBadges, GalleryLikeButton, PlatformBadges } from "./GalleryBadges";

export function ImageCard({
  picture,
  explore,
  selected = false,
  toggleLike,
  liking,
  open,
  editMetadata,
  unpublish,
  remove,
}: {
  picture: Picture;
  explore: boolean;
  selected?: boolean;
  toggleLike?: () => void;
  liking?: boolean;
  open: () => void;
  editMetadata: () => void;
  unpublish?: () => void;
  remove: () => void;
}) {
  const menu = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    const closeOutside = (event: Event) => {
      if (menu.current && !menu.current.contains(event.target as Node))
        menu.current.open = false;
    };
    document.addEventListener("pointerdown", closeOutside);
    document.addEventListener("focusin", closeOutside);
    return () => {
      document.removeEventListener("pointerdown", closeOutside);
      document.removeEventListener("focusin", closeOutside);
    };
  }, []);
  const [decoded, setDecoded] = useState<{
    src: string;
    width: number;
    height: number;
  } | null>(null);
  const [failedSource, setFailedSource] = useState<string | null>(null);
  const source = picture.background || picture.src;
  const dimensions = decoded?.src === source ? decoded : picture;
  const resolution =
    dimensions.width && dimensions.height
      ? `${dimensions.width.toLocaleString("en-US")} × ${dimensions.height.toLocaleString("en-US")} px`
      : failedSource === source
        ? "해상도 정보 없음"
        : "해상도 확인 중…";
  const shared = isImageShared(picture);
  const showMenu = !explore || !!unpublish;
  const creator = picture.creator?.trim();
  const sourceLabel = creator ? `${creator}의 이미지` : "공유 이미지";
  function runMenuAction(action: () => void) {
    if (menu.current) menu.current.open = false;
    action();
  }
  return (
    <article className={`image-card ${selected ? "image-card-selected" : ""}`}>
      <button
        className="image-card-cover"
        aria-label={`${picture.name} ${explore || picture.isReadOnly ? "상세 보기" : "편집하기"}`}
        aria-pressed={explore ? selected : undefined}
        aria-controls={explore && selected ? "explore-details" : undefined}
        onClick={open}
      >
        <PictureView
          picture={picture}
          failed={() => setFailedSource(source)}
          loaded={(image) => {
            if (
              image.naturalWidth > 0 &&
              image.naturalHeight > 0 &&
              image.currentSrc === new URL(source, location.href).href
            )
              setDecoded({
                src: source,
                width: image.naturalWidth,
                height: image.naturalHeight,
              });
          }}
        />
        <PlatformBadges picture={picture} />
        {explore && <EngagementBadges picture={picture} />}
      </button>
      <div className="image-card-body">
        <div className="image-card-heading">
          <div className="image-card-name-and-resolution">
            <button className="image-title" onClick={open} title={picture.name}>{picture.name}</button>
            <p className="image-resolution" aria-label={`${picture.name} 해상도`}>{resolution}</p>
          </div>
          {showMenu ? <details className="card-menu" ref={menu}
            onKeyDown={(event) => {
              if (event.key === "Escape" && menu.current) {
                menu.current.open = false;
                menu.current.querySelector("summary")?.focus();
              }
            }}>
            <summary aria-label={`${picture.name} 이미지 설정`}>
              <MoreVertical size={20} />
            </summary>
            <div>
              {explore ? (
                <>
                  <button type="button" onClick={() => runMenuAction(editMetadata)}><Pencil size={15} /> 이름 및 태그 수정</button>
                  {unpublish && <button type="button" className="danger-text" onClick={() => runMenuAction(unpublish)}>
                    <Upload size={15} /> 업로드 취소
                  </button>}
                </>
              ) : (
                <>
                  {!picture.isReadOnly && <>
                    <div className="image-sharing-row">
                      <span>업로드 {shared ? "ON" : "OFF"}</span>
                      <ImageSharingToggle picture={picture} label={`${picture.name} 업로드`} />
                    </div>
                    <button type="button" onClick={() => runMenuAction(editMetadata)}>
                      <Pencil size={15} /> 이름 및 태그 수정
                    </button>
                  </>}
                  <button type="button" className="danger-text" onClick={() => runMenuAction(remove)}>
                    <Trash2 size={15} /> 보관함에서 삭제
                  </button>
                </>
              )}
            </div>
          </details> : toggleLike && <GalleryLikeButton liked={!!picture.liked} disabled={liking} toggle={toggleLike} />}
        </div>
        {explore && <span className="creator"><CreatorAvatar name={picture.creator} source={picture.creatorAvatar} /><span>{picture.creator}</span></span>}
        <ImageFeatureBadges picture={picture} />
        <div className="image-card-footer">
          <CardTagPreview picture={picture} />
          {!explore && (picture.isReadOnly ? <span className="mobile-shared-badge image-source-badge" title={sourceLabel}>{sourceLabel}</span> : shared && <span className="mobile-shared-badge" aria-label="업로드된 이미지">
            업로드 <span><Check size={11} strokeWidth={3} /></span>
          </span>)}
        </div>
      </div>
    </article>
  );
}
