import { useState, type FormEvent } from "react";
import {
  Monitor,
  Smartphone,
  Plus,
  ChevronDown,
  Check,
  RefreshCw,
  Unplug,
  Link2,
  Clock3,
} from "lucide-react";
import { useDemo } from "./Workspace";
import { sampleImages, type Device, type Picture } from "./model";
import { Modal, Notice, Empty, PictureView } from "./ui";
import { cloud, post, request, type CloudDevice } from "./api";

export function Devices() {
  const { library, session, update, toast, apply, refresh } = useDemo();
  const [connecting, setConnecting] = useState(false);
  const [platform, setPlatform] = useState<"Windows" | "Android">("Windows");
  const [deviceName, setDeviceName] = useState("");
  const [phoneTab, setPhoneTab] = useState<Record<string, string>>({});
  const [target, setTarget] = useState<{
    device: Device;
    displayId: string;
  } | null>(null);
  const [selected, setSelected] = useState("");
  const [removing, setRemoving] = useState<Device | null>(null);
  const [agentToken, setAgentToken] = useState("");
  const [issuedId, setIssuedId] = useState("");
  const [busy, setBusy] = useState(false);
  const allPictures = [
    ...(library.assets || []),
    ...library.images,
    ...(!cloud ? sampleImages() : []),
  ];
  const findPicture = (id: string) => allPictures.find((p) => p.id === id);
  function sync(device: Device) {
    if (cloud) {
      void refresh().then(() => toast("기기 상태를 새로 불러왔습니다."));
      return;
    }
    const pending = library.commands.filter(
      (c) => c.device === device.name && c.status === "대기",
    );
    if (
      update((v) => ({
        ...v,
        devices: v.devices.map((d) =>
          d.id === device.id
            ? {
                ...d,
                online: true,
                displays: d.displays.map((s) =>
                  s.desired
                    ? { ...s, current: s.desired, desired: undefined }
                    : s,
                ),
              }
            : d,
        ),
        commands: v.commands.map((c) =>
          pending.some((p) => p.id === c.id) ? { ...c, status: "완료" } : c,
        ),
      }))
    )
      toast("샘플 동기화를 완료했습니다. 실제 기기에는 적용하지 않습니다.");
  }
  async function connect(e: FormEvent) {
    e.preventDefault();
    if (!deviceName.trim()) return;
    if (cloud) {
      setBusy(true);
      try {
        const d = await post<CloudDevice>("/api/devices", {
          platform,
          deviceName: deviceName.trim(),
          screenWidth: platform === "Windows" ? 1920 : 1080,
          screenHeight: platform === "Windows" ? 1080 : 2400,
          aspectRatio: platform === "Windows" ? "16:9" : "9:20",
          orientation: platform === "Windows" ? "Landscape" : "Portrait",
        });
        setIssuedId(d.id);
        const result = await post<{ token: string }>(
          `/api/devices/${d.id}/agent-token`,
          {},
        );
        setAgentToken(result.token);
        await refresh();
      } catch (e) {
        toast((e as Error).message);
        await refresh();
      } finally {
        setBusy(false);
      }
      return;
    }
    const device: Device = {
      id: crypto.randomUUID(),
      name: deviceName.trim(),
      platform,
      online: true,
      displays:
        platform === "Windows"
          ? [
              {
                id: "monitor1",
                name: "모니터 1",
                resolution: "1920 × 1080",
                connected: true,
                current: "lake",
              },
            ]
          : [
              {
                id: "home",
                name: "홈 화면",
                resolution: "1080 × 2400",
                connected: true,
                current: "ocean",
              },
              {
                id: "lock",
                name: "잠금 화면",
                resolution: "1080 × 2400",
                connected: true,
                current: "lake",
              },
            ],
    };
    if (update((v) => ({ ...v, devices: [...v.devices, device] }))) {
      setConnecting(false);
      setDeviceName("");
      toast("현재 계정에 샘플 기기를 연결했습니다.");
    }
  }
  function closeConnection() {
    setConnecting(false);
    setAgentToken("");
    setIssuedId("");
    setDeviceName("");
  }
  async function rotate(device: Device) {
    setBusy(true);
    try {
      const r = await post<{ token: string }>(
        `/api/devices/${device.id}/agent-token`,
        {},
      );
      setIssuedId(device.id);
      setAgentToken(r.token);
      setConnecting(true);
    } catch (e) {
      toast((e as Error).message);
    } finally {
      setBusy(false);
    }
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
        <button className="button primary" onClick={() => setConnecting(true)}>
          <Plus size={20} /> 기기 연결
        </button>
      </div>
      <div className="account-device-note">
        <Link2 size={15} />
        <span>
          {session.name} 계정에 연결된 {cloud ? "기기" : "샘플 기기"}
        </span>
        <span className="subtle">
          같은 계정으로 로그인한 연결 앱에서 사용합니다.
        </span>
      </div>
      {!library.devices.length ? (
        <Empty text="연결된 기기가 없어요">
          <button
            className="button primary"
            onClick={() => setConnecting(true)}
          >
            {cloud ? "기기 연결" : "샘플 기기 연결"}
          </button>
        </Empty>
      ) : (
        <div className="device-grid">
          {library.devices.map((device) => {
            const mobile = device.platform === "Android";
            const activeTab = phoneTab[device.id] || device.displays[0]?.id;
            return (
              <article
                key={device.id}
                className={`device-card ${mobile ? "mobile-device" : "pc-device"}`}
              >
                <div className="device-card-header">
                  <span className="device-icon">
                    {mobile ? <Smartphone size={27} /> : <Monitor size={29} />}
                  </span>
                  <div>
                    <h2>{device.name}</h2>
                    <p>
                      {cloud
                        ? device.osVersion || device.platform
                        : device.platform + (mobile ? "" : " 11")}
                      {` · 화면 ${device.physicalDisplayCount ?? new Set(device.displays.filter(d => d.connected).map(d => d.nativeId ?? d.id)).size}개`}
                    </p>
                  </div>
                  <span
                    className={`connection-state ${device.online ? "online" : ""}`}
                  >
                    <i />
                    {device.online ? "최근 연결" : "연결 대기"}
                  </span>
                  <details className="device-more">
                    <summary aria-label={`${device.name} 관리`}>
                      <ChevronDown size={17} />
                    </summary>
                    <div>
                      {cloud && (
                        <button
                          disabled={busy}
                          onClick={() => void rotate(device)}
                        >
                          <Link2 size={15} /> 연결 키 재발급
                        </button>
                      )}
                      <button
                        className="danger-text"
                        onClick={() => setRemoving(device)}
                      >
                        <Unplug size={15} /> 연결 해제
                      </button>
                    </div>
                  </details>
                </div>
                {[device.manufacturer, device.model, device.architecture].some(Boolean) &&
                  <p className="device-hardware">{[device.manufacturer, device.model, device.architecture].filter(Boolean).join(" · ")}</p>}
                {mobile && (
                  <div className="phone-tabs">
                    {device.displays.map((s) => (
                      <button
                        key={s.id}
                        className={activeTab === s.id ? "active" : ""}
                        onClick={() =>
                          setPhoneTab((v) => ({ ...v, [device.id]: s.id }))
                        }
                      >
                        {s.name}
                      </button>
                    ))}
                  </div>
                )}
                {!device.displays.length && (
                  <Notice>
                    연결 앱의 첫 보고를 기다리고 있습니다. 연결 키와 동일한 계정
                    로그인이 필요합니다.
                  </Notice>
                )}
                <div className="display-grid">
                  {device.displays
                    .filter((s) => !mobile || s.id === activeTab)
                    .map((display, i) => {
                      const current = findPicture(display.current);
                      const desired = display.desired
                        ? findPicture(display.desired)
                        : null;
                      return (
                        <div className="display-card" key={display.id}>
                          <div className="display-label">
                            <h3>{display.name}</h3>
                            {!mobile && (display.isPrimary ?? i === 0) && <span>주 화면</span>}
                            <span
                              className={
                                display.connected ? "connected-text" : "muted"
                              }
                            >
                              {display.connected ? "연결됨" : "연결 끊김"}
                            </span>
                          </div>
                          <div
                            className={
                              mobile ? "phone-wallpaper" : "monitor-wallpaper"
                            }
                          >
                            {current ? (
                              <PictureView picture={current} />
                            ) : (
                              <div className="unknown-wallpaper">
                                <Monitor size={30} />
                                <span>이미지 정보 없음</span>
                              </div>
                            )}
                            {mobile && (
                              <span className="lock-clock">09:41</span>
                            )}
                          </div>
                          <p className="display-resolution">
                            {display.resolution}
                            {display.refreshRateHz != null && ` · ${Number(display.refreshRateHz.toFixed(2))} Hz`}
                          </p>
                          <div className="wallpaper-state">
                            <span>현재 배경화면</span>
                            <strong>{current?.name || "이전 이미지"}</strong>
                          </div>
                          {desired && (
                            <div className="pending-wallpaper">
                              <Clock3 size={14} /> 적용 대기: {desired.name}
                            </div>
                          )}
                          <button
                            className="button secondary full"
                            disabled={
                              !library.images.length || !display.connected || display.canApplyWallpaper === false
                            }
                            onClick={() => choose(device, display.id)}
                          >
                            {display.canApplyWallpaper === false ? "정보 조회 전용 화면" : "배경화면 변경"}
                          </button>
                        </div>
                      );
                    })}
                </div>
                <div className="device-card-footer">
                  <span>
                    <i className="sample-dot" />{" "}
                    {cloud ? "계정에 연결됨" : "샘플 기기"}
                  </span>
                  <button className="text-button" onClick={() => sync(device)}>
                    <RefreshCw size={14} />{" "}
                    {cloud ? "상태 새로고침" : "샘플 동기화"}
                  </button>
                </div>
              </article>
            );
          })}
        </div>
      )}
      <section className="history-panel">
        <div className="history-heading">
          <h2>최근 적용 내역</h2>
          <span>최근 {library.commands.length}건</span>
        </div>
        {library.commands.length ? (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>기기</th>
                  <th>화면</th>
                  <th>배경화면</th>
                  <th>상태</th>
                  <th>{cloud ? "요청 시간*" : "요청 시간"}</th>
                </tr>
              </thead>
              <tbody>
                {library.commands.slice(0, 20).map((command) => (
                  <tr key={command.id}>
                    <td>{command.device}</td>
                    <td>{command.display}</td>
                    <td>{command.picture}</td>
                    <td>
                      <span
                        title={command.detail}
                        className={`status-badge ${command.status === "완료" ? "done" : command.status === "실패" ? "failed" : "pending"}`}
                      >
                        {command.status === "완료" ? (
                          <Check size={13} />
                        ) : (
                          <Clock3 size={13} />
                        )}{" "}
                        {command.status === "완료"
                          ? "적용 완료"
                          : command.status === "실패"
                            ? "적용 실패"
                            : command.detail === "NeedsUserAction"
                              ? "기기 확인 필요"
                              : "적용 대기"}
                      </span>
                    </td>
                    <td>
                      {command.created
                        ? new Date(command.created).toLocaleString("ko-KR", {
                            month: "2-digit",
                            day: "2-digit",
                            hour: "2-digit",
                            minute: "2-digit",
                          })
                        : "—"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="no-history">아직 배경화면 적용 내역이 없어요.</p>
        )}
      </section>
      {connecting && (
        <Modal
          title={agentToken ? "기기 연결 키" : "기기 연결"}
          close={closeConnection}
        >
          {agentToken ? (
            <>
              <Notice>
                이 키는 수동 연동용으로 한 번만 표시됩니다. 배포된 연결 앱은 같은 계정으로 로그인하면 기기를 자동 등록합니다.
              </Notice>
              <label>
                기기 ID
                <input
                  readOnly
                  value={issuedId}
                  onFocus={(e) => e.target.select()}
                />
              </label>
              <label>
                연결 키
                <input
                  readOnly
                  value={agentToken}
                  onFocus={(e) => e.target.select()}
                />
              </label>
              <button className="button primary full" onClick={closeConnection}>
                확인
              </button>
            </>
          ) : (
            <>
              <p>
                현재 로그인한 <strong>{session.name}</strong> 계정에{" "}
                {cloud ? "기기를 등록합니다." : "테스트 기기를 추가합니다."}
              </p>
              <form onSubmit={connect}>
                <label>
                  기기 이름
                  <input
                    required
                    value={deviceName}
                    maxLength={40}
                    placeholder="예: 작업용 PC"
                    onChange={(e) => setDeviceName(e.target.value)}
                  />
                </label>
                <label>
                  운영체제
                  <select
                    value={platform}
                    onChange={(e) =>
                      setPlatform(e.target.value as "Windows" | "Android")
                    }
                  >
                    <option>Windows</option>
                    <option>Android</option>
                  </select>
                </label>
                <Notice>
                  {cloud
                    ? "연결 앱의 계정 로그인과 기기 자격 검증이 필요합니다. 화면 정보는 연결 앱이 보고하면 갱신됩니다."
                    : "여기서는 샘플 기기만 추가합니다."}
                </Notice>
                <button
                  className="button primary full"
                  type="submit"
                  disabled={busy}
                >
                  <Link2 size={17} />{" "}
                  {cloud ? "기기 등록 및 연결 키 발급" : "샘플 기기 연결"}
                </button>
              </form>
            </>
          )}
        </Modal>
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
