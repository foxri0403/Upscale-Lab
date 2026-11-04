import { useState } from "react";
import {
  Monitor,
  Smartphone,
  Tablet,
  ChevronDown,
  RefreshCw,
  Unplug,
  Link2,
  Clock3,
} from "lucide-react";
import { useDemo } from "./Workspace";
import { sampleImages, uuid, type Device, type Picture } from "./model";
import { Modal, Notice, Empty, PictureView } from "./ui";
import { cloud, post, request } from "./api";
import { deviceDisplays, registeredDevices } from "./device-layout";

export function Devices() {
  const { library, session, update, toast, apply, refresh, refreshDevices } = useDemo();
  const [target, setTarget] = useState<{
    device: Device;
    displayId: string;
  } | null>(null);
  const [selected, setSelected] = useState("");
  const [removing, setRemoving] = useState<Device | null>(null);
  const [busy, setBusy] = useState(false);
  const [refreshing, setRefreshing] = useState<string | null>(null);
  const [clearing, setClearing] = useState<{ device: Device; displayId: string; requestId: string } | null>(null);
  const allPictures = [
    ...(library.assets || []),
    ...library.images,
    ...(!cloud ? sampleImages() : []),
  ];
  const findPicture = (id: string) => allPictures.find((p) => p.id === id);
  async function sync(device: Device) {
    if (cloud) {
      setRefreshing(device.id);
      try { await refreshDevices(); toast("기기 상태를 새로 불러왔습니다."); }
      catch (error) { toast((error as Error).message); }
      finally { setRefreshing(null); }
      return;
    }
    if (
      update((v) => ({
        ...v,
        devices: v.devices.map((d) =>
          d.id === device.id
            ? {
                ...d,
                online: true,
                displays: d.displays.map((s) =>
                  s.removing ? { ...s, current: "", desired: undefined, removing: false, observation: "Cleared" } : s.desired
                    ? { ...s, current: s.desired, desired: undefined }
                    : s,
                ),
              }
            : d,
        ),
      }))
    )
      toast("샘플 동기화를 완료했습니다. 실제 기기에는 적용하지 않습니다.");
  }
  async function remove(device: Device) {
    if (cloud) {
      try {
        await request(`/api/devices/${device.id}`, { method: "DELETE" });
        await refresh();
        setRemoving(null);
        toast("기기 연결을 해제했습니다.");
      } catch (e) {
        toast((e as Error).message);
      }
      return;
    }
    if (
      update((v) => ({
        ...v,
        devices: v.devices.filter((d) => d.id !== device.id),
      }))
    ) {
      setRemoving(null);
      toast("샘플 기기 연결을 해제했습니다.");
    }
  }
  function choose(device: Device, displayId: string) {
    setTarget({ device, displayId });
    setSelected(library.images[0]?.id || "");
  }
  async function clearWallpaper() {
    if (!clearing || busy) return;
    const display = clearing.device.displays.find(s => s.id === clearing.displayId);
    if (!display) return;
    setBusy(true);
    try {
      if (cloud) {
        await post(`/api/devices/${clearing.device.id}/wallpapers/clear`, {
          requestId: clearing.requestId, displayId: display.nativeId,
          target: display.target || (clearing.device.platform === "Windows" ? "Desktop" : "Home"),
        });
        await refreshDevices();
      } else update(value => ({ ...value,
        devices: value.devices.map(d => d.id === clearing.device.id ? { ...d, displays: d.displays.map(s => s.id === display.id ? { ...s, desired: undefined, removing: true } : s) } : d),
      }));
      setClearing(null); toast(cloud ? "기기에 배경화면 삭제 요청을 보냈습니다." : "샘플 배경화면 삭제 요청을 저장했습니다.");
    } catch (error) { toast((error as Error).message); }
    finally { setBusy(false); }
  }
  return (
    <section className="devices-page">
      <div className="page-heading">
        <div>
          <h1>
            기기 관리{" "}
            <span className="heading-count">{library.devices.length}대</span>
          </h1>
          <p>내 기기의 배경화면을 한곳에서 관리하세요.</p>
        </div>
      </div>
      <div className="account-device-note">
        <Link2 size={15} />
        <span>
          {session.name} 계정에 연결된 {cloud ? "기기" : "샘플 기기"}
        </span>
      </div>
      {!library.devices.length ? (
        <Empty text="연결된 기기가 없어요" />
      ) : (
        <div className="device-grid">
          {registeredDevices(library.devices).map((device) => {
            const mobile = device.platform === "Android";
            const tablet = mobile && device.formFactor === "tablet";
            const DeviceIcon = tablet ? Tablet : mobile ? Smartphone : Monitor;
            const connection = device.connection ?? (device.online ? "connected" : "disconnected");
            const displayConnection = device.displays.some(display => display.connected) ? connection : "disconnected";
            const screens = device.physicalDisplayCount ?? (mobile ? Math.min(1, device.displays.filter(d => d.connected).length)
              : new Set(device.displays.filter(d => d.connected).map(d => d.nativeId ?? d.id)).size);
            return (
              <article
                key={device.id}
                className={`device-card ${mobile ? "mobile-device" : "pc-device"} ${tablet ? "tablet-device" : ""} ${screens >= 3 ? "device-many-screens" : screens === 2 ? "device-two-screens" : "device-one-screen"}`}
              >
                <div className="device-card-header">
                  <span className="device-icon">
                    <DeviceIcon size={28} />
                  </span>
                  <div>
                    <h2>{device.name}</h2>
                    <p>
                      {cloud
                        ? device.osVersion || device.platform
                        : device.platform + (mobile ? "" : " 11")}
                      {` · 화면 ${screens}개`}
                    </p>
                  </div>
                  <button className="icon-button device-refresh" disabled={!!refreshing}
                    aria-label={`${device.name} 상태 새로고침`} title="상태 새로고침" onClick={() => void sync(device)}>
                    <RefreshCw size={15} className={refreshing === device.id ? "spin" : ""} />
                  </button>
                  <details className="device-more">
                    <summary aria-label={`${device.name} 관리`}>
                      <ChevronDown size={17} />
                    </summary>
                    <div>
                      <button
                        className="danger-text"
                        onClick={() => setRemoving(device)}
                      >
                        <Unplug size={15} /> 연결 해제
                      </button>
                    </div>
                  </details>
                </div>
                {!device.displays.length && (
                  <Notice>
                    <span className={`device-connection device-connection-${connection}`}>{connection === "connected" ? "연결됨" : connection === "connecting" ? "연결중" : "연결안됨"}</span>{" "}
                    연결 앱의 첫 보고를 기다리고 있습니다. 연결 앱에서 웹과 같은 계정으로 로그인해 주세요.
                  </Notice>
                )}
                <div className={`device-displays ${mobile ? "mobile-display-group" : ""}`}>
                  {mobile && device.displays.length > 0 && (
                    <span className={`device-connection device-connection-${displayConnection} mobile-device-connection`}>
                      {displayConnection === "connected" ? "연결됨" : displayConnection === "connecting" ? "연결중" : "연결안됨"}
                    </span>
                  )}
                  <div className="display-grid">
                  {deviceDisplays(device)
                    .map((display, i) => {
                      const current = findPicture(display.current);
                      const desired = display.desired
                        ? findPicture(display.desired)
                        : null;
                      return (
                        <div className="display-card" key={display.id}>
                          <div className="display-label">
                            {!mobile && (display.isPrimary ?? i === 0) && <span className="primary-display">주 화면</span>}
                            <div className="display-name">
                              <h3>{display.name}</h3>
                            </div>
                            {!mobile && <span
                              className={
                                `device-connection device-connection-${display.connected ? connection : "disconnected"}`
                              }
                            >
                              {!display.connected || connection === "disconnected" ? "연결안됨" : connection === "connecting" ? "연결중" : "연결됨"}
                            </span>}
                          </div>
                          <div className="display-preview">
                            <div
                              className={
                                mobile && display.canApplyWallpaper !== false ? (tablet ? "tablet-wallpaper" : "phone-wallpaper") : "monitor-wallpaper"
                              }
                            >
                              {current ? (
                                <PictureView picture={current} />
                              ) : (
                                <div className="unknown-wallpaper">
                                  {mobile && display.canApplyWallpaper !== false ? <DeviceIcon size={30} /> : <Monitor size={30} />}
                                  <span>{display.observation === "Cleared" ? "기본 바탕화면" : "이미지 정보 없음"}</span>
                                </div>
                              )}
                              {mobile && current && display.target === "Lock" && (
                                <span className="lock-clock">09:41</span>
                              )}
                            </div>
                          </div>
                          <p className="display-resolution">
                            {display.resolution}
                            {display.refreshRateHz != null && ` · ${Number(display.refreshRateHz.toFixed(2))} Hz`}
                          </p>
                          <p className="display-wallpaper-title" title={current?.name || "기본 바탕화면"}>
                            {current?.name || "기본 바탕화면"}
                          </p>
                          {desired && (
                            <div className="pending-wallpaper">
                              <Clock3 size={14} /> 적용 대기: {desired.name}
                            </div>
                          )}
                          {display.removing && <div className="pending-wallpaper"><Clock3 size={14} /> 배경화면 삭제 대기</div>}
                          <button
                            className="button secondary full"
                            disabled={
                              !library.images.length || !display.connected || display.canApplyWallpaper === false
                            }
                            onClick={() => choose(device, display.id)}
                          >
                            {display.canApplyWallpaper === false ? "정보 조회 전용 화면" : "배경화면 변경"}
                          </button>
                          <button className="button secondary full wallpaper-remove" disabled={busy || !display.connected || display.canApplyWallpaper === false || (cloud && !device.supportsWallpaperRemoval)}
                            title={cloud && !device.supportsWallpaperRemoval ? "연결 앱을 최신 버전으로 업데이트해 주세요." : undefined}
                            onClick={() => setClearing({ device, displayId: display.id, requestId: uuid() })}>
                            배경화면 삭제
                          </button>
                        </div>
                      );
                    })}
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      )}
      {target && (
        <Modal
          title={`${target.device.name} 배경화면 변경`}
          close={() => setTarget(null)}
        >
          {library.images.length ? (
            <>
              <label>
                이미지 선택
                <select
                  value={selected}
                  onChange={(e) => setSelected(e.target.value)}
                >
                  {library.images.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </select>
              </label>
              <img
                className="share-preview"
                src={findPicture(selected)?.src}
                alt={findPicture(selected)?.name}
              />
              <Notice>
                {cloud
                  ? "연결 앱에 적용 요청을 전송합니다. 기기가 보고하기 전까지 적용 대기로 표시됩니다."
                  : "샘플 적용 요청을 저장합니다. 샘플 동기화 후 현재 배경화면에 반영됩니다."}
              </Notice>
              <button
                className="button primary full"
                disabled={busy}
                onClick={async () => {
                  const p = library.images.find((p) => p.id === selected);
                  if (p) {
                    setBusy(true);
                    try {
                      await apply(p, target.device.id, target.displayId);
                      setTarget(null);
                    } finally {
                      setBusy(false);
                    }
                  }
                }}
              >
                적용 요청 저장
              </button>
            </>
          ) : (
            <Empty text="먼저 내 이미지에 사진을 추가해 주세요" />
          )}
        </Modal>
      )}
      {clearing && <Modal title="배경화면을 삭제할까요?" close={() => { if (!busy) setClearing(null); }}>
        <p>‘{clearing.device.name}’의 {clearing.device.displays.find(s => s.id === clearing.displayId)?.name}을 기본 배경화면으로 전환합니다.</p>
        <div className="modal-actions">
          <button className="button secondary" disabled={busy} onClick={() => setClearing(null)}>취소</button>
          <button className="button danger" disabled={busy} onClick={() => void clearWallpaper()}>배경화면 삭제</button>
        </div>
      </Modal>}
      {removing && (
        <Modal title="기기 연결을 해제할까요?" close={() => setRemoving(null)}>
          <p>
            ‘{removing.name}’을 이 계정의 {cloud ? "기기" : "샘플 기기"}{" "}
            목록에서 제거합니다.
          </p>
          <div className="modal-actions">
            <button
              className="button secondary"
              onClick={() => setRemoving(null)}
            >
              취소
            </button>
            <button
              className="button danger"
              onClick={() => void remove(removing)}
            >
              연결 해제
            </button>
          </div>
        </Modal>
      )}
    </section>
  );
}
