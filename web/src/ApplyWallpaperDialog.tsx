import { useState } from "react";
import { Monitor } from "lucide-react";
import type { Device, Picture } from "./model";
import { wallpaperTargets } from "./explore-actions";
import { Busy, Empty, Link, Modal, Notice } from "./ui";

export function ApplyWallpaperDialog({ picture, devices, busy, close, confirm }: {
  picture: Picture;
  devices: Device[];
  busy: boolean;
  close: () => void;
  confirm: (deviceId: string, displayId: string) => void;
}) {
  const targets = wallpaperTargets(devices);
  const [targetId, setTargetId] = useState(targets[0]?.id || "");
  const selected = targets.find(target => target.id === targetId);
  return <Modal title="배경화면 적용" close={() => { if (!busy) close(); }}>
    <img className="share-preview" src={picture.src} alt={picture.name} />
    {targets.length ? <>
      <label>기기 및 화면
        <select value={selected?.id || ""} disabled={busy} onChange={event => setTargetId(event.target.value)}>
          <option value="" disabled>적용할 화면을 선택해 주세요</option>
          {targets.map(target => <option key={target.id} value={target.id}>{target.name}</option>)}
        </select>
      </label>
      <Notice>선택한 이미지를 내 계정으로 가져와 적용 요청을 전송합니다. 실제 적용 완료는 연결 앱의 보고 후 확인할 수 있습니다.</Notice>
      <button className="button primary full" disabled={busy || !selected} onClick={() => {
        if (selected) confirm(selected.deviceId, selected.displayId);
      }}>
        {busy ? <Busy /> : <Monitor size={18} />}{busy ? "적용 요청 전송 중…" : "적용 요청 저장"}
      </button>
    </> : <Empty text="연결된 기기가 없어요">
      <p>연결 앱에 로그인한 뒤 적용할 화면을 확인해 주세요.</p>
      <Link to="/app/devices" className="button secondary">기기 관리로 이동</Link>
    </Empty>}
  </Modal>;
}
