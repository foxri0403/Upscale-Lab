import { MousePointer2, RotateCcw } from "lucide-react";
import { normalizeRig } from "./character-rig";
import { defaultHeadRig, type HeadRig } from "./model";
import type { CharacterRigController } from "./use-character-rig";

export function CharacterMotionControls({ rig, onChange, disabled, stage }: {
  rig?: HeadRig; onChange: (rig: HeadRig) => void; disabled: boolean;
  stage: CharacterRigController;
}) {
  const value = normalizeRig(rig);
  function update<K extends keyof HeadRig>(key: K, next: HeadRig[K]) { onChange({ ...value, [key]: next }); }
  const settings: { key: "headTilt" | "eyeMove" | "hairSway" | "pivotX" | "pivotY"; label: string; hint: string }[] = [
    { key: "headTilt", label: "고개 기울기", hint: "얼굴과 눈을 함께 회전" },
    { key: "eyeMove", label: "눈동자 이동", hint: "눈 파츠만 미세 이동" },
    { key: "hairSway", label: "머리카락 흔들림", hint: "앞·뒷머리 지연 움직임" },
    { key: "pivotX", label: "고개 회전 중심 (가로)", hint: "얼굴 중심 위치에 맞춰 조정" },
    { key: "pivotY", label: "고개 회전 중심 (세로)", hint: "캐릭터에 따라 목 위치 조정" },
  ];
  return <section className="studio-rig-controls">
    <h3>움직임 미리보기</h3>
    <label className="studio-rig-toggle"><input type="checkbox" checked={value.enabled}
      disabled={disabled} onChange={event => update("enabled", event.target.checked)} />
      <span>고개 · 눈 · 머리카락 움직임 사용</span></label>
    <p className="studio-rig-helper">몸통·의상·팔은 고정하며 분리된 PNG 파츠만 회전·이동합니다.</p>
    <fieldset disabled={disabled || !value.enabled}>
      <h3>움직임 강도</h3>
      {settings.map(item => <label key={item.key} className="studio-rig-slider">
        <span><strong>{item.label}</strong><output>{value[item.key]}</output></span>
        <input type="range" min={0} max={100} step={1} value={value[item.key]}
          onChange={event => update(item.key, event.target.valueAsNumber)} />
        <small>{item.hint}</small>
      </label>)}
      <label className="studio-rig-toggle studio-rig-idle"><input type="checkbox" checked={value.idle}
        onChange={event => update("idle", event.target.checked)} /> <span>가만히 있을 때 미세 흔들림</span></label>
      <h3>움직임 테스트</h3>
      <label className="studio-rig-slider"><span><strong>고개 방향</strong><output>{stage.tilt}</output></span>
        <input type="range" min={-100} max={100} step={1} value={stage.tilt} disabled={stage.mouse}
          onChange={event => stage.setTilt(event.target.valueAsNumber)} /></label>
      <label className="studio-rig-slider"><span><strong>고개 상하</strong><output>{stage.tiltY}</output></span>
        <input type="range" min={-100} max={100} step={1} value={stage.tiltY} disabled={stage.mouse}
          onChange={event => stage.setTiltY(event.target.valueAsNumber)} /></label>
      <label className="studio-rig-slider"><span><strong>눈동자 방향</strong><output>{stage.look}</output></span>
        <input type="range" min={-100} max={100} step={1} value={stage.look} disabled={stage.mouse}
          onChange={event => stage.setLook(event.target.valueAsNumber)} /></label>
      <label className="studio-rig-slider"><span><strong>눈동자 상하</strong><output>{stage.lookY}</output></span>
        <input type="range" min={-100} max={100} step={1} value={stage.lookY} disabled={stage.mouse}
          onChange={event => stage.setLookY(event.target.valueAsNumber)} /></label>
      <button className="button secondary full studio-rig-mouse" type="button" aria-pressed={stage.mouse}
        onClick={() => stage.setMouse(!stage.mouse)}><MousePointer2 size={16}/>{stage.mouse ? "마우스 따라 움직임 켜짐" : "마우스로 움직임 테스트"}</button>
      <button className="button secondary full studio-rig-reset" type="button"
        onClick={() => { stage.setTilt(0); stage.setTiltY(0); stage.setLook(0); stage.setLookY(0); stage.setMouse(false); onChange({ ...defaultHeadRig, enabled: true }); }}>
        <RotateCcw size={15}/> 움직임 기본값</button>
    </fieldset>
  </section>;
}
