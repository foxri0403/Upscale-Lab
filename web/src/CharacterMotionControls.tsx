import { useId, useState } from "react";
import { RotateCcw, Settings2 } from "lucide-react";
import { normalizeRig } from "./character-rig";
import { defaultHeadRig, type HeadRig } from "./model";

const headSettings = [
  ["headTilt", "고개 기울기"],
  ["eyeMove", "눈동자 이동"],
  ["hairSway", "머리카락 흔들림"],
  ["pivotX", "머리 회전 중심 (가로)"],
  ["pivotY", "머리 회전 중심 (세로)"],
] as const;
const upperSettings = [
  ["upperBody", "상체 연동 강도"],
  ["torso", "몸통·의상"],
  ["leftArm", "왼팔"],
  ["rightArm", "오른팔"],
  ["leftHand", "왼손"],
  ["rightHand", "오른손"],
  ["torsoPivotY", "상체 회전 중심 (세로)"],
] as const;
const lowerSettings = [
  ["lowerBody", "하체 연동 강도"],
  ["leftLeg", "왼다리"],
  ["rightLeg", "오른다리"],
  ["lowerPivotY", "하체 회전 중심 (세로)"],
] as const;
type SliderKey = (typeof headSettings | typeof upperSettings | typeof lowerSettings)[number][0];

function RigSetting({ label, value, change }: { label: string; value: number; change: (value: number) => void }) {
  const id = useId();
  const [text, setText] = useState<string | null>(null);
  return <div className="studio-rig-slider">
    <div>
      <label htmlFor={id}>{label}</label>
      <input aria-label={`${label} 숫자`} type="number" inputMode="numeric" min={0} max={100} step={1}
        onKeyDown={event => { if (event.key === "Enter") event.currentTarget.blur(); }}
        value={text ?? value} onChange={event => {
          setText(event.currentTarget.value);
          const number = event.currentTarget.valueAsNumber;
          if (Number.isInteger(number) && number >= 0 && number <= 100) change(number);
        }} onBlur={event => {
          const number = event.currentTarget.valueAsNumber;
          if (Number.isFinite(number)) change(Math.min(100, Math.max(0, Math.round(number))));
          setText(null);
        }} />
    </div>
    <input id={id} type="range" min={0} max={100} step={1} value={value}
      onChange={event => { setText(null); change(event.currentTarget.valueAsNumber); }} />
  </div>;
}

export function CharacterMotionControls({ rig, onChange, disabled }: {
  rig?: HeadRig; onChange: (rig: HeadRig) => void; disabled: boolean;
}) {
  const value = normalizeRig(rig);
  const follows = value.bodyMode === "follow";
  const [details, setDetails] = useState<"face" | "body" | null>(null);
  const detailsId = useId();
  function sliders(items: ReadonlyArray<readonly [SliderKey, string]>) {
    return items.map(([key, label]) => <RigSetting key={key} label={label} value={value[key]}
      change={next => { if (next !== value[key]) onChange({ ...value, [key]: next }); }} />);
  }
  return <section className="studio-rig-controls">
    <h3>레이어 움직임</h3>
    <fieldset disabled={disabled}>
      {([
        ["face", "얼굴", value.faceEnabled, () => onChange({ ...value, faceEnabled: !value.faceEnabled })],
        ["body", "몸", follows, () => onChange({ ...value, bodyMode: follows ? "fixed" : "follow" })],
      ] as const).map(([part, label, applied, toggle]) => <div key={part}>
        <div className="studio-rig-actions">
          <button className="button secondary" type="button" onClick={toggle}>
            {label} 반응 {applied ? "제거" : "추가"}
          </button>
          <button className="button secondary" type="button" aria-label={`${label} 반응 세부 설정`}
            aria-expanded={details === part} aria-controls={`${detailsId}-${part}`}
            onClick={() => setDetails(details === part ? null : part)}>
            <Settings2 size={15} /> 세부 설정
          </button>
        </div>
        <div className="studio-rig-details" id={`${detailsId}-${part}`} hidden={details !== part}>
          {part === "face" ? sliders(headSettings) : <>
            <h3>상체 움직임</h3>{sliders(upperSettings)}
            <h3>하체 움직임</h3>{sliders(lowerSettings)}
          </>}
        </div>
      </div>)}
      <button className="button secondary full studio-rig-reset" type="button"
        onClick={() => onChange({ ...defaultHeadRig, enabled: value.enabled, faceEnabled: value.faceEnabled,
          bodyMode: value.bodyMode, partAssignments: value.partAssignments })}>
        <RotateCcw size={15} /> 움직임 기본값
      </button>
    </fieldset>
  </section>;
}
