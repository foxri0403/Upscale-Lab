import { useEffect, useRef, useState, type MouseEvent } from "react";
import {
  Expand,
  Scan,
  Layers3,
  Crop,
  SlidersHorizontal,
  Share2,
  Download,
  Upload,
  Tag,
  Monitor,
  Check,
  Save,
  ArrowRight,
  RotateCcw,
  Info,
  Image as ImageIcon,
} from "lucide-react";
import { useDemo } from "./Workspace";
import {
  defaultEdit,
  downloadImage,
  filterStyle,
  uuid,
  write,
  type Genre,
  type Edit,
  type Tool,
} from "./model";
import { Modal, Notice, Busy, Empty, Link } from "./ui";
import { cloud, request, post, type Job } from "./api";
import { detailPicture, discardDraft, loadProjectPicture, processProject, refineSubject, resetProjectEffects, saveImageEdit } from "./cloud-store";
import { useUnsavedWarning } from "./use-unsaved-warning";
import { LayerPreviewSelector } from "./LayerPreviewSelector";
import { LayerMotionControls } from "./LayerMotionControls";
import { useParallax } from "./use-parallax";
import type { LayerPreview } from "./layer-preview";
import { StudioUpload } from "./StudioUpload";
import { isImageShared } from "./image-sharing";
import { ImageInformation } from "./ImageInformation";
import { StudioCanvas } from "./StudioCanvas";
import { upscaleDimensions, upscaleResolutions } from "./upscale-resolution";
import { allTagsSelected, imageTags, selectableTags, unspecifiedTag } from "./image-tags";
import { imageFeatures } from "./image-features";
import { copyGeneratedLink } from "./share-clipboard";
import "./studio.css";
import { CropControls } from "./CropControls";
import { useMediaQuery } from "./use-media-query";
import { useSubjectSeparation, SubjectSeparationControls } from "./SubjectSeparation";
import { SmartSelectionClient } from "./smart-selection-client";

