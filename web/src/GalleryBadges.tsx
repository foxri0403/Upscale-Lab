import { useEffect, useRef, useState } from "react";
import { ChevronDown, Download, Heart, Monitor, Smartphone, Tablet, type LucideProps } from "lucide-react";
import type { Picture } from "./model";
import { imageTags } from "./image-tags";
import { platformTags, selectPlatform, type PlatformTag } from "./platform-tags";

function TabletLandscape(props: LucideProps) {
  return <Tablet {...props} className="tablet-landscape-icon" />;
}

const platforms = {
  pc: { label: "PC", Icon: Monitor },
  mobile: { label: "모바일", Icon: Smartphone },
  tablet: { label: "태블릿", Icon: TabletLandscape },
};

export function PlatformTagOptions({ selected, change, filter = false }: {
  selected: PlatformTag[];
  change: (value: PlatformTag[]) => void;
  filter?: boolean;
}) {
  return <div className={filter ? "tag-options" : "studio-tag-options"} role="group" aria-label="상위 태그">
    {platformTags.map(tag => {
      const { label, Icon } = platforms[tag];
      const name = <span className="platform-tag-label"><Icon size={16} aria-hidden="true" />{label}</span>;
      const checkbox = <input type="checkbox" checked={selected.includes(tag)}
        onChange={event => change(selectPlatform(selected, tag, event.target.checked))} />;
      return <label key={tag} className={filter ? "tag-option" : undefined}>
        {filter ? <>{name}{checkbox}</> : <>{checkbox}{name}</>}
      </label>;
    })}
  </div>;
}

export function PlatformBadges({ picture }: { picture: Picture }) {
  const selected = platformTags.filter(tag => picture.platformTags?.includes(tag));
  return selected.length > 0 && <span className="image-platform-badges" aria-label="상위 태그">
    {selected.map(tag => {
      const { label, Icon } = platforms[tag];
      return <span key={tag} className="image-overlay-badge" title={label} role="img" aria-label={label}>
        <Icon size={17} aria-hidden="true" />
      </span>;
    })}
  </span>;
}

export function PlatformTagList({ picture }: { picture: Picture }) {
  const selected = platformTags.filter(tag => picture.platformTags?.includes(tag));
  return <ul className="image-tag-list" aria-label="상위 태그">
    {selected.length ? selected.map(tag => {
      const { label, Icon } = platforms[tag];
      return <li key={tag}><Icon size={14} aria-hidden="true" />{label}</li>;
    }) : <li>상위 태그 없음</li>}
  </ul>;
}

export function LowerTagList({ picture }: { picture: Picture }) {
  const tags = imageTags(picture);
  return <ul className="image-tag-list" aria-label="하위 태그">
    {tags.length ? tags.map(tag => <li key={tag}>{tag}</li>) : <li>태그 없음</li>}
  </ul>;
}

export function CardTagPreview({ picture }: { picture: Picture }) {
  const tags = imageTags(picture);
  const disclosure = useRef<HTMLDetailsElement>(null);
  const [open, setOpen] = useState(false);
  useEffect(() => {
    if (!open) return;
    const closeOutside = (event: Event) => {
      if (disclosure.current && !disclosure.current.contains(event.target as Node)) disclosure.current.open = false;
    };
    document.addEventListener("pointerdown", closeOutside);
    document.addEventListener("focusin", closeOutside);
    return () => {
      document.removeEventListener("pointerdown", closeOutside);
      document.removeEventListener("focusin", closeOutside);
    };
  }, [open]);
  if (!tags.length) return <span className="image-tag-trigger image-tag-empty">태그 없음</span>;

  return <details className="image-tag-preview" ref={disclosure} onToggle={event => {
    const details = event.currentTarget;
    setOpen(details.open);
    if (!details.open) return;
    const trigger = details.querySelector("summary")!.getBoundingClientRect();
    const height = details.querySelector("ul")!.offsetHeight;
    details.dataset.side = trigger.bottom + height + 8 > innerHeight && trigger.top >= height + 8 ? "above" : "below";
  }} onKeyDown={event => {
    if (event.key !== "Escape" || !event.currentTarget.open) return;
    event.stopPropagation();
    event.currentTarget.open = false;
    event.currentTarget.querySelector("summary")?.focus();
  }}>
    <summary className="image-tag-trigger" aria-label="전체 태그 보기">
      <span>{tags.join(" · ")}</span><ChevronDown size={14} aria-hidden="true" />
    </summary>
    <LowerTagList picture={picture} />
  </details>;
}

export function GalleryLikeButton({ liked, disabled, toggle }: {
  liked: boolean;
  disabled?: boolean;
  toggle: () => void;
}) {
  const label = liked ? "좋아요 취소" : "좋아요";
  return <button type="button" className="gallery-like-button" aria-label={label} title={label}
    aria-pressed={liked} disabled={disabled} onClick={toggle}>
    <Heart size={20} fill={liked ? "currentColor" : "none"} aria-hidden="true" />
  </button>;
}

export function EngagementBadges({ picture }: { picture: Picture }) {
  const likes = picture.likes ?? 0;
  const downloads = picture.downloads ?? 0;
  return <span className="image-engagement-badges">
    <span className="image-overlay-badge" aria-label={`좋아요 ${likes}개`}>
      <Heart size={17} aria-hidden="true" fill={picture.liked ? "currentColor" : "none"} />
      <span>{likes.toLocaleString("ko-KR")}</span>
    </span>
    <span className="image-overlay-badge" title="내 이미지에 저장된 횟수" aria-label={`다운로드 ${downloads}회`}>
      <Download size={17} aria-hidden="true" /><span>{downloads.toLocaleString("ko-KR")}</span>
    </span>
  </span>;
}
