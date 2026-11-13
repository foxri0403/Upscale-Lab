import { useEffect, useRef, useState, type PointerEvent } from "react";
import { Images, Monitor, Mouse, Smartphone } from "lucide-react";
import { defaultEdit, type Device, type Display, type Picture } from "./model";
import { wallpaperTargets } from "./explore-actions";
import { Busy, Empty, Link, Modal, Notice } from "./ui";
import { cloud } from "./api";
import { refreshWallpaperPreview } from "./cloud-store";
import { FramedWallpaper } from "./FramedWallpaper";
import { OptionPicker } from "./OptionPicker";
import { useParallax } from "./use-parallax";
import { defaultDemoInput } from "./demo-input";
import { wallpaperExportPolicy } from "./wallpaper-export-policy";
import { normalizeRig } from "./character-rig";
import { motionStrength } from "./editor-defaults";
import { defaultFrame, displaySize, dragFrame, frameLayout, wallpaperCrop, type ScreenSize, type WallpaperFrame } from "./wallpaper-frame";

type Target = ReturnType<typeof wallpaperTargets>[number] & { display: Display; platform: Device["platform"] };
export type WallpaperSelection = { picture: Picture; deviceId: string; displayId: string; frame: WallpaperFrame };

export function ApplyWallpaperDialog({ picture, pictures, currentPicture, devices, fixedTarget, fromExplore, busy, close, confirm }: {
  picture?: Picture;
  pictures?: Picture[];
  currentPicture?: Picture;
  devices: Device[];
  fixedTarget?: string;
  fromExplore?: boolean;
  busy: boolean;
  close: () => void;
  confirm: (selection: WallpaperSelection) => Promise<void>;
}) {
  const targets = wallpaperTargets(devices, !!fromExplore);
  const [targetId, setTargetId] = useState(targets[0]?.id || "");
  const [pictureId, setPictureId] = useState<string>();
  const target = targets.find(value => value.id === (fixedTarget || targetId)) || (fixedTarget ? undefined : targets[0]);
  const device = devices.find(value => value.id === target?.deviceId);
  const display = device?.displays.find(value => value.id === target?.displayId);
  const selected = target && device && display ? { ...target, display, platform: device.platform } : undefined;
  const choices = currentPicture && pictures ? [currentPicture, ...pictures.filter(value => value.id !== currentPicture.id)] : pictures;
  const selectedPicture = picture ?? choices?.find(value => value.id === pictureId) ?? currentPicture;
  return <Modal title="배경화면 적용 미리보기" className="wallpaper-apply-dialog" wide close={() => { if (!busy) close(); }}>
    {choices && <div className="wallpaper-picker"><span>이미지 선택</span>
      <OptionPicker label="이미지 선택" value={selectedPicture?.id || ""} disabled={busy} change={setPictureId}
        icon={<Images size={18} aria-hidden="true" />} options={choices.map(value => ({ value: value.id, label: value.name }))} />
    </div>}
    {!fixedTarget && targets.length > 0 && <div className="wallpaper-picker"><span>기기 및 화면</span>
      <OptionPicker label="기기 및 화면" value={selected?.id || ""} disabled={busy} change={setTargetId}
        icon={<Monitor size={18} aria-hidden="true" />} options={targets.map(value => ({ value: value.id, label: value.name }))} />
    </div>}
    {!selected ? <Empty text="적용할 수 있는 화면이 없어요">
      <p>연결 앱에 로그인한 뒤 적용할 화면을 확인해 주세요.</p>
      {!fixedTarget && <Link to="/app/devices" className="button secondary">기기 관리로 이동</Link>}
    </Empty> : !selectedPicture ? <Empty text={choices ? "이미지 정보 없음" : "먼저 내 이미지에 사진을 추가해 주세요"} />
      : <WallpaperApplyPreview key={`${selectedPicture.id}/${selected.id}/${selected.display.resolution}`}
        picture={selectedPicture} target={selected} fromExplore={fromExplore}
        busy={busy} confirm={confirm} />}
  </Modal>;
}

