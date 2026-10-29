import { CropEditor } from "./CropEditor";
import { CropControls } from "./CropControls";
import { useEffect, useRef, useState } from "react";
import {
  Expand,
  Scan,
  Layers3,
  Crop,
  SlidersHorizontal,
  LockKeyhole,
  Eye,
  EyeOff,
  Share2,
  Download,
  Mouse,
  Monitor,
  Check,
  Copy,
  Save,
  ArrowRight,
  RotateCcw,
  Info,
} from "lucide-react";
import { useDemo } from "./Workspace";
import {
  defaultEdit,
  downloadImage,
  filterStyle,
  type Edit,
  type Tool,
} from "./model";
import { Modal, Notice, Busy, Empty, Link, navigate } from "./ui";
import { cloud, request, type Job } from "./api";
import { detailPicture, processProject } from "./cloud-store";
import { StudioUpload } from "./StudioUpload";
import { ImageSharingToggle } from "./ImageSharingToggle";
import { isImageShared } from "./image-sharing";
import { ImageInformation } from "./ImageInformation";

const tools: {
  id: Tool;
  name: string;
  icon: typeof Expand;
  description: string;
}[] = [
  {
    id: "upscale",
    name: "업스케일",
    icon: Expand,
    description: "더 큰 해상도로 이미지를 준비해요",
  },
  {
    id: "layers",
    name: "배경 분리",
    icon: Scan,
    description: "배경과 인물·사물을 따로 확인해요",
  },
  {
    id: "live",
    name: "Live 2D",
    icon: Layers3,
    description: "배경은 고정하고 피사체를 움직여요",
  },
  {
    id: "crop",
    name: "자르기",
    icon: Crop,
    description: "화면에 어울리는 비율을 골라 보세요",
  },
  {
    id: "filter",
    name: "필터",
    icon: SlidersHorizontal,
    description: "사진의 분위기를 완성해요",
  },
];
export function Studio({ route }: { route: string }) {
  const {
    library,
    update,
    toast,
    session,
    apply,
    saveCloud,
    refresh,
  } = useDemo();
  const parameters = new URLSearchParams(route.split("?")[1]);
  const id = parameters.get("id");
  const picture = parameters.get("new") !== "1" && id
    ? [...library.images, ...(library.assets || [])].find((p) => p.id === id) : undefined;
  const [tool, setTool] = useState<Tool>("live");
  const [edit, setEdit] = useState<Edit>(picture?.edit || defaultEdit);
  const [cropDimensions, setCropDimensions] = useState({ src: "", width: 0, height: 0 });
  const [xy, setXy] = useState({ x: 0, y: 0 });
  const editRef = useRef(edit);
  editRef.current = edit;
  const [dialog, setDialog] = useState<"share" | "apply" | "info" | null>(null);
  const [busy, setBusy] = useState(false);
  const [target, setTarget] = useState("pc/monitor1");
  const [copied, setCopied] = useState(false);
  const [job, setJob] = useState<Job | null>(null);
  useEffect(() => {
    setJob(null);
  }, [picture?.id]);
  useEffect(() => {
    if (
      !cloud ||
      !picture?.projectId ||
      (picture.detailsUntil && picture.detailsUntil > Date.now())
    )
      return;
    let alive = true;
    detailPicture(picture)
      .then((p) => {
        if (alive)
          update((v) => ({
            ...v,
            images: v.images.map((x) => (x.id === p.id ? p : x)),
          }));
      })
      .catch((e) => {
        if (alive) toast(e.message);
      });
    return () => {
      alive = false;
    };
  }, [picture?.id, picture?.detailsUntil]);
  useEffect(() => {
    setEdit(picture?.edit || { ...defaultEdit });
  }, [picture?.id]);
  const isFox =
    !!(picture?.background && picture.foregrounds?.length) ||
    (!cloud && !!picture?.src.endsWith("/fox.png"));
  const backgroundSrc = picture?.background || "/images/fox-background.png";
  const foregroundSrcs = picture?.foregrounds?.length
    ? picture.foregrounds
    : ["/images/fox-foreground.png"];
  const dirty =
    JSON.stringify(edit) !== JSON.stringify(picture?.edit || defaultEdit);
  const published = isImageShared(picture);
  const options = library.devices.flatMap((d) =>
    d.displays
      .filter((s) => s.connected && s.canApplyWallpaper !== false)
      .map((s) => ({ id: `${d.id}/${s.id}`, name: `${d.name} · ${s.name}` })),
  );
  const selectedTarget = options.some((o) => o.id === target)
    ? target
    : options[0]?.id || "";
  const active = tools.find((t) => t.id === tool)!;
  function change<K extends keyof Edit>(key: K, value: Edit[K]) {
    if (published) return;
    setEdit((e) => ({ ...e, [key]: value }));
  }
  async function save() {
    if (!picture || published) return false;
    if (cloud) {
      setBusy(true);
      try {
        const asset = await saveCloud({ ...picture, edit });
        await refresh();
        navigate(`/app/studio?id=asset:${encodeURIComponent(asset.id)}`, true);
        toast("새 배경화면 버전을 서버 보관함에 저장했습니다.");
        return true;
      } catch (e) {
        toast((e as Error).message);
        return false;
      } finally {
        setBusy(false);
      }
    }
    const success = update((v) => ({
      ...v,
      images: v.images.map((p) =>
        p.id === picture.id ? { ...p, edit: { ...edit } } : p,
      ),
    }));
    if (success) toast("편집 설정을 보관함에 저장했습니다.");
    return success;
  }
  async function startProcessing() {
    if (!picture || published) return;
    setBusy(true);
    try {
      const value = await processProject(
        picture,
        tool === "upscale" ? "Upscale" : "Layers",
        edit.scale,
      );
      setJob(value);
    } catch (e) {
      toast((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  useEffect(() => {
    if (!cloud || !picture?.projectId) return;
    let alive = true;
    let polling = false;
    const poll = async () => {
      if (polling) return;
      polling = true;
      try {
        const status = await request<Job>(
          `/api/projects/${picture.projectId}/processing-status`,
        );
        if (!alive) return;
        if (status.status === "Completed") {
          const p = await detailPicture(picture);
          if (alive) {
            update((v) => ({
              ...v,
              images: v.images.map((x) =>
                x.id === p.id
                  ? { ...p, edit: { ...editRef.current, scale: 1 } }
                  : x,
              ),
            }));
            change("scale", 1);
            setJob(status);
            toast("이미지 처리를 완료했습니다.");
          }
        } else {
          setJob(status);
          if (status.status === "Failed")
            toast(status.errorMessage || "이미지 처리에 실패했습니다.");
        }
      } catch (e) {
        if (alive) {
          setJob(null);
          toast((e as Error).message);
        }
      } finally {
        polling = false;
      }
    };
    if (!job) {
      request<Job>(`/api/projects/${picture.projectId}/processing-status`)
        .then((s) => {
          if (alive && ["Queued", "Processing"].includes(s.status)) setJob(s);
        })
        .catch(() => {});
    }
    const timer =
      job && ["Queued", "Processing"].includes(job.status)
        ? setInterval(() => void poll(), 2200)
        : null;
    return () => {
      alive = false;
      if (timer) clearInterval(timer);
    };
  }, [picture?.id, job?.jobId, job?.status]);
  async function download() {
    if (!picture) return;
    setBusy(true);
    try {
      await downloadImage(
        {
          ...picture,
          ...(isFox
            ? { background: backgroundSrc, foregrounds: foregroundSrcs }
            : {}),
        },
        edit,
      );
      toast(
        "PNG 파일을 다운로드했습니다. Live 2D 움직임은 정지 이미지로 저장됩니다.",
      );
    } catch (e) {
      toast((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (!picture)
    return id ? (
      <Empty
        text={id ? "이미지를 찾을 수 없어요" : "편집할 이미지를 추가해 주세요"}
      >
        <Link to="/app/images" className="button primary">
          내 이미지로 이동
        </Link>
      </Empty>
    ) : <StudioUpload />;
  const sharedId = cloud
    ? picture.sharedId || picture.galleryPostId || picture.assetId || ""
    : `shared-${session.id}-${picture.id}`;
  const cropRatio =
    edit.crop === "wide"
      ? "16/9"
      : edit.crop === "square"
        ? "1/1"
        : edit.crop === "portrait"
          ? "9/16"
          : undefined;
  return (
    <div className="studio-layout">
      <section className="studio-workspace">
        <h1 className="visually-hidden">{picture.name} 작업실</h1>
        <div className="studio-toolbar">
        <div className="tool-tabs" role="tablist" aria-label="이미지 편집 도구">
          {tools.map(({ id, name, icon: Icon }) => (
            <button
              key={id}
              role="tab"
              aria-selected={tool === id}
              onClick={() => {
                setTool(id);
              }}
              className={tool === id ? "active" : ""}
            >
              <Icon size={20} />
              {name}
            </button>
          ))}
        </div>
          <button className="button secondary image-information-button" onClick={() => setDialog("info")}>
            <Info size={18} /> 이미지 정보
          </button>
        </div>
        <div className="canvas-panel">
          {tool === "crop" ? <div className="canvas-stage"><CropEditor onDimensions={setCropDimensions} picture={isFox ? { ...picture, background: backgroundSrc, foregrounds: foregroundSrcs } : picture} edit={edit} disabled={published || busy} change={value => { if (!published && !busy) setEdit(value); }} /></div> : <div
            className={`canvas-stage ${cropRatio ? "crop-stage" : ""}`}
            style={
              cropRatio
                ? {
                    aspectRatio: cropRatio,
                    width: `min(100%, calc(var(--studio-canvas-height) * ${edit.crop === "wide" ? 16 / 9 : edit.crop === "portrait" ? 9 / 16 : 1}))`,
                    height: "auto",
                  }
                : undefined
            }
            onMouseMove={(e) => {
              if (tool !== "live" || !isFox) return;
              const r = e.currentTarget.getBoundingClientRect();
              setXy({
                x:
                  ((e.clientX - r.left) / r.width - 0.5) * edit.strength * 0.45,
                y: ((e.clientY - r.top) / r.height - 0.5) * edit.strength * 0.3,
              });
            }}
            onMouseLeave={() => setXy({ x: 0, y: 0 })}
          >
            {isFox && (tool === "live" || tool === "layers") ? (
              <div
                className="live-canvas"
                style={{ filter: filterStyle(edit.filter) }}
              >
                <img src={backgroundSrc} alt="배경 레이어" />
                {edit.foreground &&
                  foregroundSrcs.map((src, index) => (
                    <img
                      key={src}
                      className="subject-layer"
                      src={src}
                      alt={`피사체 레이어 ${index + 1}`}
                      style={{ transform: `translate(${xy.x}px, ${xy.y}px)` }}
                    />
                  ))}
              </div>
            ) : (
              <img
                src={picture.src}
                alt={picture.name}
                style={{ filter: filterStyle(edit.filter) }}
              />
            )}{" "}
          </div>}
          <p className="canvas-caption">
            <Mouse size={15} />
            {tool === "live" && isFox
              ? "마우스를 움직여 확인하세요"
              : tool === "crop"
                ? "선택 영역을 드래그해 이동하세요"
                : "로컬 편집 미리보기"}
          </p>

        </div>
      </section>
      <aside className="feature-panel">
        {published && <Notice>공유 중인 이미지입니다. 이미지 설정에서 공유를 끄면 이름과 사진 설정을 변경할 수 있어요.</Notice>}
        <div className="feature-panel-heading">
          <h2>{active.name}</h2>
          <p>{active.description}</p>
          <span className={`save-state ${dirty ? "unsaved" : ""}`}>
            <i />
            {published ? "공유 중 · 편집 잠금" : dirty ? "저장하지 않은 변경" : "저장됨"}
          </span>
        </div>
        <fieldset className="photo-settings" disabled={published || busy}>
        {cloud && (tool === "upscale" || tool === "layers") && (
          <div className="cloud-processing">
            <button
              className="button primary full"
              disabled={
                busy ||
                !picture.projectId ||
                (!!job && ["Queued", "Processing"].includes(job.status))
              }
              onClick={() => void startProcessing()}
            >
              {busy ||
              (job && ["Queued", "Processing"].includes(job.status)) ? (
                <Busy />
              ) : (
                <Expand size={17} />
              )}{" "}
              {tool === "upscale"
                ? "AI 업스케일 시작"
                : "배경·피사체 분리 시작"}
            </button>
            {job && (
              <p className="job-state">
                {job.status === "Completed"
                  ? "처리 완료"
                  : job.status === "Failed"
                    ? job.errorMessage || "처리 실패"
                    : `처리 중 · ${job.progress}%`}
              </p>
            )}
          </div>
        )}
        {(tool === "live" || tool === "layers") && (
          <>
            <h3>레이어</h3>
            <div className="layer-row">
              <img src={isFox ? backgroundSrc : picture.src} alt="배경" />
              <span>배경</span>
              <LockKeyhole size={19} />
            </div>
            <div className="layer-row">
              <div className="checker layer-thumb">
                {isFox && <img src={foregroundSrcs[0]} alt="피사체" />}
              </div>
              <span>인물·사물</span>
              <button
                className="icon-button"
                disabled={!isFox}
                aria-label={edit.foreground ? "피사체 숨기기" : "피사체 보이기"}
                onClick={() => change("foreground", !edit.foreground)}
              >
                {edit.foreground ? <Eye size={20} /> : <EyeOff size={20} />}
              </button>
            </div>
            {tool === "live" && (
              <>
                <h3>움직임 강도</h3>
                <div className="range-field">
                  <input
                    aria-label="움직임 강도"
                    type="range"
                    min="0"
                    max="100"
                    value={edit.strength}
                    disabled={!isFox}
                    onChange={(e) => change("strength", Number(e.target.value))}
                  />
                  <output>{edit.strength}</output>
                </div>
              </>
            )}
            <Notice>
              {cloud
                ? isFox
                  ? "준비된 레이어로 움직임을 확인하세요. Live 2D 결과물 저장 시 레이어 설정을 고정합니다."
                  : "배경 분리 도구에서 분리를 시작해 주세요. 원본 프로젝트에서 사용할 수 있습니다."
                : isFox
                  ? "샘플 여우의 준비된 레이어로 동작을 확인할 수 있어요."
                  : "샘플 여우를 선택하면 레이어를 미리 볼 수 있습니다."}
            </Notice>
          </>
        )}
        {tool === "upscale" && (
          <>
            <h3>크기 배율</h3>
            <div className="choice-row">
              {[1, 2, 4].map((n) => (
                <button
                  key={n}
                  className={edit.scale === n ? "active" : ""}
                  onClick={() => change("scale", n)}
                >
                  {n}×
                </button>
              ))}
            </div>
            <div className="quality-detail">
              <Expand size={25} />
              <strong>고해상도 내보내기</strong>
              <p>PNG · 최대 긴 변 4,096px</p>
            </div>
            <Notice>
              {cloud
                ? "배율을 선택한 뒤 AI 업스케일을 시작하세요. 완료된 결과가 미리보기에 표시됩니다."
                : "로컬 테스트에서는 브라우저 보간으로 크기를 바꿉니다. AI 처리는 클라우드 모드에서 실행하세요."}
            </Notice>
          </>
        )}
        {tool === "crop" && <CropControls edit={edit} width={cropDimensions.src === (isFox ? backgroundSrc : picture.src) ? cropDimensions.width : picture.width || 1920} height={cropDimensions.src === (isFox ? backgroundSrc : picture.src) ? cropDimensions.height : picture.height || 1080} change={value => { if (!published && !busy) setEdit(value); }} />}
        {tool === "filter" && (
          <>
            <h3>색감</h3>
            <div className="filter-choices">
              {[
                ["original", "원본"],
                ["vivid", "선명하게"],
                ["warm", "따뜻하게"],
                ["mono", "흑백"],
                ["soft", "부드럽게"],
              ].map(([value, label]) => (
                <button
                  key={value}
                  className={edit.filter === value ? "active" : ""}
                  onClick={() => change("filter", value)}
                >
                  <img
                    src={picture.src}
                    alt=""
                    style={{ filter: filterStyle(value) }}
                  />
                  <span>{label}</span>
                </button>
              ))}
            </div>
          </>
        )}
        <button
          className="text-button reset-edit"
          onClick={() => {
            setEdit({ ...defaultEdit });
          }}
        >
          <RotateCcw size={14} /> 편집 초기화
        </button>
        </fieldset>
        <div className="apply-target">
          <h3>적용 기기</h3>
          <label className="device-select">
            <Monitor size={23} />
            <select
              aria-label="배경화면 적용 기기"
              value={selectedTarget}
              onChange={(e) => setTarget(e.target.value)}
              disabled={!options.length}
            >
              {options.length ? (
                options.map((o) => (
                  <option key={o.id} value={o.id}>
                    {o.name}
                  </option>
                ))
              ) : (
                <option>연결된 기기 없음</option>
              )}
            </select>
          </label>
        </div>
        <div className="export-panel">
          <h3>저장 및 내보내기</h3>
          <div className="export-buttons">
            <button
              className="button secondary"
              onClick={() => {
                setCopied(false);
                setDialog("share");
              }}
            >
              <Share2 size={18} /> 공유
            </button>
            <button
              className="button secondary"
              disabled={busy}
              onClick={download}
            >
              {busy ? <Busy /> : <Download size={18} />} 다운로드
            </button>
          </div>
          <button
            className="button secondary full"
            disabled={busy || published}
            onClick={() => void save()}
          >
            <Save size={17} /> 보관함에 저장
          </button>
          <button
            className="button primary full"
            disabled={!options.length}
            onClick={() => setDialog("apply")}
          >
            <Monitor size={18} /> 기기에 적용
          </button>
        </div>
      </aside>
      {dialog === "info" && <ImageInformation picture={picture}
        layerCount={isFox ? foregroundSrcs.length + 1 : 1} close={() => setDialog(null)} />}
      {dialog === "share" && (
        <Modal title="이미지 공유" close={() => setDialog(null)}>
          <img className="share-preview" src={picture.src} alt={picture.name} />
          <div className="sharing-row">
            <div>
              <strong>탐색에 공유</strong>
              <p>
                {cloud
                  ? "공유한 배경화면을 탐색에서 볼 수 있습니다."
                  : "이 브라우저의 탐색 화면에 표시됩니다."}
              </p>
            </div>
            <ImageSharingToggle picture={picture} disabled={busy || (dirty && !published)} />
          </div>
          {dirty && !published && <Notice>변경 사항을 보관함에 저장한 뒤 공유를 켜 주세요.</Notice>}
          {published && (
            <div className="copy-field">
              <input
                readOnly
                aria-label="공유 링크"
                value={`${location.origin}/app/explore?shared=${encodeURIComponent(sharedId)}`}
              />
              <button
                className="icon-button"
                aria-label="공유 링크 복사"
                onClick={async () => {
                  try {
                    await navigator.clipboard.writeText(
                      `${location.origin}/app/explore?shared=${encodeURIComponent(sharedId)}`,
                    );
                    setCopied(true);
                  } catch {
                    toast("링크를 선택한 뒤 직접 복사해 주세요.");
                  }
                }}
              >
                {copied ? <Check size={18} /> : <Copy size={18} />}
              </button>
            </div>
          )}
          <Notice>
            {cloud
              ? "공유를 켜면 이름과 사진 설정이 잠깁니다. 공유를 끈 뒤 변경할 수 있습니다."
              : "브라우저 안에서 공유 흐름을 확인하는 데모입니다. 다른 기기나 외부 사용자에게는 전달되지 않습니다."}
          </Notice>
        </Modal>
      )}
      {dialog === "apply" && (
        <Modal title="배경화면 적용" close={() => setDialog(null)}>
          <img className="share-preview" src={picture.src} alt={picture.name} />
          <label>
            기기 및 화면
            <select
              value={selectedTarget}
              onChange={(e) => setTarget(e.target.value)}
            >
              {options.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.name}
                </option>
              ))}
            </select>
          </label>
          <Notice>
            {cloud
              ? "연결 앱이 같은 계정으로 로그인한 상태에서 요청을 받아 적용합니다. 실제 적용 완료는 기기의 보고 이후 표시됩니다."
              : "샘플 기기에 적용 요청을 만듭니다. 실제 PC·휴대폰 배경화면은 변경되지 않습니다. 기기 관리에서 샘플 동기화를 눌러 완료 상태를 확인하세요."}
          </Notice>
          <button
            className="button primary full"
            disabled={busy}
            onClick={async () => {
              setBusy(true);
              try {
                const [d, s] = selectedTarget.split("/");
                await apply({ ...picture, edit }, d, s);
                setDialog(null);
              } finally { setBusy(false); }
            }}
          >
            적용 요청 저장 <ArrowRight size={17} />
          </button>
        </Modal>
      )}
    </div>
  );
}
