import { useEffect, useId, useState, type CSSProperties, type KeyboardEvent } from "react";
import { Check, ChevronsLeftRight, MousePointer2, Pause, Play, Smartphone } from "lucide-react";
import { useParallax } from "./use-parallax";

const characterImage = "/images/live2d-character-v2.png";

export function UpscaleDemo() {
  const [split, setSplit] = useState(45);
  const description = useId();
  return (
    <div className="upscale-demo">
      <div className="compare-art">
        <img src="/images/ocean.jpg" alt="선명한 바다 풍경 미리보기" loading="lazy" />
        <div className="compare-blur" style={{ clipPath: `inset(0 ${100 - split}% 0 0)` }} aria-hidden="true">
          <img src="/images/ocean.jpg" alt="" loading="lazy" />
        </div>
        <div className="compare-line" style={{ left: `${split}%` }} aria-hidden="true">
          <span><ChevronsLeftRight size={21} /></span>
        </div>
        <span className="compare-label before" aria-hidden="true">저해상도 예시</span>
        <span className="compare-label after" aria-hidden="true">업스케일 미리보기</span>
        <input type="range" min="0" max="100" step="1" value={split}
          onChange={event => setSplit(Number(event.target.value))}
          aria-label="업스케일 전후 비교" aria-describedby={description}
          aria-valuetext={`저해상도 ${split}%, 고해상도 ${100 - split}%`} />
      </div>
      <p id={description} className="demo-caption">슬라이더를 좌우로 움직여 비교해 보세요. 소개용 비교 예시입니다.</p>
    </div>
  );
}