function WallpaperApplyPreview({ picture, target, fromExplore, busy, confirm }: {
  picture: Picture; target: Target; fromExplore?: boolean; busy: boolean;
  confirm: (selection: WallpaperSelection) => Promise<void>;
}) {
  const [prepared, setPrepared] = useState<{ revision: string; value?: Picture; error?: string }>();
  const [measured, setMeasured] = useState<{ src: string; size?: ScreenSize; error?: string }>();
  const needsDetail = cloud && !!(picture.projectId || picture.assetId);
  const revision = JSON.stringify([picture.projectId, picture.assetId, picture.savedAt, picture.jobId,
    picture.name, picture.edit, picture.layers?.map(layer => layer.id)]);
  const input = useRef(picture);
  input.current = picture;
  const result = prepared?.revision === revision ? prepared : undefined;
  const preview = needsDetail ? result?.value : picture;
  const source = preview?.background || preview?.foregrounds?.[0] || preview?.src;
  useEffect(() => {
    if (!needsDetail) return;
    const request = new AbortController();
    refreshWallpaperPreview(input.current, AbortSignal.any([request.signal, AbortSignal.timeout(30000)])).then(
      value => { if (!request.signal.aborted) setPrepared({ revision, value }); },
      (err: Error) => { if (!request.signal.aborted) setPrepared({ revision, error: err.message }); });
    return () => request.abort();
  }, [needsDetail, revision]);
  useEffect(() => {
    if (!source) return;
    const image = new Image();
    image.onload = () => setMeasured({ src: source, size: { width: image.naturalWidth, height: image.naturalHeight } });
    image.onerror = () => setMeasured({ src: source, error: "이미지를 불러오지 못했습니다." });
    image.src = source;
    return () => { image.onload = image.onerror = null; };
  }, [source]);
  const size = measured?.src === source ? measured?.size : undefined;
  const error = (needsDetail ? result?.error : undefined) ?? (measured?.src === source ? measured?.error : undefined);
  const screen = displaySize(target.display);
  if (!screen) return <Notice>기기에서 화면 해상도를 확인한 뒤 다시 시도해 주세요.</Notice>;
  if (error) return <Notice>{error}</Notice>;
  if (!preview || !size) return <p className="explore-result-status" role="status"><Busy /> 적용 미리보기를 불러오는 중…</p>;
  return <WallpaperFrameControls key={source} picture={preview} target={target} size={size} screen={screen}
    busy={busy} confirm={confirm} fromExplore={fromExplore} />;
}

