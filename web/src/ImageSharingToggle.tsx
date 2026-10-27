import { useState } from "react";
import { useDemo } from "./Workspace";
import { isImageShared } from "./image-sharing";
import type { Picture } from "./model";

export function ImageSharingToggle({ picture, label = "탐색 공유", disabled = false }: {
  picture: Picture;
  label?: string;
  disabled?: boolean;
}) {
  const { publish } = useDemo();
  const [busy, setBusy] = useState(false);
  const enabled = isImageShared(picture);
  return (
    <button
      type="button"
      className={`switch ${enabled ? "on" : ""}`}
      role="switch"
      aria-label={label}
      aria-checked={enabled}
      disabled={busy || disabled}
      onClick={async () => {
        if (enabled && picture.galleryPostId && !window.confirm(
          "기존 모바일 공유 게시물과 댓글·좋아요를 삭제할까요? 보관함의 원본은 유지됩니다.",
        )) return;
        setBusy(true);
        try { await publish(picture, !enabled); }
        finally { setBusy(false); }
      }}
    >
      <span />
    </button>
  );
}
