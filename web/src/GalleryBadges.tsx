import { Download, Heart, Monitor, Smartphone, Tablet } from "lucide-react";
import type { Picture } from "./model";
import { platformTags, selectPlatform, type PlatformTag } from "./platform-tags";

const platforms = {
  pc: { label: "PC", Icon: Monitor },
  mobile: { label: "모바일", Icon: Smartphone },
  tablet: { label: "태블릿", Icon: Tablet },
};

export function PlatformTagOptions({ selected, change }: {
  selected: PlatformTag[];
  change: (value: PlatformTag[]) => void;
}) {
  return <div className="platform-tag-options" role="group" aria-label="상위 태그">
    {platformTags.map(tag => {
      const { label, Icon } = platforms[tag];
      return <label key={tag}>
        <input type="checkbox" checked={selected.includes(tag)}
          onChange={event => change(selectPlatform(selected, tag, event.target.checked))} />
        <Icon size={16} aria-hidden="true" /><span>{label}</span>
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

export function EngagementBadges({ picture, toggleLike, busy }: {
  picture: Picture;
  toggleLike?: () => void;
  busy?: boolean;
}) {
  const likes = picture.likes ?? 0;
  const downloads = picture.downloads ?? 0;
  const heart = <><Heart size={17} aria-hidden="true" fill={picture.liked ? "currentColor" : "none"} />
    <span>{likes.toLocaleString("ko-KR")}</span></>;
  return <span className="image-engagement-badges">
    {toggleLike ? <button type="button" className="image-overlay-badge" onClick={toggleLike} disabled={busy}
      aria-label={`좋아요 ${likes}개`} aria-pressed={!!picture.liked} title={picture.liked ? "좋아요 취소" : "좋아요"}>
      {heart}
    </button> : <span className="image-overlay-badge" aria-label={`좋아요 ${likes}개`}>{heart}</span>}
    <span className="image-overlay-badge" title="내 이미지에 저장된 횟수" aria-label={`다운로드 ${downloads}회`}>
      <Download size={17} aria-hidden="true" /><span>{downloads.toLocaleString("ko-KR")}</span>
    </span>
  </span>;
}