function WallpaperFrameControls({ picture, target, size, screen, fromExplore, busy, confirm }: {
  picture: Picture; target: Target; size: ScreenSize; screen: ScreenSize; fromExplore?: boolean;
  busy: boolean; confirm: (selection: WallpaperSelection) => Promise<void>;
}) {
  const crop = wallpaperCrop(picture, size);
  const [frame, setFrame] = useState(() => {
    const previous = picture.wallpaperFrame;
    return previous?.width === screen.width && previous.height === screen.height ? previous : defaultFrame(screen, crop);
  });
  const [mouseChoice, setMouseChoice] = useState<boolean>();
  const [testing, setTesting] = useState(false);
  const [testError, setTestError] = useState("");
  const [testInput] = useState(() => defaultDemoInput(matchMedia("(pointer: coarse)").matches,
    matchMedia("(any-pointer: fine)").matches, navigator.maxTouchPoints));
  const drag = useRef<{ id: number; x: number; y: number; frame: WallpaperFrame } | null>(null);
  const submitting = useRef(false);
  const layout = frameLayout(frame, crop);
  const edit = { ...defaultEdit, ...picture.edit, strength: motionStrength(picture.edit), motionDefaultsVersion: 1 };
  const { characterParts, layered } = wallpaperExportPolicy(picture, edit);
  const windows = target.platform === "Windows";
  const showMouseChoice = windows && layered && !fromExplore && !picture.isReadOnly;
  const mouseEnabled = mouseChoice ?? (edit.mouseEnabled || !!picture.legacyMotion);
  const rig = characterParts ? { ...normalizeRig(edit.rig), enabled: true } : edit.rig;
  const appliedPicture = { ...picture, edit: { ...edit, rig,
    mouseEnabled: showMouseChoice ? mouseEnabled : edit.mouseEnabled },
    legacyMotion: mouseChoice === undefined && picture.legacyMotion };
  const gyro = testInput === "gyro";
  const motion = useParallax(layered && testing && !busy, edit.strength, !gyro, gyro);
  const MotionIcon = gyro ? Smartphone : Mouse;

  useEffect(() => {
    if (!gyro || !motion.active) return;
    const timer = window.setTimeout(() => setTestError("센서 입력을 받지 못했습니다. 브라우저의 모션 센서 설정을 확인해 주세요."), 2500);
    function receive(event: DeviceOrientationEvent) {
      if (event.gamma === null || event.beta === null || !Number.isFinite(event.gamma) || !Number.isFinite(event.beta)) return;
      window.clearTimeout(timer);
      setTestError("");
      window.removeEventListener("deviceorientation", receive);
    }
    window.addEventListener("deviceorientation", receive);
    return () => { window.clearTimeout(timer); window.removeEventListener("deviceorientation", receive); };
  }, [gyro, motion.active]);

  async function toggleMotionTest() {
    setTestError("");
    if (testing) { setTesting(false); return; }
    try {
      if (gyro) await motion.requestGyro();
      setTesting(true);
    } catch (error) { setTestError((error as Error).message); }
  }
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
      await confirm({ picture: { ...appliedPicture, ...size }, deviceId: target.deviceId, displayId: target.displayId, frame });
    } finally { submitting.current = false; }
  }
  return <>
    <div className="wallpaper-frame-heading"><strong>{target.name}</strong><span>{screen.width} × {screen.height} px</span></div>
    <div className={`wallpaper-screen-frame${windows ? " wallpaper-screen-frame-pc" : ""}`} aria-label="대상 화면 미리보기" ref={motion.stage}
      style={{ aspectRatio: `${layout.width} / ${layout.height}`, width: `min(100%, ${36 * layout.width / layout.height}dvh)` }}
      onPointerDown={event => {
        if (busy || !event.isPrimary || event.button !== 0) return;
        drag.current = { id: event.pointerId, x: event.clientX, y: event.clientY, frame };
        event.currentTarget.setPointerCapture(event.pointerId);
      }} onPointerMove={move} onLostPointerCapture={() => { drag.current = null; }}>
      <FramedWallpaper picture={appliedPicture}
        size={size} frame={frame} motion={motion} rig={rig} />
    </div>
    <fieldset className="wallpaper-frame-settings" disabled={busy}>
      <legend>이미지 배치</legend>
      <div className="wallpaper-fit-options">
        <button type="button" className={`button ${frame.contain ? "secondary" : "primary"}`} aria-pressed={!frame.contain}
          onClick={() => setFrame({ ...frame, contain: false, zoom: 1 })}>화면 채우기</button>
        <button type="button" className={`button ${frame.contain ? "primary" : "secondary"}`} aria-pressed={frame.contain}
          onClick={() => setFrame({ ...frame, contain: true, zoom: 1 })}>초기화</button>
        <button type="button" className="button secondary wallpaper-motion-test" aria-pressed={testing}
          aria-busy={motion.gyroPending} disabled={!layered || motion.gyroPending} onClick={() => void toggleMotionTest()}>
          <MotionIcon size={18} aria-hidden="true" />움직임 테스트
        </button>
      </div>
      <label>확대 {Math.round(frame.zoom * 100)}%
        <input type="range" min="1" max="3" step="0.01" value={frame.zoom}
          onChange={event => setFrame({ ...frame, zoom: event.target.valueAsNumber })} />
      </label>
      <label>가로 위치
        <input type="range" min="0" max="1" step="0.01" value={layout.travelX < 0 ? 1 - frame.x : frame.x}
          disabled={Math.abs(layout.travelX) < 1}
          onChange={event => setFrame({ ...frame, x: layout.travelX < 0 ? 1 - event.target.valueAsNumber : event.target.valueAsNumber })} />
      </label>
      <label>세로 위치
        <input type="range" min="0" max="1" step="0.01" value={layout.travelY < 0 ? 1 - frame.y : frame.y}
          disabled={Math.abs(layout.travelY) < 1}
          onChange={event => setFrame({ ...frame, y: layout.travelY < 0 ? 1 - event.target.valueAsNumber : event.target.valueAsNumber })} />
      </label>
      {showMouseChoice && <label className="studio-rig-toggle">
        <input type="checkbox" checked={mouseEnabled} onChange={event => setMouseChoice(event.target.checked)} />
        <span>PC에서 마우스 움직임 적용</span>
      </label>}
    </fieldset>
    {testError && <p className="wallpaper-motion-status" role="status">{testError}</p>}
    {fromExplore && <Notice>이미지를 내 이미지에 저장한 뒤 적용 요청을 전송합니다.</Notice>}
    <button className="button primary full" disabled={busy} onClick={() => void submit()}>
      {busy ? <Busy /> : <Monitor size={18} />}{busy ? "적용 요청 전송 중…" : "이 배치로 적용"}
    </button>
  </>;
}