const tools: {
  id: Tool;
  name: string;
  icon: typeof Expand;
}[] = [
  {
    id: "upscale",
    name: "업스케일",
    icon: Expand,
  },
  {
    id: "layers",
    name: "배경 분리",
    icon: Scan,
  },
  {
    id: "live",
    name: "Live 2D",
    icon: Layers3,
  },
  {
    id: "crop",
    name: "자르기",
    icon: Crop,
  },
  {
    id: "filter",
    name: "필터",
    icon: SlidersHorizontal,
  },
  { id: "tags", name: "태그 수정", icon: Tag },
];
const normalizedEdit = (value?: Edit) => ({ ...defaultEdit, ...value });
export function Studio({ route }: { route: string }) {
  const compact = useMediaQuery("(max-width: 1100px)");
  const {
    library,
    update,
    toast,
    apply,
    publish,
    saveCloud,
  } = useDemo();
  const parameters = new URLSearchParams(route.split("?")[1]);
  const id = parameters.get("id");
  const picture = parameters.get("new") !== "1" && id
    ? [...library.images, ...(library.assets || [])].find((p) => p.id === id) : undefined;
  const [tool, setTool] = useState<Tool>("upscale");
  const [selectedLayer, setSelectedLayer] = useState<LayerPreview>("all");
  const [lookupFinished, setLookupFinished] = useState<string | null>(null);
  const discarding = useRef(false);
  const [edit, setEdit] = useState<Edit>(picture?.edit || defaultEdit);
  const [name, setName] = useState(picture?.name || "");
  const [tags, setTags] = useState<string[]>(picture ? imageTags(picture) : []);
  const [shareFeedback, setShareFeedback] = useState<{ x: number; y: number; text: string } | null>(null);
  const shareTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const sharePending = useRef(false);
  const pointer = useRef({ x: 0, y: 0 });
  const allTags = useRef<HTMLInputElement>(null);
  const settingsScroll = useRef<HTMLDivElement>(null);
  const [resolution, setResolution] = useState(2560);
  const [imageDimensions, setImageDimensions] = useState({ src: "", width: 0, height: 0 });
  const [dialog, setDialog] = useState<"apply" | "info" | null>(null);
  const [busy, setBusy] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const saving = useRef(false);
  const [target, setTarget] = useState("pc/monitor1");
  const [job, setJob] = useState<Job | null>(null);
  const [capabilities, setCapabilities] = useState<{ upscale: boolean; layers: boolean } | null>(null);
  const selectionClient = useRef<SmartSelectionClient | null>(null);
  const subjectJob = useRef<string | null>(null);
  useEffect(() => { settingsScroll.current?.scrollTo(0, 0); }, [tool, picture?.id]);
  useEffect(() => {
    if (!cloud) return;
    let active = true;
    request<{ upscale: boolean; layers: boolean }>("/api/projects/capabilities")
      .then(value => { if (active) setCapabilities(value); }).catch(() => {});
    return () => { active = false; };
  }, []);
  useEffect(() => {
    const move = (event: PointerEvent) => { pointer.current = { x: event.clientX, y: event.clientY }; };
    window.addEventListener("pointermove", move);
    return () => { window.removeEventListener("pointermove", move); if (shareTimer.current) clearTimeout(shareTimer.current); };
  }, []);
  useEffect(() => {
    if (allTags.current) allTags.current.indeterminate = tags.length > 0 && !allTagsSelected(tags);
  }, [tags, tool]);
  useEffect(() => {
    setJob(null); setBusy(false);
    setTool("upscale"); setShareFeedback(null); setSelectedLayer("all"); discarding.current = false;
    subjectJob.current = null;
  }, [picture?.id]);
  useEffect(() => {
    if (!cloud || !id || picture || id.startsWith("asset:") || parameters.get("new") === "1") return;
    let alive = true;
    loadProjectPicture(id).then(p => {
      if (alive) update(v => ({ ...v, images: [p, ...v.images.filter(x => x.id !== p.id)] }));
    }).catch(() => {}).finally(() => { if (alive) setLookupFinished(id); });
    return () => { alive = false; };
  }, [id, !!picture]);
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
    setName(picture?.name || "");
    setTags(picture ? imageTags(picture) : []);
  }, [picture?.id]);
  const isFox =
    !picture?.separationDisabled && (!!(picture?.background && picture.foregrounds?.length) ||
    (!cloud && !!picture?.src.endsWith("/fox.png")));
  const backgroundSrc = picture?.background || "/images/fox-background.png";
  const foregroundSrcs = picture?.foregrounds?.length
    ? picture.foregrounds
    : ["/images/fox-foreground.png"];
  const published = isImageShared(picture);
  const processing = !!job && ["Queued", "Processing"].includes(job.status);
  const separation = useSubjectSeparation(picture && isFox ? { ...picture, foregrounds: foregroundSrcs } : picture,
    tool === "layers" && !published, published || busy || processing, {
      refine: (input, progress) => {
        selectionClient.current ||= new SmartSelectionClient(() => {});
        return selectionClient.current.refine(input, progress);
      },
      onRefined: () => { setSelectedLayer("all"); separation.setView("overlay"); },
    });
  useEffect(() => {
    return () => { selectionClient.current?.dispose(); selectionClient.current = null; };
  }, [tool, picture?.id, published]);
  const dirty = separation.dirty ||
    JSON.stringify(normalizedEdit(edit)) !== JSON.stringify(normalizedEdit(picture?.edit)) ||
    name.trim() !== picture?.name || JSON.stringify(tags) !== JSON.stringify(picture ? imageTags(picture) : []);
  const saved = !!picture && !picture.isDraft;
  useUnsavedWarning(!!picture && (!saved || dirty), () => {
    if (!picture || discarding.current) return;
    discarding.current = true;
    if (picture.isDraft) {
      update(v => ({ ...v, images: v.images.filter(p => p.id !== picture.id) }));
      if (cloud) void discardDraft(picture).catch(() => {});
    }
  });
  const options = library.devices.flatMap((d) =>
    d.displays
      .filter((s) => s.connected && s.canApplyWallpaper !== false)
      .map((s) => ({ id: `${d.id}/${s.id}`, name: `${d.name} · ${s.name}` })),
  );
  const selectedTarget = options.some((o) => o.id === target)
    ? target
    : options[0]?.id || "";
  const dimensions = imageDimensions.src === (tool === "crop" && isFox ? backgroundSrc : picture?.src) ? imageDimensions : picture;
  const sourceWidth = dimensions?.width || 0;
  const sourceHeight = dimensions?.height || 0;
  const longest = Math.max(sourceWidth, sourceHeight);
  const targetDimensions = upscaleDimensions(sourceWidth, sourceHeight, resolution);
  const motion = useParallax(tool === "layers" && !!isFox && selectedLayer !== "background" && (published || separation.view === "motion"), edit.strength,
    !!edit.mouseEnabled, !!edit.gyroEnabled, picture?.id);
  const gyroContext = useRef({ id: picture?.id, editable: !published });
  gyroContext.current = { id: picture?.id, editable: !published };
  async function toggleGyro() {
    if (!picture || published || busy || processing || motion.gyroPending) return;
    if (edit.gyroEnabled) { change("gyroEnabled", false); return; }
    // A desktop browser can configure the phone's wallpaper without owning a tilt sensor.
    if (!navigator.maxTouchPoints) { change("gyroEnabled", true); return; }
    const requestedFor = picture.id;
    try {
      await motion.requestGyro();
      if (gyroContext.current.id === requestedFor && gyroContext.current.editable) change("gyroEnabled", true);
    } catch (error) { toast((error as Error).message); }
  }
  useEffect(() => {
    if (!longest || job?.status === "Completed") return;
    setResolution(current => current > longest ? current
      : upscaleResolutions.find(value => value.edge > longest)?.edge || 3840);
  }, [longest, job?.status]);
  function change<K extends keyof Edit>(key: K, value: Edit[K]) {
    if (published) return;
    setEdit((e) => ({ ...e, [key]: value }));
  }
  async function resetEffects(feature: "All" | "Upscale" | "Layers") {
    if (!picture || published || busy || processing) return;
    if (separation.dirty) { toast("배경·피사체 분리 시작으로 선택을 적용하거나 선택 초기화를 눌러 주세요."); return; }
    setBusy(true);
    try {
      const nextEdit = feature === "All" ? { ...defaultEdit }
        : { ...edit, ...(feature === "Upscale" ? { scale: 1 } : { mouseEnabled: false, gyroEnabled: false, foreground: true }) };
      const next = cloud && picture.projectId ? await resetProjectEffects(picture, feature)
        : { ...picture, ...(feature !== "Upscale" ? { separationDisabled: true, background: undefined, foregrounds: [], kind: undefined, features: [] } : {}) };
      update(value => ({ ...value, images: value.images.map(p => p.id === picture.id ? next : p),
        assets: value.assets?.map(p => p.id === picture.id ? next : p) }));
      setEdit(nextEdit); setSelectedLayer("all"); setJob(null);
      toast(feature === "All" ? "모든 이미지 편집을 초기화하고 원본으로 복구했습니다."
        : feature === "Upscale" ? "업스케일 적용을 취소했습니다." : "배경 분리와 움직임 적용을 취소했습니다.");
    } catch (error) { toast((error as Error).message); }
    finally { setBusy(false); }
  }
  const draft = picture ? { ...picture, name: name.trim(), tags, genre: (tags[0] || unspecifiedTag) as Genre, edit } : null;
  async function save(silent = false) {
    if (!picture || published || saving.current || busy) return false;
    if (separation.dirty) { toast("배경·피사체 분리 시작을 눌러 선택 영역을 먼저 적용해 주세요."); return false; }
    if (!name.trim()) { toast("이미지 이름을 입력해 주세요."); return false; }
    saving.current = true;
    setBusy(true);
    const snapshot = { ...edit };
    try {
      const savedAt = cloud ? (await saveImageEdit({ ...draft!, edit: snapshot })).savedAt : new Date().toISOString();
      const replace = (p: typeof picture) => p.id === picture.id ? { ...p, isDraft: false, name: name.trim(), tags, genre: (tags[0] || unspecifiedTag) as Genre,
        features: imageFeatures({ ...p, edit: snapshot }), edit: snapshot, savedAt } : p;
      const success = update((value) => ({ ...value,
        images: value.images.map(replace), assets: value.assets?.map(replace),
      }));
      if (success && !silent) toast(saved ? "기존 이미지에 수정 사항을 저장했습니다." : "보관함에 저장했습니다.");
      return success;
    } catch (error) {
      toast((error as Error).message);
      return false;
    } finally { saving.current = false; setBusy(false); }
  }
  async function uploadAndSave() {
    if (!draft || busy || processing) return;
    if (published && (draft.galleryPostId || draft.galleryPostIds?.length) && !window.confirm(
      "기존 모바일 공유 게시물과 댓글·좋아요를 삭제하고 업로드를 해제할까요? 보관함의 원본은 유지됩니다.",
    )) return;
    if (!published && !await save()) return;
    setBusy(true);
    try { await publish(draft, !published); }
    finally { setBusy(false); }
  }
  function showShareFeedback(text: string) {
    if (shareTimer.current) clearTimeout(shareTimer.current);
    setShareFeedback({ x: Math.max(112, Math.min(innerWidth - 112, pointer.current.x)),
      y: Math.max(55, pointer.current.y), text });
    shareTimer.current = setTimeout(() => setShareFeedback(null), 2200);
  }
  async function share(event: MouseEvent<HTMLButtonElement>) {
    if (!draft || busy || processing || sharePending.current) return;
    if (!saved || dirty) { toast("보관함에 저장한 뒤 링크를 공유해 주세요."); return; }
    const bounds = event.currentTarget.getBoundingClientRect();
    pointer.current = event.detail ? { x: event.clientX, y: event.clientY } : { x: bounds.x + bounds.width / 2, y: bounds.y };
    sharePending.current = true;
    setShareFeedback(null);
    try {
      await copyGeneratedLink(async () => {
        setBusy(true);
        let token: string;
        if (cloud) {
          const asset = await saveCloud(draft);
          token = (await post<{ token: string }>(`/api/wallpapers/${asset.id}/share-link`, {})).token;
        } else {
          token = uuid(); write(`upscale.demo.link.${token}`, draft);
        }
        return `${location.origin}/share/${encodeURIComponent(token)}`;
      });
      showShareFeedback("링크가 복사되었습니다!");
    } catch (error) { showShareFeedback((error as Error).message); }
    finally { sharePending.current = false; setBusy(false); }
  }
  async function startProcessing() {
    if (!picture || published || busy || processing || separation.tracing || separation.drawing || separation.polygonPoints) return;
    if (tool === "layers") {
      if (separation.startLocked) return;
      setSelectedLayer("all"); separation.setView("overlay");
      if (separation.dirty && separation.recognized) { await applySubjectEdits(); return; }
    }
    if (tool !== "layers" && separation.dirty) return;
    setBusy(true);
    try {
      const value = await processProject(
        picture,
        tool === "upscale" ? "Upscale" : "Layers",
        tool === "upscale" ? resolution : undefined,
      );
      subjectJob.current = null;
      setJob(value);
    } catch (e) {
      toast((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function applySubjectEdits() {
    if (!picture || published || busy || processing || !separation.recognized || !separation.dirty || separation.drawing || separation.tracing || separation.polygonPoints) return;
    setBusy(true);
    try {
      const value = await refineSubject(picture, await separation.exportMask());
      subjectJob.current = value.jobId;
      setJob(value);
    } catch (error) { toast((error as Error).message); }
    finally { setBusy(false); }
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
                  ? p
                  : x,
              ),
            }));
            change("scale", 1);
            const refined = subjectJob.current === status.jobId;
            if (status.operation === "Layers") {
              if (refined) { separation.markApplied(); subjectJob.current = null; }
              else { separation.reload(true); setSelectedLayer("all"); separation.setView("overlay"); }
            }
            setJob(status);
            toast(status.operation === "Layers" ? refined ? "선택한 피사체와 배경 레이어를 갱신했습니다." : "분리 결과를 확인하고 중앙 이미지에서 바로 수정해 주세요." : "이미지 처리를 완료했습니다.");
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
    if (!picture || busy || processing || downloading || separation.dirty) return;
    setDownloading(true);
    setBusy(true);
    try {
      await downloadImage(
        {
          ...(draft || picture),
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
      setDownloading(false);
      setBusy(false);
    }
  }
  if (!picture)
    return cloud && id && !id.startsWith("asset:") && lookupFinished !== id ? <div className="empty"><Busy /><p>이미지를 불러오는 중…</p></div> : id ? (
      <Empty
        text={id ? "이미지를 찾을 수 없어요" : "편집할 이미지를 추가해 주세요"}
      >
        <Link to="/app/images" className="button primary">
          내 이미지로 이동
        </Link>
      </Empty>
    ) : <StudioUpload />;
  return (
    <div className="studio-layout studio-editor">
      <h1 className="visually-hidden">{picture.name} 작업실</h1>
      <div className="studio-document-bar">
        <div className="studio-document-name"><ImageIcon size={20} />
          <input aria-label="사진 이름" title="사진 이름 변경" maxLength={160} value={name}
            disabled={published || busy || processing} onChange={event => setName(event.target.value)} />
        </div>
        <div className="studio-document-actions">
          <button aria-label="편집 초기화" title="업스케일·배경 분리·색감·자르기·움직임을 초기화하고 원본으로 복구" disabled={published || busy || processing || separation.dirty} onClick={() => void resetEffects("All")}>
            <RotateCcw size={17} /><span>편집 초기화</span>
          </button>
          <button aria-label="이미지 정보" title="이미지 정보" onClick={() => setDialog("info")}><Info size={18} /><span>이미지 정보</span></button>
        </div>
      </div>
      <div className="tool-tabs studio-tool-dock" role="tablist" aria-label="이미지 편집 도구" aria-orientation={compact ? "horizontal" : "vertical"}>
        {tools.map(({ id, name, icon: Icon }) => (
          <button key={id} id={`studio-tool-${id}`} type="button" role="tab"
            disabled={busy || separation.drawing || separation.tracing || separation.polygonPoints > 0}
            aria-controls="studio-properties" aria-selected={tool === id}
            tabIndex={tool === id ? 0 : -1}
            onKeyDown={(event) => {
              const direction = event.key === "ArrowDown" || event.key === "ArrowRight" ? 1
                : event.key === "ArrowUp" || event.key === "ArrowLeft" ? -1 : 0;
              const next = event.key === "Home" ? tools[0] : event.key === "End" ? tools.at(-1)
                : direction ? tools[(tools.findIndex(t => t.id === id) + direction + tools.length) % tools.length] : null;
              if (!next) return;
              event.preventDefault(); setTool(next.id);
              document.getElementById(`studio-tool-${next.id}`)?.focus();
            }}
            onClick={() => setTool(id)} className={tool === id ? "active" : ""}>
            <Icon size={25} /><span>{name}</span>
          </button>
        ))}
      </div>
      <StudioCanvas picture={draft || picture} edit={edit} tool={tool} layered={isFox} motion={motion}
        separation={separation} published={published}
        onEdit={next => { if (!published && !busy && !processing) setEdit(next); }} readOnly={published || busy || processing}
        selectedLayer={selectedLayer}
        background={backgroundSrc} foregrounds={foregroundSrcs} onDimensions={setImageDimensions}
        unsaved={dirty || !saved} saveState={published ? "업로드됨 · 편집 잠금" : dirty ? "저장하지 않은 변경" : saved ? "저장됨" : "보관함에 저장 전"} />
      <aside className="feature-panel studio-inspector" aria-label="이미지 설정">
        <div className="studio-settings-scroll" ref={settingsScroll} tabIndex={0} role="tabpanel" id="studio-properties"
          aria-labelledby={`studio-tool-${tool}`}>
        <fieldset className="photo-settings" disabled={published || busy || processing}>
        {tool === "upscale" && (
          <>
            <h3>목표 해상도</h3>
            <div className="resolution-choices" role="group" aria-label="업스케일 목표 해상도">
              {upscaleResolutions.map(({ edge, name }) => {
                const size = upscaleDimensions(sourceWidth, sourceHeight, edge);
                return <button key={edge} aria-pressed={resolution === edge}
                  className={resolution === edge ? "active" : ""}
                  disabled={!longest || edge <= longest} onClick={() => setResolution(edge)}>
                  <strong>{name}</strong><span>{size ? `${size.width.toLocaleString()} × ${size.height.toLocaleString()}` : `${edge.toLocaleString()}px`}</span>
                </button>;
              })}
            </div>
            <dl className="resolution-summary">
              <div><dt>현재</dt><dd>{sourceWidth && sourceHeight ? `${sourceWidth.toLocaleString()} × ${sourceHeight.toLocaleString()} px` : "확인 중…"}</dd></div>
              <div><dt>결과</dt><dd>{targetDimensions ? `${targetDimensions.width.toLocaleString()} × ${targetDimensions.height.toLocaleString()} px` : "—"}</dd></div>
            </dl>
          </>
        )}
        {(tool === "upscale" || tool === "layers") && (
          <div className="cloud-processing">
            <button
              className={`button primary full ${tool === "layers" ? "separation-action" : ""}`}
              disabled={
                busy ||
                !cloud ||
                !picture.projectId ||
                processing ||
                (tool === "upscale" && separation.dirty) ||
                separation.tracing || separation.drawing || separation.polygonPoints > 0 ||
                (tool === "layers" && (!separation.ready || separation.startLocked)) ||
                (tool === "layers" && capabilities?.layers === false) ||
                (tool === "upscale" && capabilities?.upscale === false) ||
                (tool === "upscale" && (!longest || resolution <= longest))
              }
              onClick={() => void startProcessing()}
            >
              {busy ||
              (job && ["Queued", "Processing"].includes(job.status)) ? (
                <Busy />
              ) : (
                tool === "layers" ? <Scan size={17} /> : <Expand size={17} />
              )}{" "}
              {tool === "upscale"
                ? "업스케일"
                : "배경·피사체 분리 시작"}
            </button>
            {tool === "layers" && separation.error && <p className="mask-error" role="alert">{separation.error}</p>}
            {tool === "upscale" && <button className="button secondary full effect-cancel" type="button"
              disabled={busy || processing || !picture.projectId || !picture.jobId}
              onClick={() => void resetEffects("Upscale")}>
              <RotateCcw size={16} />업스케일 적용 취소
            </button>}
            {tool === "layers" && capabilities?.layers === false && <Notice>배경 분리 서비스 연결을 준비하고 있습니다.</Notice>}
            {job && job.status !== "Completed" && (
              <p className="job-state">
                {job.status === "Failed"
                    ? job.errorMessage || "처리 실패"
                    : `처리 중 · ${job.progress}%`}
              </p>
            )}
          </div>
        )}
        </fieldset>
        {tool === "layers" && <>
          {!published && <SubjectSeparationControls editor={separation} disabled={busy || processing} hasResult={!!picture}
            selectTool={() => { setSelectedLayer("all"); separation.setView("overlay"); }} />}
          <LayerPreviewSelector value={selectedLayer} change={value => { setSelectedLayer(value); if (value !== "background") separation.setView(value === "subject" ? "cutout" : "overlay"); }}
            ready={isFox} original={picture.src} background={backgroundSrc} foregrounds={foregroundSrcs} />
          <fieldset className="photo-settings layer-motion-settings" disabled={published || busy || processing || separation.dirty}>
            <LayerMotionControls edit={edit} ready={isFox} change={(key, value) => { change(key, value); separation.setView("motion"); }} gyroReady={motion.gyroReady}
              gyroPending={motion.gyroPending} toggleGyro={() => { separation.setView("motion"); void toggleGyro(); }} />
            <button className="button secondary full effect-cancel" type="button"
              disabled={!picture.projectId || !isFox || separation.dirty} onClick={() => void resetEffects("Layers")}>
              <RotateCcw size={16} />배경 분리 적용 취소
            </button>
          </fieldset>
        </>}
        {tool === "live" && <h3>Live 2D</h3>}
        <fieldset className="photo-settings" disabled={published || busy || processing}>
        {tool === "crop" && (
          <CropControls edit={edit} width={sourceWidth} height={sourceHeight} change={next => { if (!published && !busy && !processing) setEdit(next); }} />
        )}
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
        {tool === "tags" && <>
          <h3>태그 수정</h3>
          <label className="studio-tag-all"><input ref={allTags} type="checkbox" checked={allTagsSelected(tags)}
            onChange={event => setTags(event.target.checked ? [...selectableTags] : [])} /><span>전체 선택</span></label>
          <div className="studio-tag-options" role="group" aria-label="이미지 태그">
            {selectableTags.map(tag => <label key={tag}><input type="checkbox" checked={tags.includes(tag)}
              onChange={event => setTags(current => event.target.checked ? [...current, tag] : current.filter(value => value !== tag))} /><span>{tag}</span></label>)}
          </div>
        </>}
        </fieldset>
        <div className="studio-save-actions">
        <div className="export-panel">
          <h3>저장 및 내보내기</h3>
          <div className="export-buttons">
            <button
              className="button secondary"
              disabled={busy || processing} onClick={event => void share(event)}
            >
              <Share2 size={18} /> 공유
            </button>
            <button
              className="button secondary"
              disabled={busy || processing || separation.dirty}
              aria-busy={downloading}
              onClick={download}
            >
              <Download size={18} />{downloading ? "다운로드 준비 중…" : "다운로드"}
            </button>
          </div>
        </div>
          <button
            className="button primary full"
            disabled={busy || processing || published || separation.dirty || (saved && !dirty) || !name.trim()}
            onClick={() => void save()}
          >
            <Save size={17} />보관함에 저장
          </button>
          <button className="button secondary full" disabled={busy || processing || separation.dirty || !name.trim()} onClick={() => void uploadAndSave()}>
            <Upload size={18} />{published ? "업로드 해제" : "업로드하고 저장"}
          </button>
          <button
            className="button secondary full"
            disabled={!options.length || busy || processing}
            onClick={() => { if (!saved || dirty) toast("보관함에 저장한 뒤 기기에 적용해 주세요."); else setDialog("apply"); }}
          >
            <Monitor size={18} /> 연결된 기기에 적용
          </button>
        </div>
        </div>
      </aside>
      {shareFeedback && <div className="studio-share-feedback" role="status" aria-live="polite"
        style={{ left: shareFeedback.x, top: shareFeedback.y - 18 }}>{shareFeedback.text}</div>}
      {dialog === "info" && <ImageInformation picture={picture}
        layerCount={isFox ? foregroundSrcs.length + 1 : 1} close={() => setDialog(null)} />}
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
                await apply(draft || { ...picture, edit }, d, s);
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
