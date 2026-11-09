import { useEffect, useRef, useState, type PointerEvent } from "react";
import { Monitor, RotateCcw } from "lucide-react";
import type { Device, Display, Picture } from "./model";
import { wallpaperTargets } from "./explore-actions";
import { Busy, Empty, Link, Modal, Notice } from "./ui";
import { cloud } from "./api";
import { detailPicture } from "./cloud-store";
import { FramedWallpaper } from "./FramedWallpaper";
import { defaultFrame, displaySize, dragFrame, frameLayout, wallpaperCrop, type ScreenSize, type WallpaperFrame } from "./wallpaper-frame";

type Target = ReturnType<typeof wallpaperTargets>[number] & { display: Display; platform: Device["platform"] };
export type WallpaperSelection = { picture: Picture; deviceId: string; displayId: string; frame: WallpaperFrame; pcMotion: boolean };

export function ApplyWallpaperDialog({ picture, pictures, devices, fixedTarget, fromExplore, pcMotionAvailable, busy, close, confirm }: {
  picture?: Picture;
  pictures?: Picture[];
  devices: Device[];
  fixedTarget?: string;
  fromExplore?: boolean;
  pcMotionAvailable?: boolean;
  busy: boolean;
  close: () => void;
  confirm: (selection: WallpaperSelection) => Promise<void>;
}) {
  const targets = wallpaperTargets(devices, !!fromExplore);
  const [targetId, setTargetId] = useState(targets[0]?.id || "");
  const [pictureId, setPictureId] = useState(pictures?.[0]?.id || "");
  const target = targets.find(value => value.id === (fixedTarget || targetId)) || (fixedTarget ? undefined : targets[0]);
  const device = devices.find(value => value.id === target?.deviceId);
  const display = device?.displays.find(value => value.id === target?.displayId);
  const selected = target && device && display ? { ...target, display, platform: device.platform } : undefined;
  const selectedPicture = picture || pictures?.find(value => value.id === pictureId) || pictures?.[0];
  return <Modal title="배경화면 적용 미리보기" className="wallpaper-apply-dialog" wide close={() => { if (!busy) close(); }}>
    {pictures && <label>이미지 선택
      <select value={selectedPicture?.id || ""} disabled={busy} onChange={event => setPictureId(event.target.value)}>
        {pictures.map(value => <option key={value.id} value={value.id}>{value.name}</option>)}
      </select>
    </label>}
    {!fixedTarget && targets.length > 0 && <label>기기 및 화면
      <select value={selected?.id || ""} disabled={busy} onChange={event => setTargetId(event.target.value)}>
        {targets.map(target => <option key={target.id} value={target.id}>{target.name}</option>)}
      </select>
    </label>}
    {!selected ? <Empty text="적용할 수 있는 화면이 없어요">
      <p>연결 앱에 로그인한 뒤 적용할 화면을 확인해 주세요.</p>
      {!fixedTarget && <Link to="/app/devices" className="button secondary">기기 관리로 이동</Link>}
    </Empty> : !selectedPicture ? <Empty text="먼저 내 이미지에 사진을 추가해 주세요" />
      : <WallpaperApplyPreview key={`${selectedPicture.id}/${selected.id}/${selected.display.resolution}`}
        picture={selectedPicture} target={selected} fromExplore={fromExplore} pcMotionAvailable={pcMotionAvailable}
        busy={busy} confirm={confirm} />}
  </Modal>;
}

function WallpaperApplyPreview({ picture, target, fromExplore, pcMotionAvailable, busy, confirm }: {
  picture: Picture; target: Target; fromExplore?: boolean; pcMotionAvailable?: boolean; busy: boolean;
  confirm: (selection: WallpaperSelection) => Promise<void>;
}) {
  const [prepared, setPrepared] = useState<Picture>();
  const [measured, setMeasured] = useState<{ src: string; size: ScreenSize }>();
  const [error, setError] = useState("");
  const needsDetail = cloud && !!picture.projectId && !picture.layers;
  const preview = needsDetail ? prepared : picture;
  const source = preview?.background || preview?.src;
  useEffect(() => {
    if (!needsDetail) return;
    let active = true;
    detailPicture(picture).then(value => { if (active) setPrepared(value); },
      (err: Error) => { if (active) setError(err.message); });
    return () => { active = false; };
  }, [needsDetail, picture]);
  useEffect(() => {
    if (!source) return;
    const image = new Image();
    image.onload = () => setMeasured({ src: source, size: { width: image.naturalWidth, height: image.naturalHeight } });
    image.onerror = () => setError("이미지를 불러오지 못했습니다.");
    image.src = source;
    return () => { image.onload = image.onerror = null; };
  }, [source]);
  const size = measured?.src === source ? measured?.size : undefined;
  const screen = displaySize(target.display);
  if (!screen) return <Notice>기기에서 화면 해상도를 확인한 뒤 다시 시도해 주세요.</Notice>;
  if (error) return <Notice>{error}</Notice>;
  if (!preview || !size) return <p className="explore-result-status" role="status"><Busy /> 적용 미리보기를 불러오는 중…</p>;
  return <WallpaperFrameControls key={source} picture={preview} target={target} size={size} screen={screen}
    busy={busy} confirm={confirm} fromExplore={fromExplore} pcMotionAvailable={pcMotionAvailable} />;
}

