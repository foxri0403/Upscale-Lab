import { MousePointer2, Smartphone } from "lucide-react";
import type { Edit } from "./model";

export function LayerMotionControls({ edit, ready, change, gyroPending, toggleGyro }: {
  edit: Edit; ready: boolean; change: <K extends keyof Edit>(key: K, value: Edit[K]) => void;
  gyroReady: boolean; gyroPending: boolean; toggleGyro: () => void;
}) {
  const strength = (value: number) => change("strength", Math.max(0, Math.min(100, Math.round(Number.isFinite(value) ? value : 0))));
  return <>
    <h3>움직임 강도</h3>
    <div className="range-field">
      <input aria-label="움직임 강도" type="range" min="0" max="100" step="1"
        value={edit.strength} disabled={!ready} onChange={event => strength(event.target.valueAsNumber)} />
      <input aria-label="움직임 강도 숫자" className="motion-strength-number" type="number" inputMode="numeric"
        min="0" max="100" step="1" value={edit.strength} disabled={!ready}
        onChange={event => strength(event.target.valueAsNumber)} />
    </div>
    <h3>움직임 사용</h3>
    <div className="motion-input-options" role="group" aria-label="움직임 사용">
      <button type="button" aria-pressed={!!edit.mouseEnabled} disabled={!ready}
        onClick={() => change("mouseEnabled", !edit.mouseEnabled)}>
        <MousePointer2 size={19} aria-hidden="true" /><span>마우스</span>
      </button>
      <button type="button" aria-pressed={!!edit.gyroEnabled} disabled={!ready || gyroPending}
        aria-busy={gyroPending} onClick={toggleGyro}>
        <Smartphone size={19} aria-hidden="true" /><span>자이로센서</span>
      </button>
    </div>
  </>;
}
