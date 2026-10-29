import { useState } from "react";
import {
  Check,
  Layers3,
  MoreVertical,
  Pencil,
  Tag,
  Trash2,
  Info,
} from "lucide-react";
import { PictureView } from "./ui";
import { imageTags, tagSummary } from "./image-tags";
import type { Picture } from "./model";
import { isImageShared } from "./image-sharing";
import { ImageSharingToggle } from "./ImageSharingToggle";
import { ImageFeatureBadges } from "./ImageFeatureBadges";
import { CreatorAvatar } from "./CreatorAvatar";

export function ImageCard({
  picture,
  explore,
  selected = false,
  open,
  rename,
  edit,
  remove,
}: {
  picture: Picture;
  explore: boolean;
  selected?: boolean;
  open: () => void;
  rename: () => void;
  edit: () => void;
  remove: () => void;
}) {
  const [decoded, setDecoded] = useState<{
    src: string;
    width: number;
    height: number;
  } | null>(null);
  const [failedSource, setFailedSource] = useState<string | null>(null);
  const source = picture.background || picture.src;
  const tags = imageTags(picture);
  const dimensions = decoded?.src === source ? decoded : picture;
  const resolution =
    dimensions.width && dimensions.height
      ? `${dimensions.width.toLocaleString("en-US")} × ${dimensions.height.toLocaleString("en-US")} px`
      : failedSource === source
        ? "해상도 정보 없음"
        : "해상도 확인 중…";
  const shared = isImageShared(picture);
  return (
    <article className={`image-card ${selected ? "image-card-selected" : ""}`}>
      <button
        className="image-card-cover"
        aria-label={`${picture.name} ${explore ? "상세 보기" : "편집하기"}`}
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
      </button>
      <div className="image-card-body">
        <button className="image-title" onClick={open} title={picture.name}>
          {picture.name}
        </button>
        <div className="image-tags" title={tags.join(" · ") || "장르 없음"}>
          <Tag size={14} />
          <span>{tagSummary(tags)}</span>
        </div>
        <p className="image-resolution" aria-label={`${picture.name} 해상도`}>
          {resolution}
        </p>
        {explore && <span className="creator"><CreatorAvatar name={picture.creator} source={picture.creatorAvatar} /><span>{picture.creator}</span></span>}
        <ImageFeatureBadges picture={picture} />
        {!explore && <div className="card-actions">
          {!explore && shared && (
            <span className="mobile-shared-badge" aria-label="공유된 이미지">
              공유
              <span>
                <Check size={11} strokeWidth={3} />
              </span>
            </span>
          )}
          <details className="card-menu">
            <summary aria-label={`${picture.name} 이미지 설정`}>
              <MoreVertical size={20} />
            </summary>
            <div>
              {explore ? (
                <button
                  onClick={(e) => {
                    e.currentTarget.closest("details")?.removeAttribute("open");
                    open();
                  }}
                >
                  <Info size={15} /> 상세 보기
                </button>
              ) : (
                <>
                  <div className="image-sharing-row">
                    <span>공유 {shared ? "ON" : "OFF"}</span>
                    <ImageSharingToggle picture={picture} label={`${picture.name} 공유`} />
                  </div>
                  <button
                    disabled={shared}
                    title={shared ? "공유를 끈 뒤 변경할 수 있습니다." : undefined}
                    onClick={(e) => {
                      e.currentTarget
                        .closest("details")
                        ?.removeAttribute("open");
                      rename();
                    }}
                  >
                    <Pencil size={15} /> 이름 변경
                  </button>
                  <button
                    disabled={shared}
                    title={shared ? "공유를 끈 뒤 변경할 수 있습니다." : undefined}
                    onClick={(e) => {
                      e.currentTarget.closest("details")?.removeAttribute("open");
                      edit();
                    }}
                  >
                    <Layers3 size={15} /> 사진 설정
                  </button>
                  <button
                    className="danger-text"
                    onClick={(e) => {
                      e.currentTarget
                        .closest("details")
                        ?.removeAttribute("open");
                      remove();
                    }}
                  >
                    <Trash2 size={15} /> 보관함에서 삭제
                  </button>
                </>
              )}
            </div>
          </details>
        </div>}
      </div>
    </article>
  );
}
