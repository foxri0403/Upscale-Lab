import { useEffect, useState } from "react";
import type { Picture } from "./model";
import { Modal } from "./ui";
import { imageFileFormat, imageResolution } from "./image-information";
import { imageFeatures } from "./image-features";

export function ImageInformation({ picture, layerCount, close }: {
  picture: Picture;
  layerCount: number;
  close: () => void;
}) {
  const knownResolution = imageResolution(picture.width, picture.height);
  const [measured, setMeasured] = useState<{ source: string; resolution: string | null } | null>(null);
  useEffect(() => {
    if (knownResolution) return;
    let active = true;
    const image = new Image();
    image.onload = () => {
      if (active) setMeasured({ source: picture.src,
        resolution: imageResolution(image.naturalWidth, image.naturalHeight) });
    };
    image.onerror = () => {
      if (active) setMeasured({ source: picture.src, resolution: null });
    };
    // Natural dimensions do not require cross-origin fetch/canvas access.
    image.src = picture.src;
    return () => { active = false; image.onload = null; image.onerror = null; };
  }, [picture.src, knownResolution]);
  const resolution = knownResolution || (measured?.source === picture.src
    ? measured.resolution || "정보 없음" : "해상도 확인 중…");
  const created = new Date(picture.created);
  return (
    <Modal title="이미지 정보" close={close}>
      <dl className="image-information">
        <div><dt>이미지 이름</dt><dd>{picture.name}</dd></div>
        <div><dt>해상도</dt><dd aria-live="polite">{resolution}</dd></div>
        <div><dt>파일 형식</dt><dd>{imageFileFormat(picture.src, picture.contentType)}</dd></div>
        <div><dt>이용한 기능</dt><dd>{imageFeatures(picture).join(" · ") || "정적 이미지"}</dd></div>
        <div><dt>레이어</dt><dd>{layerCount}개</dd></div>
        <div><dt>등록일</dt><dd>{Number.isNaN(created.getTime()) ? "정보 없음"
          : created.toLocaleString("ko-KR")}</dd></div>
      </dl>
      <p className="image-information-help">현재 불러온 이미지의 정보입니다. 저장하지 않은 자르기·배율 변경은 포함하지 않습니다.</p>
    </Modal>
  );
}