/** The same calibrated mouse/gyro input as the editor, applied to prepared demo artwork. */
export function MotionDemo({ variant }: { variant: "subject" | "character" }) {
  const [enabled, setEnabled] = useState(true);
  const [gyro, setGyro] = useState(false);
  const [sensorReceived, setSensorReceived] = useState(false);
  const [sensorMissing, setSensorMissing] = useState(false);
  const [error, setError] = useState("");
  const [inView, setInView] = useState(false);
  const [reduced, setReduced] = useState(() => window.matchMedia("(prefers-reduced-motion: reduce)").matches);
  const [keyboard, setKeyboard] = useState({ x: 0, y: 0 });
  const instruction = useId();
  const title = variant === "character" ? "Live 2D 캐릭터" : "배경·피사체 분리";
  const moving = enabled && !reduced;
  const motion = useParallax(moving && inView, variant === "character" ? 65 : 85, true, gyro, variant);

  useEffect(() => {
    const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
    const change = () => setReduced(preference.matches);
    preference.addEventListener("change", change);
    return () => preference.removeEventListener("change", change);
  }, []);

  useEffect(() => {
    const stage = motion.stage.current;
    if (!stage) return;
    const observer = new IntersectionObserver(entries => setInView(entries[0].isIntersecting));
    observer.observe(stage);
    return () => observer.disconnect();
  }, [motion.stage]);

  useEffect(() => {
    if (!gyro || !moving || !inView) return;
    setSensorReceived(false);
    setSensorMissing(false);
    const receive = (event: DeviceOrientationEvent) => {
      if (event.gamma === null || event.beta === null || !Number.isFinite(event.gamma) || !Number.isFinite(event.beta)) return;
      setSensorReceived(true);
      setSensorMissing(false);
      window.removeEventListener("deviceorientation", receive);
      window.clearTimeout(timeout);
    };
    const timeout = window.setTimeout(() => setSensorMissing(true), 4000);
    window.addEventListener("deviceorientation", receive);
    return () => {
      window.clearTimeout(timeout);
      window.removeEventListener("deviceorientation", receive);
    };
  }, [gyro, moving, inView]);

  async function toggleGyro() {
    setError("");
    if (gyro) { setGyro(false); return; }
    try {
      await motion.requestGyro();
      setGyro(true);
    } catch (failure) {
      setError((failure as Error).message);
    }
  }

  function moveWithKeys(event: KeyboardEvent<HTMLDivElement>) {
    if (!moving || !["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home"].includes(event.key)) return;
    event.preventDefault();
    if (event.key === "Home") { setKeyboard({ x: 0, y: 0 }); return; }
    const dx = event.key === "ArrowLeft" ? -4 : event.key === "ArrowRight" ? 4 : 0;
    const dy = event.key === "ArrowUp" ? -4 : event.key === "ArrowDown" ? 4 : 0;
    setKeyboard(current => ({ x: Math.max(-12, Math.min(12, current.x + dx)), y: Math.max(-12, Math.min(12, current.y + dy)) }));
  }

  const status = reduced ? "기기의 움직임 줄이기 설정에 따라 정지 화면을 표시합니다."
    : !enabled ? "움직임을 멈췄어요. 다시 켜서 체험해 보세요."
    : error || (gyro ? sensorMissing ? "센서 응답이 없어요. 센서를 지원하는 기기에서 이용하거나 마우스로 체험해 보세요."
      : sensorReceived ? "현재 기울기를 기준으로 기기를 천천히 움직여 보세요."
      : "기울기 센서를 기다리고 있어요. 기기를 천천히 움직여 보세요."
      : "휴대폰에서는 자이로 켜기를 누르고 기기를 기울여 보세요.");

  return (
    <div className={`motion-demo ${variant}-demo`}>
      <div className="demo-stage" ref={motion.stage} tabIndex={0} role="group"
        aria-label={`${title} 움직임 미리보기`} aria-describedby={instruction}
        onKeyDown={moveWithKeys} onBlur={() => setKeyboard({ x: 0, y: 0 })}>
        <div className="demo-planes" ref={motion.layers} style={{
          "--key-x": `${moving ? keyboard.x : 0}px`, "--key-y": `${moving ? keyboard.y : 0}px`,
        } as CSSProperties}>
          {variant === "subject" ? <>
            <img className="demo-background" src="/images/fox-background.png" alt="고정된 숲 배경" loading="lazy" />
            <div className="demo-subject-plane">
              <img src="/images/fox-foreground.png" alt="마우스와 기울기에 따라 움직이는 여우 피사체" loading="lazy" />
            </div>
          </> : <>
            <div className="character-backdrop" aria-hidden="true"><span /></div>
            <div className="demo-character-plane">
              <img src={characterImage} alt="마우스와 기울기에 반응하는 여성 버튜버 캐릭터" width="1024" height="1536" loading="lazy" />
            </div>
          </>}
        </div>
        <span className="demo-stage-tag" aria-hidden="true">{variant === "character" ? "LIVE 2D" : "BACKGROUND / SUBJECT"}</span>
      </div>
      <div className="demo-toolbar">
        <p id={instruction}><MousePointer2 size={15} /> 마우스 또는 방향키로 움직여 보세요</p>
        <div className="demo-actions">
          <button type="button" aria-label={`${title} 움직임 ${enabled ? "끄기" : "켜기"}`}
            aria-pressed={enabled} disabled={reduced} onClick={() => { setEnabled(!enabled); setKeyboard({ x: 0, y: 0 }); }}>
            {enabled ? <Pause size={14} /> : <Play size={14} />}{enabled ? "멈추기" : "움직이기"}
          </button>
          <button type="button" aria-label={`${title} 자이로 ${gyro ? "끄기" : "켜기"}`}
            aria-pressed={gyro} disabled={!moving || motion.gyroPending} onClick={() => void toggleGyro()}>
            <Smartphone size={14} />{motion.gyroPending ? "권한 확인 중…" : gyro ? "자이로 끄기" : "자이로 켜기"}
          </button>
        </div>
      </div>
      <p className="demo-status" role="status">{status}</p>
    </div>
  );
}

export function DeviceShowcase() {
  return (
    <div className="devices-art showcase-devices">
      <div className="mock-monitor">
        <img src="/images/lake.jpg" alt="푸른 호수 배경화면이 적용된 PC" loading="lazy" />
        <div className="monitor-foot" />
      </div>
      <div className="mock-tablet">
        <img src="/images/fox.png" alt="여우 배경화면이 적용된 태블릿" loading="lazy" />
        <span className="tablet-camera" aria-hidden="true" />
      </div>
      <div className="mock-phone">
        <div className="phone-character-wallpaper">
          <img src={characterImage} alt="Live 2D 버튜버 캐릭터 배경화면이 적용된 휴대폰" width="1024" height="1536" loading="lazy" />
        </div>
        <div className="phone-notch" />
        <span aria-hidden="true">09:41</span>
      </div>
      <span className="floating-label"><Check size={16} /> 나의 화면, 하나의 공간에서</span>
    </div>
  );
}