function WallpaperFrameControls({ picture, target, size, screen, fromExplore, pcMotionAvailable, busy, confirm }: {
  picture: Picture; target: Target; size: ScreenSize; screen: ScreenSize; fromExplore?: boolean;
  pcMotionAvailable?: boolean; busy: boolean; confirm: (selection: WallpaperSelection) => Promise<void>;
}) {
  const crop = wallpaperCrop(picture, size);
  const [frame, setFrame] = useState(() => defaultFrame(screen, crop));
  const [pcMotion, setPcMotion] = useState(true);
  const drag = useRef<{ id: number; x: number; y: number; frame: WallpaperFrame } | null>(null);
  const submitting = useRef(false);
  const layout = frameLayout(frame, crop);
  const character = !picture.background && ((picture.layers?.length ?? 0) >= 2 || (picture.foregrounds?.length ?? 0) >= 2);
  const showPcMotion = pcMotionAvailable && target.platform === "Windows" && character;
  function move(event: PointerEvent<HTMLDivElement>) {
    const value = drag.current;
    if (busy || !value || event.pointerId !== value.id) return;
    const bounds = event.currentTarget.getBoundingClientRect();
    setFrame(dragFrame(value.frame, crop, (event.clientX - value.x) / bounds.width, (event.clientY - value.y) / bounds.height));
  }
  async function submit() {
    if (submitting.current) return;
    submitting.current = true;
    try {
      await confirm({ picture: { ...picture, ...size }, deviceId: target.deviceId, displayId: target.displayId,
        frame, pcMotion: !!showPcMotion && pcMotion });
    } finally { submitting.current = false; }
  }
  return <>
    <div className="wallpaper-frame-heading"><strong>{target.name}</strong><span>{screen.width} × {screen.height} px</span></div>
    <div className="wallpaper-screen-frame" aria-label="대상 화면 미리보기"
      style={{ aspectRatio: `${layout.width} / ${layout.height}`, width: `min(100%, ${36 * layout.width / layout.height}dvh)` }}
      onPointerDown={event => {
        if (busy || !event.isPrimary || event.button !== 0) return;
        drag.current = { id: event.pointerId, x: event.clientX, y: event.clientY, frame };
        event.currentTarget.setPointerCapture(event.pointerId);
      }} onPointerMove={move} onLostPointerCapture={() => { drag.current = null; }}>
      <FramedWallpaper picture={picture} size={size} frame={frame} />
    </div>
    <fieldset className="wallpaper-frame-settings" disabled={busy}>
      <legend>이미지 배치</legend>
      <div className="wallpaper-fit-options">
        <button type="button" className={`button ${frame.contain ? "secondary" : "primary"}`} aria-pressed={!frame.contain}
          onClick={() => setFrame({ ...frame, contain: false, zoom: 1 })}>화면 채우기</button>
        <button type="button" className={`button ${frame.contain ? "primary" : "secondary"}`} aria-pressed={frame.contain}
          onClick={() => setFrame({ ...frame, contain: true, zoom: 1 })}>이미지 전체 표시</button>
        <button type="button" className="button secondary" onClick={() => setFrame(defaultFrame(screen, crop))}><RotateCcw size={16} /> 초기화</button>
      </div>
      <p>미리보기에서 이미지를 끌거나 아래 조절 막대를 사용하세요.</p>
      <label>확대 {Math.round(frame.zoom * 100)}%
        <input type="range" min="1" max="3" step="0.01" value={frame.zoom}
          onChange={event => setFrame({ ...frame, zoom: event.target.valueAsNumber })} />
      </label>
      {Math.abs(layout.travelX) >= 1 && <label>가로 위치
        <input type="range" min="0" max="1" step="0.01" value={layout.travelX < 0 ? 1 - frame.x : frame.x}
          onChange={event => setFrame({ ...frame, x: layout.travelX < 0 ? 1 - event.target.valueAsNumber : event.target.valueAsNumber })} />
      </label>}
      {Math.abs(layout.travelY) >= 1 && <label>세로 위치
        <input type="range" min="0" max="1" step="0.01" value={layout.travelY < 0 ? 1 - frame.y : frame.y}
          onChange={event => setFrame({ ...frame, y: layout.travelY < 0 ? 1 - event.target.valueAsNumber : event.target.valueAsNumber })} />
      </label>}
      {showPcMotion && <label className="studio-rig-toggle">
        <input type="checkbox" checked={pcMotion} onChange={event => setPcMotion(event.target.checked)} />
        <span>PC에서 Live 2D 파츠 움직임 켜고 적용</span>
      </label>}
    </fieldset>
    <Notice>{fromExplore ? "이미지를 내 이미지에 저장한 뒤 적용 요청을 전송합니다. " : ""}
      {cloud ? "연결 앱이 처리한 뒤 기기 관리에서 적용 결과를 확인할 수 있습니다." : "샘플 기기 관리에서 동기화하면 적용 결과를 확인할 수 있습니다."}</Notice>
    <button className="button primary full" disabled={busy} onClick={() => void submit()}>
      {busy ? <Busy /> : <Monitor size={18} />}{busy ? "적용 요청 전송 중…" : "이 배치로 적용"}
    </button>
  </>;
}
