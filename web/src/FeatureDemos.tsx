import { useEffect, useId, useState, type KeyboardEvent, type Ref } from "react";
import { BatteryFull, Check, ChevronsLeftRight, MousePointer2, Repeat2, Signal, Smartphone } from "lucide-react";
import { useAutomaticGyro, useParallax } from "./use-parallax";
import { useCharacterRig } from "./use-character-rig";
import { characterImage, portraitImage, portraitRig, showcaseCharacter, showcasePortrait, showcaseRig, type Live2DExample } from "./showcase-character";
import { defaultDemoInput } from "./demo-input";

function currentDemoInput() {
  return defaultDemoInput(window.matchMedia("(pointer: coarse)").matches,
    window.matchMedia("(any-pointer: fine)").matches, navigator.maxTouchPoints);
}

export function UpscaleDemo() {
  const [split, setSplit] = useState(45);
  const description = useId();
  return (
    <div className="upscale-demo">
      <div className="compare-art">
        <img src="/images/lake.jpg" alt="선명한 호수 풍경 미리보기" loading="lazy" />
        <div className="compare-blur" style={{ clipPath: `inset(0 ${100 - split}% 0 0)` }} aria-hidden="true">
          <img src="/images/lake.jpg" alt="" loading="lazy" />
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

function ShowcaseArtwork({ example, layersRef, onError }: {
  example: Live2DExample; layersRef: Ref<HTMLDivElement>; onError: () => void;
}) {
  const [loaded, setLoaded] = useState(0);
  const neckOpening = `${useId()}-neck-opening`;
  const portrait = example === "portrait";
  const artwork = portrait ? showcasePortrait : showcaseCharacter;
  const preparingPortrait = portrait && loaded < artwork.layers.length;

  return <>
    {!portrait && <div className="character-backdrop" aria-hidden="true"><span /></div>}
    {preparingPortrait && <img className="demo-portrait-fallback" src={portraitImage} width={artwork.width} height={artwork.height}
      alt="남성 인물 사진" loading="lazy" />}
    <div className="demo-character-plane">
      <div className={`demo-character-rig${portrait ? " demo-portrait-rig" : ""}`} ref={layersRef} role="img"
        aria-hidden={preparingPortrait || undefined}
        aria-label={portrait ? "얼굴·눈동자·머리카락 파츠가 독립적으로 움직이는 남성 인물"
          : "얼굴·눈동자·머리카락 파츠가 독립적으로 움직이는 여성 캐릭터"}
        style={{ aspectRatio: `${artwork.width} / ${artwork.height}`, visibility: preparingPortrait ? "hidden" : undefined }}>
        {!portrait && <svg className="character-clip-defs" width="0" height="0" aria-hidden="true" focusable="false">
          <defs><clipPath id={neckOpening} clipPathUnits="objectBoundingBox">
            <path d="M0 0H1V.42H.572C.568 .435 .548 .442 .514 .442C.485 .442 .464 .436 .458 .42H0Z" />
          </clipPath></defs>
        </svg>}
        {artwork.layers.map(layer => <img key={layer.file} src={layer.src} alt="" aria-hidden="true"
          width={artwork.width} height={artwork.height} loading="lazy"
          data-rig-part={layer.part} data-layer-name={layer.name}
          style={{ zIndex: layer.renderOrder, clipPath: !portrait && layer.name === "neck" ? `url("#${neckOpening}")` : undefined }}
          onLoad={portrait ? () => setLoaded(count => count + 1) : undefined} onError={onError} />)}
      </div>
    </div>
  </>;
}

type MotionDemoProps = { variant: "subject" } |
  { variant: "character"; example: Live2DExample; switchExample: () => void };

/** The same calibrated mouse/gyro input as the editor, applied to prepared demo artwork. */
export function MotionDemo(props: MotionDemoProps) {
  const { variant } = props;
  const example = props.variant === "character" ? props.example : "character";
  const [input, setInput] = useState(currentDemoInput);
  const [sensorMissing, setSensorMissing] = useState(false);
  const [inView, setInView] = useState(false);
  const [reduced, setReduced] = useState(() => window.matchMedia("(prefers-reduced-motion: reduce)").matches);
  const [keyboard, setKeyboard] = useState({ x: 0, y: 0 });
  const [failedExample, setFailedExample] = useState<Live2DExample>();
  const instruction = useId();
  const portrait = variant === "character" && example === "portrait";
  const artworkError = failedExample === example;
  const title = variant === "character" ? (portrait ? "Live 2D 실사 인물" : "Live 2D 캐릭터") : "배경·피사체 분리";
  const moving = !reduced;
  const motion = useParallax(moving && inView, 85, input === "mouse", input === "gyro", variant, keyboard, true);
  const error = useAutomaticGyro(input === "gyro" && moving, motion.gyroReady, motion.requestGyro);
  const rig = useCharacterRig(variant === "character" && moving && inView && !artworkError,
    portrait ? portraitRig : showcaseRig, example, motion.position);

  useEffect(() => {
    const preference = window.matchMedia("(prefers-reduced-motion: reduce)");
    const change = () => setReduced(preference.matches);
    preference.addEventListener("change", change);
    return () => preference.removeEventListener("change", change);
  }, []);

  useEffect(() => {
    const queries = [window.matchMedia("(pointer: coarse)"), window.matchMedia("(any-pointer: fine)")];
    const change = () => { setInput(currentDemoInput()); setKeyboard({ x: 0, y: 0 }); };
    queries.forEach(query => query.addEventListener("change", change));
    return () => queries.forEach(query => query.removeEventListener("change", change));
  }, []);

  useEffect(() => {
    const stage = motion.stage.current;
    if (!stage) return;
    const observer = new IntersectionObserver(entries => setInView(entries[0].isIntersecting));
    observer.observe(stage);
    return () => observer.disconnect();
  }, [motion.stage]);

  useEffect(() => {
    if (input !== "gyro" || !motion.gyroReady || !moving || !inView) return;
    setSensorMissing(false);
    const receive = (event: DeviceOrientationEvent) => {
      if (event.gamma === null || event.beta === null || !Number.isFinite(event.gamma) || !Number.isFinite(event.beta)) return;
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
  }, [input, motion.gyroReady, moving, inView]);

  function moveWithKeys(event: KeyboardEvent<HTMLDivElement>) {
    if (event.target !== event.currentTarget || !moving || !["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home"].includes(event.key)) return;
    event.preventDefault();
    if (event.key === "Home") { setKeyboard({ x: 0, y: 0 }); return; }
    const dx = event.key === "ArrowLeft" ? -.25 : event.key === "ArrowRight" ? .25 : 0;
    const dy = event.key === "ArrowUp" ? -.25 : event.key === "ArrowDown" ? .25 : 0;
    setKeyboard(current => ({ x: Math.max(-1, Math.min(1, current.x + dx)), y: Math.max(-1, Math.min(1, current.y + dy)) }));
  }

  const status = reduced ? "기기의 움직임 줄이기 설정에 따라 정지 화면을 표시합니다."
    : input === "gyro" ? error || (!motion.gyroReady ? "화면을 한 번 터치하고 기울기 센서 권한을 허용해 주세요."
      : sensorMissing ? "기울기 센서의 응답이 없어요. 브라우저의 동작·방향 접근 설정을 확인해 주세요." : "") : "";

  return (
    <div className={`motion-demo ${variant}-demo`} data-input={input}>
      <div className="demo-artwork">
        <div className="demo-stage" ref={element => { motion.stage.current = element; rig.stage.current = element; }} tabIndex={0} role="group"
          aria-label={`${title} 움직임 미리보기`} aria-describedby={instruction}
          onKeyDown={moveWithKeys} onBlur={() => setKeyboard({ x: 0, y: 0 })}>
          <div className="demo-planes" ref={motion.layers}>
            {variant === "subject" ? <>
              <img className="demo-background" src="/images/fox-background.png" alt="고정된 숲 배경" loading="lazy" />
              <div className="demo-subject-plane">
                <img src="/images/fox-foreground.png" alt="마우스와 기울기에 따라 움직이는 여우 피사체" loading="lazy" />
              </div>
            </> : <ShowcaseArtwork key={example} example={example} layersRef={rig.layers} onError={() => setFailedExample(example)} />}
          </div>
          {props.variant === "character" && <button type="button" className="demo-example-toggle" onClick={props.switchExample}>
            <Repeat2 size={17} aria-hidden="true" />{portrait ? "2D 캐릭터로 전환" : "실사 인물로 전환"}
          </button>}
        </div>
        <p id={instruction} className="floating-label demo-motion-hint">
          <span className="motion-hint-desktop"><MousePointer2 size={16} /><span>마우스를 움직여 보세요.</span></span>
          <span className="motion-hint-mobile"><Smartphone size={16} /><span>핸드폰을 기울여서 자이로센서를 이용해 보세요.</span></span>
        </p>
      </div>
      {variant === "character" && artworkError && <p className="form-error" role="alert">이미지 파츠를 불러오지 못했어요. 페이지를 새로고침해 주세요.</p>}
      {status && <p className="demo-status" role="status">{status}</p>}
    </div>
  );
}

export function DeviceShowcase({ example }: { example: Live2DExample }) {
  const portrait = example === "portrait";
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
      <div className="mock-phone android-phone">
        <div className={`phone-character-wallpaper${portrait ? " phone-portrait-wallpaper" : ""}`}>
          <img src={portrait ? portraitImage : characterImage} alt={portrait ? "Live 2D 실사 인물 배경화면이 적용된 휴대폰" : "Live 2D 캐릭터 배경화면이 적용된 휴대폰"} width="1024" height="1536" loading="lazy" />
        </div>
        <div className="phone-punch-hole" aria-hidden="true" />
        <div className="android-status-bar" aria-hidden="true"><span>5G</span><Signal size={9} /><BatteryFull size={10} /></div>
        <span className="android-lock-clock" aria-hidden="true">09:41</span>
        <div className="android-navigation" aria-hidden="true">
          <span className="android-recents" /><span className="android-home" /><span className="android-back" />
        </div>
      </div>
      <span className="floating-label"><Check size={16} /> 나의 화면, 하나의 공간에서</span>
    </div>
  );
}
