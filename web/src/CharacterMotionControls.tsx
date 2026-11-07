import { MousePointer2, RotateCcw } from "lucide-react";
import { assignableRigParts, normalizeRig, rigPart, type RigPart } from "./character-rig";
import { defaultHeadRig, type HeadRig } from "./model";
import type { CharacterRigController } from "./use-character-rig";
import type { ApiLayer } from "./api";
import { layerLabel } from "./layer-label";

const headSettings = [
  { key: "headTilt", label: "고개 기울기", hint: "얼굴과 머리 파츠 회전" },
  { key: "eyeMove", label: "눈동자 이동", hint: "눈 파츠 이동" },
  { key: "hairSway", label: "머리카락 흔들림", hint: "앞·뒷머리 지연 움직임" },
  { key: "pivotX", label: "머리 회전 중심 (가로)", hint: "원본 PNG 캔버스 기준" },
  { key: "pivotY", label: "머리 회전 중심 (세로)", hint: "목 주변으로 맞춰 조정" },
] as const;
const upperSettings = [
  { key: "upperBody", label: "상체 연동 강도", hint: "고개 움직임에 대한 상체 전체의 반응" },
  { key: "torso", label: "몸통·의상", hint: "상체 중심 움직임" },
  { key: "leftArm", label: "왼팔", hint: "몸통에 이어서 움직임" },
  { key: "rightArm", label: "오른팔", hint: "몸통에 이어서 움직임" },
  { key: "leftHand", label: "왼손", hint: "왼팔 움직임에 이어 반응" },
  { key: "rightHand", label: "오른손", hint: "오른팔 움직임에 이어 반응" },
  { key: "torsoPivotY", label: "상체 회전 중심 (세로)", hint: "목 또는 어깨 부근으로 조정" },
] as const;
const lowerSettings = [
  { key: "lowerBody", label: "하체 연동 강도", hint: "움직임이 과도하지 않도록 낮게 설정 권장" },
  { key: "leftLeg", label: "왼다리", hint: "왼다리 PNG가 있을 때만 독립적으로 작동" },
  { key: "rightLeg", label: "오른다리", hint: "오른다리 PNG가 있을 때만 독립적으로 작동" },
  { key: "lowerPivotY", label: "하체 회전 중심 (세로)", hint: "허리 근처에 맞춰 조정" },
] as const;
type SliderKey = typeof headSettings[number]["key"] | typeof upperSettings[number]["key"] | typeof lowerSettings[number]["key"];
export function CharacterMotionControls({ rig, onChange, disabled, stage, layers = [] }: {
  rig?: HeadRig; onChange: (rig: HeadRig) => void; disabled: boolean;
  stage: CharacterRigController; layers?: ApiLayer[];
}) {
  const value = normalizeRig(rig);
  function update<K extends keyof HeadRig>(key: K, next: HeadRig[K]) { onChange({ ...value, [key]: next }); }
  function sliders(items: ReadonlyArray<{ key: SliderKey; label: string; hint: string }>) {
    return items.map(item => <label key={item.key} className="studio-rig-slider">
      <span><strong>{item.label}</strong><output>{value[item.key]}</output></span>
      <input type="range" min={0} max={100} step={1} value={value[item.key]}
        onChange={event => update(item.key, event.target.valueAsNumber)} />
      <small>{item.hint}</small>
    </label>);
  }
  return <section className="studio-rig-controls">
    <h3>캐릭터 움직임</h3>
    <label className="studio-rig-toggle"><input type="checkbox" checked={value.enabled}
      disabled={disabled} onChange={event => update("enabled", event.target.checked)} />
      <span>PNG 파츠 움직임 사용</span></label>
    <p className="studio-rig-helper">실제 분리된 PNG 파츠만 움직입니다. 파츠가 몸통과 합쳐져 있다면 팔·손·다리를 따로 움직일 수 없습니다.</p>
    <fieldset disabled={disabled || !value.enabled}>
      <h3>머리·얼굴</h3>
      {sliders(headSettings)}
      <h3>몸 반응 방식</h3>
      <div className="studio-rig-mode" role="group" aria-label="몸 움직임 방식">
        <label><input type="radio" name="studio-body-mode" checked={value.bodyMode === "fixed"}
          onChange={() => update("bodyMode", "fixed")} /> 몸 고정</label>
        <label><input type="radio" name="studio-body-mode" checked={value.bodyMode === "follow"}
          onChange={() => update("bodyMode", "follow")} /> 고개 방향으로 몸 연동</label>
      </div>
      {value.bodyMode === "follow" && <>
        <h3>상체 움직임</h3>{sliders(upperSettings)}
        <h3>하체 움직임</h3>{sliders(lowerSettings)}
      </>}
      <h3>분리된 PNG 파츠 역할 지정</h3>
      <p className="studio-rig-helper">실제 레이어를 부위별로 지정해 주세요. See-through는 팔·손·다리를 항상 개별 PNG로 분리하지 않습니다.</p>
      <div className="studio-rig-assignments">
        {layers.map((layer, index) => <label key={layer.id}>
          <span title={layer.layerType}>{layerLabel(layer.layerType)} #{index + 1}</span>
          <select aria-label={`${layerLabel(layer.layerType)} ${index + 1} 움직임 부위`}
            value={rigPart(layer.layerType, layer.id, value.partAssignments)}
            onChange={event => {
              const part = event.target.value as RigPart;
              update("partAssignments", { ...value.partAssignments, [layer.id]: part });
            }}>
            {assignableRigParts.map(part => <option key={part.value} value={part.value}>{part.label}</option>)}
          </select>
        </label>)}
      </div>
      <label className="studio-rig-toggle studio-rig-idle"><input type="checkbox" checked={value.idle}
        onChange={event => update("idle", event.target.checked)} /> <span>가만히 있을 때 미세 흔들림</span></label>
      <h3>움직임 테스트</h3>
      {([
        ["고개 방향", "tilt", "setTilt"], ["고개 상하", "tiltY", "setTiltY"],
        ["눈동자 방향", "look", "setLook"], ["눈동자 상하", "lookY", "setLookY"],
      ] as const).map(([label, field, setter]) => <label key={field} className="studio-rig-slider">
        <span><strong>{label}</strong><output>{stage[field]}</output></span>
        <input type="range" min={-100} max={100} step={1} value={stage[field]} disabled={stage.mouse}
          onChange={event => stage[setter](event.target.valueAsNumber)} />
      </label>)}
      <button className="button secondary full studio-rig-mouse" type="button" aria-pressed={stage.mouse}
        onClick={() => stage.setMouse(!stage.mouse)}><MousePointer2 size={16}/>{stage.mouse ? "마우스 따라 움직임 켜짐" : "마우스로 움직임 테스트"}</button>
      <button className="button secondary full studio-rig-reset" type="button"
        onClick={() => { stage.setTilt(0); stage.setTiltY(0); stage.setLook(0); stage.setLookY(0); stage.setMouse(false); onChange({ ...defaultHeadRig, partAssignments: {}, enabled: true }); }}>
        <RotateCcw size={15}/> 움직임 기본값</button>
    </fieldset>
  </section>;
}
