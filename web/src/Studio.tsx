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
import { Modal, Notice, Busy, Empty, Link, navigate } from "./ui";
import { ApiError, cloud, request, post, type Job } from "./api";
import { createCroppedLive2dProject, detailPicture, discardDraft, loadProjectPicture, prepareEditableProject, processProject, refineSubject, resetProjectEffects, saveImageEdit } from "./cloud-store";
import { processingStatus } from "./processing-status";
import { TokenUsageHelp } from "./TokenUsageHelp";
import { useUnsavedWarning } from "./use-unsaved-warning";
import { LayerPreviewSelector } from "./LayerPreviewSelector";
import { LayerMotionControls, MotionInputButtons } from "./LayerMotionControls";
import { useParallax } from "./use-parallax";
import type { LayerPreview } from "./layer-preview";
import { StudioUpload } from "./StudioUpload";
import { isImageShared } from "./image-sharing";
import { ImageInformation } from "./ImageInformation";
import { StudioCanvas } from "./StudioCanvas";
import { canSelectUpscaleResolution, upscaleDimensions, upscaleResolutions } from "./upscale-resolution";
import { allTagsSelected, imageTags, selectableTags, unspecifiedTag } from "./image-tags";
import { PlatformTagOptions } from "./GalleryBadges";
import type { PlatformTag } from "./platform-tags";
import { ApplyWallpaperDialog } from "./ApplyWallpaperDialog";
import { wallpaperTargets } from "./explore-actions";
import { imageFeatures } from "./image-features";
import { copyGeneratedLink } from "./share-clipboard";
import "./studio.css";
import { CropControls } from "./CropControls";
import { useMediaQuery } from "./use-media-query";
import { useSubjectSeparation, SubjectSeparationControls } from "./SubjectSeparation";
import { useCropAutosave } from "./use-crop-autosave";
import { sameCrop } from "./crop-geometry";
import { live2dCropRequired } from "./live2d-crop-input";
import { motionStrength } from "./editor-defaults";

import { LayerStackEditor } from "./LayerStackEditor";
import { separationAvailable } from "./character-processing";
import { CharacterMotionControls } from "./CharacterMotionControls";
import { layerLabel } from "./layer-label";
import { livePreviewLayers, selectedLivePartId } from "./live-layer-preview";
import { useCharacterRig } from "./use-character-rig";
import { normalizeRig } from "./character-rig";

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
  { id: "tags", name: "태그", icon: Tag },
];
const normalizedEdit = (value?: Edit) => ({ ...defaultEdit, ...value, strength: motionStrength(value), motionDefaultsVersion: 1 });
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
  const requestedTool: Tool = parameters.get("tool") === "live" ? "live" : "upscale";
  const [tool, setTool] = useState<Tool>(requestedTool);
  const [selectedLayer, setSelectedLayer] = useState<LayerPreview>("all");
  const [requestedLivePartId, setRequestedLivePartId] = useState<string | null>(null);
  const [lookupFinished, setLookupFinished] = useState<string | null>(null);
  const discarding = useRef(false);
  const [edit, setEdit] = useState<Edit>(normalizedEdit(picture?.edit));
  const currentLibrary = useRef(library); currentLibrary.current = library;
  const cropAutosave = useCropAutosave(async ({ picture: target, crop }) => {
    const current = [...currentLibrary.current.images, ...(currentLibrary.current.assets || [])].find(p => p.id === target.id) || target;
    const snapshot = { ...normalizedEdit(current.edit), ...crop };
    const savedAt = cloud ? (await saveImageEdit({ ...current, edit: snapshot }, true)).savedAt : new Date().toISOString();
    const replace = (p: typeof current) => p.id === target.id ? { ...p, isDraft: false,
      edit: { ...normalizedEdit(p.edit), ...crop }, savedAt } : p;
    if (!update(value => ({ ...value, images: value.images.map(replace), assets: value.assets?.map(replace) })))
      throw new Error("자르기 저장 상태를 동기화하지 못했습니다.");
  }, error => toast(error.message), picture?.id, tool === "crop");
  const [name, setName] = useState(picture?.name || "");
  const [tags, setTags] = useState<string[]>(picture ? imageTags(picture) : []);
  const [platformTags, setPlatformTags] = useState<PlatformTag[]>(picture?.platformTags ?? []);
  const [shareFeedback, setShareFeedback] = useState<{ x: number; y: number; text: string } | null>(null);
  const shareTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const sharePending = useRef(false);
  const pointer = useRef({ x: 0, y: 0 });
  const allTags = useRef<HTMLInputElement>(null);
  const settingsScroll = useRef<HTMLDivElement>(null);
  const [upscaleSelection, setUpscaleSelection] = useState<{ pictureId: string; jobId?: string; edge: number }>();
  const [imageDimensions, setImageDimensions] = useState({ src: "", width: 0, height: 0 });
  const [dialog, setDialog] = useState<"apply" | "info" | "layerStack" | null>(null);
  const [busy, setBusy] = useState(false);
  const [startingProcessing, setStartingProcessing] = useState(false);
  const [cancelPrompt, setCancelPrompt] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [checkingJob, setCheckingJob] = useState(cloud && !!picture?.projectId);
  const [copyingAsset, setCopyingAsset] = useState(false);
  const [createdLive2dProjectId, setCreatedLive2dProjectId] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);
  const saving = useRef(false);
  const [job, setJob] = useState<Job | null>(null);
  const [capabilities, setCapabilities] = useState<{ upscale: boolean; layers: boolean; characterLayers?: boolean } | null>(null);
  const subjectJob = useRef<string | null>(null);
  useEffect(() => { settingsScroll.current?.scrollTo(0, 0); }, [tool, picture?.id]);
  useEffect(() => {
    if (!cloud) return;
    let active = true;
    request<{ upscale: boolean; layers: boolean; characterLayers?: boolean }>("/api/projects/capabilities")
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
    setJob(null); setBusy(false); setStartingProcessing(false); setCancelPrompt(false); setCancelling(false);
    setCheckingJob(cloud && !!picture?.projectId);
    setTool(requestedTool); setCreatedLive2dProjectId(null); setShareFeedback(null); setSelectedLayer("all"); setRequestedLivePartId(null); discarding.current = false;
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
    setEdit(normalizedEdit(picture?.edit));
    setName(picture?.name || "");
    setTags(picture ? imageTags(picture) : []);
    setPlatformTags(picture?.platformTags ?? []);
  }, [picture?.id]);
  const isFox =
    !picture?.separationDisabled && (!!(picture?.background && picture.foregrounds?.length) ||
    (!cloud && !!picture?.src.endsWith("/fox.png")));
  const isCharacterProject = !!picture?.layers?.length && !picture.layers.some(layer => layer.layerType === "Background");
  const hasCharacterSeparation = isCharacterProject || (isFox && picture?.features?.includes("Live 2D") &&
    !picture.features.includes("배경 분리"));
  const separationConflict = tool === "layers" && hasCharacterSeparation
    ? "배경 분리를 적용하려면 Live 2D 탭에서 기존 적용을 취소해 주세요."
    : tool === "live" && isFox && !hasCharacterSeparation
      ? "Live 2D를 적용하려면 배경 분리 탭에서 기존 적용을 취소해 주세요."
      : undefined;
  const activeLivePartId = selectedLivePartId(picture?.layers ?? [], requestedLivePartId);
  const backgroundSrc = picture?.background || (!cloud && isFox ? "/images/fox-background.png" : "");
  const foregroundSrcs = picture?.foregrounds?.length
    ? picture.foregrounds
    : !cloud && isFox ? ["/images/fox-foreground.png"] : [];
  const published = isImageShared(picture);
  const processing = !!job && ["Queued", "Processing"].includes(job.status);
  const separation = useSubjectSeparation(picture && isFox ? { ...picture, foregrounds: foregroundSrcs } : picture,
    tool === "layers" && !published && !separationConflict, published || busy || processing || !isFox, undefined, true);
  const dirty = cropAutosave.pending || separation.dirty ||
    JSON.stringify(normalizedEdit(edit)) !== JSON.stringify(normalizedEdit(picture?.edit)) ||
    name.trim() !== picture?.name || JSON.stringify(tags) !== JSON.stringify(picture ? imageTags(picture) : []) ||
    JSON.stringify(platformTags) !== JSON.stringify(picture?.platformTags ?? []);
  const saved = !!picture && !picture.isDraft;
  useEffect(() => {
    if (!createdLive2dProjectId || busy || cropAutosave.pending || dirty || !saved) return;
    // Saving before the switch prevents the draft cleanup from deleting the source.
    discarding.current = true;
    navigate(`/app/studio?id=${encodeURIComponent(createdLive2dProjectId)}&tool=live`);
    setCreatedLive2dProjectId(null);
  }, [createdLive2dProjectId, busy, cropAutosave.pending, dirty, saved]);
  useUnsavedWarning(!!picture && !picture.isReadOnly && (!saved || dirty), () => {
    if (!picture || discarding.current) return;
    discarding.current = true;
    if (picture.isDraft) {
      update(v => ({ ...v, images: v.images.filter(p => p.id !== picture.id) }));
      if (cloud) void discardDraft(picture).catch(() => {});
    }
  }, { active: busy || processing || checkingJob || cropAutosave.pending || separation.tracing,
    blocked: () => toast("작업 중에는 다른 기능이나 화면으로 전환할 수 없습니다. 작업 완료를 기다리거나 작업 취소를 눌러 주세요.") });
  const options = wallpaperTargets(library.devices, false);
  const dimensions = imageDimensions.src === (tool === "crop" && isFox ? backgroundSrc : picture?.src) ? imageDimensions : picture;
  const sourceWidth = dimensions?.width || 0;
  const sourceHeight = dimensions?.height || 0;
  const longest = Math.max(sourceWidth, sourceHeight);
  const availableResolutions = upscaleResolutions.filter(({ edge }) => canSelectUpscaleResolution(edge, longest));
  const preferredEdge = upscaleSelection?.pictureId === picture?.id && upscaleSelection?.jobId === picture?.jobId
    ? upscaleSelection?.edge : undefined;
  const resolution = availableResolutions.find(({ edge }) => edge === preferredEdge)?.edge ?? availableResolutions[0]?.edge;
  const targetDimensions = resolution ? upscaleDimensions(sourceWidth, sourceHeight, resolution) : null;
  const liveMotion = tool === "live" && isCharacterProject && activeLivePartId === null;
  const backgroundMotion = tool === "layers" && isFox && !separationConflict && selectedLayer !== "background" && (published || separation.view === "motion");
  const motion = useParallax(liveMotion || backgroundMotion, edit.strength,
    !!edit.mouseEnabled, !!edit.gyroEnabled, picture?.id);
  const characterRig = useCharacterRig(liveMotion && motion.active, { ...normalizeRig(edit.rig), enabled: true },
    picture ? `${picture.id}:${picture.layers?.map(layer => layer.id).join(",") || "none"}` : undefined, motion.position);
  const gyroContext = useRef({ id: picture?.id, editable: !published });
  gyroContext.current = { id: picture?.id, editable: !published };
  function changeMotionInput(key: "mouseEnabled" | "gyroEnabled", value: boolean) {
    if (tool !== "live") { change(key, value); return; }
    if (value) setRequestedLivePartId(null);
    setEdit(previous => ({ ...previous, [key]: value, rig: { ...normalizeRig(previous.rig), enabled: true } }));
  }
  async function toggleGyro() {
    if (!picture || published || busy || processing || motion.gyroPending) return;
    if (edit.gyroEnabled) { changeMotionInput("gyroEnabled", false); return; }
    // A desktop browser can configure the phone's wallpaper without owning a tilt sensor.
    if (!navigator.maxTouchPoints && (tool !== "live" || typeof DeviceOrientationEvent === "undefined")) {
      changeMotionInput("gyroEnabled", true); return;
    }
    const requestedFor = picture.id;
    try {
      await motion.requestGyro();
      if (gyroContext.current.id === requestedFor && gyroContext.current.editable) changeMotionInput("gyroEnabled", true);
    } catch (error) { toast((error as Error).message); }
  }
  function change<K extends keyof Edit>(key: K, value: Edit[K]) {
    if (published) return;
    setEdit((e) => ({ ...e, [key]: value }));
  }
  function changeCrop(next: Edit) {
    if (!picture || published || busy || processing || sameCrop(next, edit)) return;
    const crop = { crop: next.crop, cropRegion: next.crop === "original" || !next.cropRegion ? undefined : { ...next.cropRegion } };
    setEdit(previous => ({ ...previous, ...crop }));
    cropAutosave.enqueue({ picture, crop });
  }
  async function resetEffects(feature: "All" | "Upscale" | "Layers") {
    if (!picture || published || busy || processing || cropAutosave.pending) return;
    if (separation.dirty) { toast("피사체 다시 분리를 누르거나 선택 초기화를 눌러 주세요."); return; }
    setBusy(true);
    try {
      const nextEdit = feature === "All" ? { ...defaultEdit }
        : { ...edit, ...(feature === "Upscale" ? { scale: 1 } : { mouseEnabled: false, gyroEnabled: false, foreground: true,
            ...(hasCharacterSeparation ? { liveEnabled: false } : {}) }) };
      const next = cloud && picture.projectId ? await resetProjectEffects(picture, feature)
        : { ...picture, ...(feature !== "Upscale" ? { separationDisabled: true, background: undefined, foregrounds: [], kind: undefined, features: [] } : {}) };
      update(value => ({ ...value, images: value.images.map(p => p.id === picture.id ? next : p),
        assets: value.assets?.map(p => p.id === picture.id ? next : p) }));
      setEdit(nextEdit); setSelectedLayer("all"); setJob(null);
      toast(feature === "All" ? "모든 이미지 편집을 초기화하고 원본으로 복구했습니다."
        : feature === "Upscale" ? "업스케일 적용을 취소했습니다."
        : hasCharacterSeparation ? "Live 2D 적용을 취소했습니다." : "배경 분리와 움직임 적용을 취소했습니다.");
    } catch (error) { toast((error as Error).message); }
    finally { setBusy(false); }
  }
  const exportEdit = isCharacterProject && (edit.mouseEnabled || edit.gyroEnabled) && !edit.rig?.enabled
    ? { ...edit, rig: { ...normalizeRig(edit.rig), enabled: true } } : edit;
  const draft = picture ? { ...picture, name: name.trim(), tags, platformTags, genre: (tags[0] || unspecifiedTag) as Genre, edit: exportEdit } : null;
  async function save(silent = false) {
    if (!picture || published || saving.current || busy) return false;
    if (cropAutosave.pending) { toast("자르기 자동 저장을 마친 뒤 다시 시도해 주세요."); return false; }
    if (separation.dirty) { toast("피사체 다시 분리를 눌러 선택 영역을 먼저 적용해 주세요."); return false; }
    if (!name.trim()) { toast("이미지 이름을 입력해 주세요."); return false; }
    saving.current = true;
    setBusy(true);
    const snapshot = exportEdit;
    try {
      const savedAt = cloud ? (await saveImageEdit({ ...draft!, edit: snapshot })).savedAt : new Date().toISOString();
      const replace = (p: typeof picture) => p.id === picture.id ? { ...p, isDraft: false, name: name.trim(), tags, platformTags, genre: (tags[0] || unspecifiedTag) as Genre,
        features: imageFeatures({ ...p, edit: snapshot }), edit: snapshot, savedAt } : p;
      const success = update((value) => ({ ...value,
        images: value.images.map(replace), assets: value.assets?.map(replace),
      }));
      if (success && !silent) toast(saved ? "기존 이미지에 수정 사항을 저장했습니다." : "보관함에 저장했습니다.");
      if (success && snapshot !== edit) setEdit(snapshot);
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
  async function createLive2dFromCrop() {
    if (!picture?.projectId || published || !cloud || busy || processing || checkingJob ||
        cropAutosave.pending || separation.dirty || !live2dCropRequired(edit)) return;
    // Keep the original as a saved project; only the COPY bakes in the crop.
    if (!await save(true)) return;
    setBusy(true);
    try {
      const copy = await createCroppedLive2dProject(picture, edit);
      if (!update(value => ({ ...value, images: [copy, ...value.images.filter(p => p.id !== copy.id)] })))
        throw new Error("새 Live2D 프로젝트를 보관함에 표시하지 못했습니다. 보관함을 새로고침해 주세요.");
      setCreatedLive2dProjectId(copy.id);
      toast("선택한 비율을 실제 이미지에 적용한 새 프로젝트를 만들었습니다. Live2D 탭에서 파츠 분리를 실행해 주세요.");
    } catch (error) { toast((error as Error).message); }
    finally { setBusy(false); }
  }
  async function makeEditableCopy() {
    if (!picture?.assetId || picture.projectId || copyingAsset || busy) return;
    setCopyingAsset(true);
    try {
      const copy = await prepareEditableProject(picture);
      if (update(value => ({ ...value, images: [copy, ...value.images] })))
        navigate(`/app/studio?id=${encodeURIComponent(copy.id)}`, true);
    } catch (error) { toast((error as Error).message); }
    finally { setCopyingAsset(false); }
  }
  async function startProcessing() {
    if (!picture || published || busy || processing || checkingJob || cropAutosave.pending || separationConflict ||
        separation.tracing || separation.drawing || separation.polygonPoints) return;
    if (tool === "upscale" && !resolution) return;
    if (tool === "layers") {
      if (isFox) { await applySubjectEdits(); return; }
      setSelectedLayer("all"); separation.setView("overlay");
    }
    if (tool !== "layers" && separation.dirty) return;
    setBusy(true); setStartingProcessing(true);
    try {
      const value = await processProject(
        picture,
        tool === "upscale" ? "Upscale" : tool === "live" ? "CharacterLayers" : "Layers",
        tool === "upscale" ? resolution : undefined,
      );
      subjectJob.current = null;
      setJob(value);
      window.dispatchEvent(new Event("tokens-updated"));
    } catch (e) {
      setJob(null); setCheckingJob(true);
      toast((e as Error).message);
    } finally {
      setBusy(false); setStartingProcessing(false);
    }
  }
  async function applySubjectEdits() {
    if (!cloud || !picture?.projectId || !isFox || capabilities?.layers === false || published || busy || processing || cropAutosave.pending || !separation.ready || !separation.dirty || separation.drawing || separation.tracing || separation.polygonPoints) return;
    setSelectedLayer("all"); separation.setView("overlay");
    setBusy(true); setStartingProcessing(true);
    try {
      const value = await refineSubject(picture, await separation.exportSelection());
      subjectJob.current = value.jobId;
      setJob(value);
      window.dispatchEvent(new Event("tokens-updated"));
    } catch (error) { setJob(null); setCheckingJob(true); toast((error as Error).message); }
    finally { setBusy(false); setStartingProcessing(false); }
  }
  async function cancelProcessing() {
    if (!picture?.projectId || !job || !processing || cancelling) return;
    setCancelling(true);
    try {
      const result = await post<Job>(`/api/projects/${picture.projectId}/jobs/${job.jobId}/cancel`, {});
      setJob(result); subjectJob.current = null; setCancelPrompt(false);
      window.dispatchEvent(new Event("tokens-updated"));
      toast("작업을 취소했습니다. 사용한 토큰은 반환되지 않습니다.");
    } catch (error) {
      toast((error as Error).message); setCancelPrompt(false);
    } finally { setCancelling(false); }
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
          setCancelPrompt(false);
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
            window.dispatchEvent(new Event("tokens-updated"));
            toast(status.operation === "Layers" ? refined ? "선택한 피사체와 배경 레이어를 갱신했습니다." : "분리 결과를 확인하고 중앙 이미지에서 바로 수정해 주세요." : "이미지 처리를 완료했습니다.");
          }
        } else {
          setJob(status);
          if (status.status === "Failed")
            toast(status.errorMessage || "이미지 처리에 실패했습니다.");
          if (status.status === "Cancelled") toast("이미지 처리 작업이 취소됐습니다.");
          if (["Failed", "Cancelled"].includes(status.status)) { setCancelPrompt(false); window.dispatchEvent(new Event("tokens-updated")); }
        }
      } catch (e) {
        if (alive) {
          toast(`작업 상태를 다시 확인하고 있습니다. ${(e as Error).message}`);
        }
      } finally {
        polling = false;
      }
    };
    const probe = async () => {
      if (polling) return;
      polling = true;
      try {
        const status = await request<Job>(`/api/projects/${picture.projectId}/processing-status`);
        if (alive) { if (["Queued", "Processing"].includes(status.status)) setJob(status); setCheckingJob(false); }
      } catch (error) {
        if (alive && error instanceof ApiError && error.status === 404) setCheckingJob(false);
      } finally { polling = false; }
    };
    if (!job && checkingJob) void probe();
    const timer =
      job && ["Queued", "Processing"].includes(job.status)
        ? setInterval(() => void poll(), 2200)
        : !job && checkingJob ? setInterval(() => void probe(), 2200) : null;
    return () => {
      alive = false;
      if (timer) clearInterval(timer);
    };
  }, [picture?.id, job?.jobId, job?.status, checkingJob]);
  async function download() {
    if (!picture || busy || processing || downloading || separation.dirty || cropAutosave.pending) return;
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
  if (picture.isReadOnly) return <Empty text="다른 사용자의 저장 이미지는 편집할 수 없어요">
    <Link to="/app/images" className="button primary">내 이미지로 이동</Link>
  </Empty>;
  return (
    <div className="studio-layout studio-editor">
      <h1 className="visually-hidden">{picture.name} 작업실</h1>
      {cloud && picture.assetId && !picture.projectId && <div className="studio-asset-copy-notice" role="status">
        <p>보관함에 저장된 완성 이미지입니다. 업스케일·배경 분리·Live2D 파츠 분리에는 편집용 프로젝트가 필요합니다. 원본을 유지하고 편집본을 만들어 주세요.</p>
        <button type="button" className="button secondary" disabled={copyingAsset || busy}
          onClick={() => void makeEditableCopy()}>{copyingAsset ? <Busy /> : "편집용 복사본 만들기"}</button>
      </div>}
      <div className="studio-document-bar">
        <div className="studio-document-name"><ImageIcon size={20} />
          <input aria-label="사진 이름" title="사진 이름 변경" maxLength={160} value={name}
            disabled={published || busy || processing} onChange={event => setName(event.target.value)} />
        </div>
        <div className="studio-document-actions">
          <button aria-label="편집 초기화" title="업스케일·배경 분리·색감·자르기·움직임을 초기화하고 원본으로 복구" disabled={published || busy || processing || separation.dirty || cropAutosave.pending} onClick={() => void resetEffects("All")}>
            <RotateCcw size={17} /><span>편집 초기화</span>
          </button>
          <button aria-label="이미지 정보" title="이미지 정보" onClick={() => setDialog("info")}><Info size={18} /><span>이미지 정보</span></button>
        </div>
      </div>
      <div className="tool-tabs studio-tool-dock" role="tablist" aria-label="이미지 편집 도구" aria-orientation={compact ? "horizontal" : "vertical"}>
        {tools.map(({ id, name, icon: Icon }) => (
          <button key={id} id={`studio-tool-${id}`} type="button" role="tab"
            disabled={busy || processing || checkingJob || cropAutosave.pending || separation.drawing || separation.tracing || separation.polygonPoints > 0}
            aria-controls="studio-properties" aria-selected={tool === id}
            tabIndex={tool === id ? 0 : -1}
            onKeyDown={(event) => {
              if (busy || processing || checkingJob || cropAutosave.pending) return;
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
      <StudioCanvas picture={draft || picture} edit={edit} tool={tool} layered={isFox || isCharacterProject} motion={motion} characterRig={characterRig}
        separation={separation} published={published}
        onEdit={changeCrop} readOnly={published || busy || processing}
        selectedLayer={selectedLayer} selectedCharacterLayerId={activeLivePartId}
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
                  disabled={!canSelectUpscaleResolution(edge, longest)}
                  onClick={() => setUpscaleSelection({ pictureId: picture.id, jobId: picture.jobId, edge })}>
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
        {(tool === "upscale" || tool === "layers" || tool === "live") && (
          <div className="cloud-processing">
            <button
              className={`button primary full ${tool === "layers" ? "separation-action" : ""}`}
              disabled={
                busy || checkingJob || cropAutosave.pending || !!separationConflict ||
                !cloud ||
                !picture.projectId ||
                processing ||
                (tool !== "layers" && separation.dirty) ||
                separation.tracing || separation.drawing || separation.polygonPoints > 0 ||
                (tool === "layers" && (!separation.ready || isFox && !separation.dirty)) ||
                (tool === "layers" && capabilities?.layers === false) ||
                (tool === "live" && !separationAvailable("character", capabilities)) ||
                (tool === "upscale" && capabilities?.upscale === false) ||
                (tool === "upscale" && !resolution)
              }
              onClick={() => void startProcessing()}
            >
              {busy ||
              (job && ["Queued", "Processing"].includes(job.status)) ? (
                <Busy />
              ) : (
                tool === "layers" ? <Scan size={17} /> : tool === "live" ? <Layers3 size={17} /> : <Expand size={17} />
              )}{" "}
              {tool === "upscale"
                ? "업스케일"
                : tool === "live" ? "파츠 분리 시작" : isFox ? "피사체 다시 분리" : "피사체 자동 분리"}
            </button>
            <TokenUsageHelp />
            {tool === "layers" && !separationConflict && <button className="button secondary full effect-cancel" type="button"
              disabled={!picture.projectId || !isFox || separation.dirty} onClick={() => void resetEffects("Layers")}>
              <RotateCcw size={16} />배경 분리 적용 취소
            </button>}
            {separationConflict && <Notice>{separationConflict}</Notice>}
            {tool === "layers" && separation.error && <p className="mask-error" role="alert">{separation.error}</p>}
            {tool === "upscale" && <button className="button secondary full effect-cancel" type="button"
              disabled={busy || processing || !picture.projectId || !picture.jobId}
              onClick={() => void resetEffects("Upscale")}>
              <RotateCcw size={16} />업스케일 적용 취소
            </button>}
            {tool === "live" && hasCharacterSeparation && <button className="button secondary full effect-cancel" type="button"
              disabled={!picture.projectId || checkingJob || cropAutosave.pending || separation.dirty}
              onClick={() => void resetEffects("Layers")}>
              <RotateCcw size={16} />Live 2D 적용 취소
            </button>}
            {tool === "layers" && capabilities?.layers === false && <Notice>배경 분리 서비스 연결을 준비하고 있습니다.</Notice>}
            {job && job.status !== "Completed" && (
              <div className="job-state" role="status" aria-live="polite">
                <p>{processingStatus(job)}</p>
              </div>
            )}
          </div>
        )}
        </fieldset>
        {tool === "layers" && !separationConflict && <>
          {!published && <SubjectSeparationControls editor={separation} disabled={busy || processing} hasResult={isFox}
            selectTool={() => { setSelectedLayer("all"); separation.setView("overlay"); }} />}
          <LayerPreviewSelector value={selectedLayer} change={value => { setSelectedLayer(value); if (value !== "background") separation.setView(value === "subject" ? "cutout" : "overlay"); }}
            ready={isFox} original={picture.src} background={backgroundSrc} foregrounds={foregroundSrcs} layers={picture.layers}
            edit={edit} width={sourceWidth} height={sourceHeight} />
          <fieldset className="photo-settings" disabled={published || busy || processing || separation.dirty}>
            <LayerMotionControls edit={edit} ready={isFox} change={(key, value) => { change(key, value); separation.setView("motion"); }}
              gyroPending={motion.gyroPending} toggleGyro={() => { separation.setView("motion"); void toggleGyro(); }} />
          </fieldset>
        </>}
        {tool === "live" && <div className="studio-live-controls">
          {isCharacterProject && <section className="studio-live-layer-list">
            <h3>분리된 레이어 <span>{picture.layers?.length ?? 0}개</span></h3>
            <div className="studio-live-layer-items" role="group" aria-label="캐릭터 레이어 선택">
              <button type="button" className={`layer-row layer-choice ${activeLivePartId === null ? "active" : ""}`}
                aria-pressed={activeLivePartId === null} onClick={() => setRequestedLivePartId(null)}>
                <span className="layer-thumb checker">{livePreviewLayers(picture.layers ?? [], null).map(layer =>
                  <img key={layer.id} src={layer.imageUrl} alt="" />)}</span>
                <span>전체</span>
                {activeLivePartId === null && <Check size={17} aria-hidden="true" />}
              </button>
              {livePreviewLayers(picture.layers ?? [], null).reverse().map(layer =>
                <button type="button" className={`layer-row layer-choice ${activeLivePartId === layer.id ? "active" : ""}`}
                  aria-pressed={activeLivePartId === layer.id} key={layer.id}
                  onClick={() => setRequestedLivePartId(layer.id)} title={`${layerLabel(layer.layerType)} 파츠만 보기`}>
                  <span className="layer-thumb checker"><img src={layer.imageUrl} alt="" loading="lazy" /></span>
                  <span>{layerLabel(layer.layerType)}</span><small>{layer.layerOrder + 1}</small>
                  {activeLivePartId === layer.id && <Check size={17} aria-hidden="true" />}
                </button>)}
            </div>
          </section>}
          {((isCharacterProject && !cloud) || (picture.projectId && (isCharacterProject || isFox))) && <div className="studio-layer-stack-entry">
            <button type="button" className="button secondary full" disabled={busy || processing || published}
              onClick={() => setDialog("layerStack")}><Layers3 size={17} /> 수동 레이어 조합</button>
            <p>레이어 겹침이 어색하다면 파츠를 직접 수정해보세요.</p>
          </div>}
          {isCharacterProject && <section className="studio-rig-controls" aria-label="기기별 Live2D 움직임 입력">
            <h3>배경화면 움직임 입력</h3>
            <MotionInputButtons edit={edit} ready={!published && !busy && !processing}
              change={changeMotionInput} gyroPending={motion.gyroPending} toggleGyro={() => void toggleGyro()} />
          </section>}
          {isCharacterProject && <CharacterMotionControls rig={edit.rig} onChange={next => change("rig", next)}
            disabled={published || busy || processing} />}
        </div>}
        <fieldset className="photo-settings" disabled={published || busy || processing}>
        {tool === "crop" && (
          <>
          <CropControls edit={edit} width={sourceWidth} height={sourceHeight} change={changeCrop}
            saving={cropAutosave.pending} error={cropAutosave.error} />
          {cloud && picture.projectId && <section className="studio-followup-action" aria-label="자른 이미지로 Live2D 진행">
            <h3>선택한 비율로 Live2D 분리</h3>
            <p>현재 이미지(업스케일 완료본 포함)에서 지정한 영역을 실제로 잘라 새 프로젝트로 만듭니다. 원본과 업스케일 결과는 남고, 새 프로젝트에서는 자르기가 중복 적용되지 않습니다.</p>
            <button type="button" className="button secondary full"
              disabled={published || busy || checkingJob || processing || cropAutosave.pending || separation.dirty || !live2dCropRequired(edit) || !name.trim()}
              onClick={() => void createLive2dFromCrop()}>
              {busy ? <Busy /> : <Layers3 size={16} />}
              자른 이미지로 Live2D 시작
            </button>
            <p>{!live2dCropRequired(edit) ? "먼저 16:9 PC, 9:16 모바일 또는 사용자 비율을 선택해 주세요." : "새 프로젝트에서 분리를 실행하면 Live2D 최초 실행 토큰(7개)이 사용됩니다."}</p>
          </section>}
          </>
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
          <h3>상위 태그</h3>
          <PlatformTagOptions selected={platformTags} change={setPlatformTags} />
          <h3>하위 태그</h3>
          <label className="studio-tag-all"><input ref={allTags} type="checkbox" checked={allTagsSelected(tags)}
            onChange={event => setTags(event.target.checked ? [...selectableTags] : [])} /><span>전체 선택</span></label>
          <div className="studio-tag-options" role="group" aria-label="하위 태그">
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
              disabled={busy || processing || cropAutosave.pending} onClick={event => void share(event)}
            >
              <Share2 size={18} /> 공유
            </button>
            <button
              className="button secondary"
              disabled={busy || processing || separation.dirty || cropAutosave.pending}
              aria-busy={downloading}
              onClick={download}
            >
              <Download size={18} />{downloading ? "다운로드 준비 중…" : "다운로드"}
            </button>
          </div>
        </div>
          <button
            className="button primary full"
            disabled={busy || processing || published || separation.dirty || cropAutosave.pending || (saved && !dirty) || !name.trim()}
            onClick={() => void save()}
          >
            <Save size={17} />보관함에 저장
          </button>
          <button className="button secondary full" disabled={busy || processing || separation.dirty || cropAutosave.pending || !name.trim()} onClick={() => void uploadAndSave()}>
            <Upload size={18} />{published ? "업로드 해제" : "업로드하고 저장"}
          </button>
          <button
            className="button secondary full"
            disabled={!options.length || busy || processing || cropAutosave.pending}
            onClick={() => { if (!saved || dirty) toast("보관함에 저장한 뒤 기기에 적용해 주세요."); else setDialog("apply"); }}
          >
            <Monitor size={18} /> 연결된 기기에 적용
          </button>
        </div>
        </div>
      </aside>
      {(startingProcessing || processing || checkingJob) && <Modal title={cancelPrompt ? "작업을 취소할까요?" : "이미지 처리 중"}
        close={() => {}} dismissible={false}>
        {cancelPrompt ? <>
          <p className="studio-cancel-warning" role="alert">작업을 취소하면 결과를 받을 수 없으며, 사용한 토큰은 반환되지 않습니다.</p>
          <div className="studio-processing-actions">
            <button type="button" className="button secondary" disabled={cancelling} onClick={() => setCancelPrompt(false)}>작업 계속하기</button>
            <button type="button" className="button danger" disabled={cancelling} onClick={() => void cancelProcessing()}>{cancelling ? <Busy /> : null}토큰 반환 없이 취소</button>
          </div>
        </> : <>
          <p className="studio-processing-status" role="status" aria-live="polite"><Busy />{job && processing ? processingStatus(job) : checkingJob ? "진행 중인 작업을 확인하고 있습니다." : "작업을 시작하고 있습니다."}</p>
          {processing && <progress className="studio-processing-progress" max={100} value={job?.progress || 0} aria-label="이미지 처리 진행률" />}
          <p className="notice">작업이 끝날 때까지 다른 기능이나 화면으로 전환할 수 없습니다.</p>
          <button type="button" className="button secondary full" disabled={!processing || startingProcessing || cancelling} onClick={() => setCancelPrompt(true)}>작업 취소</button>
        </>}
      </Modal>}
      {shareFeedback && <div className="studio-share-feedback" role="status" aria-live="polite"
        style={{ left: shareFeedback.x, top: shareFeedback.y - 18 }}>{shareFeedback.text}</div>}
      {dialog === "layerStack" && <LayerStackEditor
        layers={picture.layers ?? []}
        partAssignments={isCharacterProject ? normalizeRig(edit.rig).partAssignments : undefined}
        close={() => setDialog(null)}
        onSave={async (order, assignments) => {
          if (order) {
            let refreshed = picture;
            if (cloud && picture.projectId) {
              await request(`/api/projects/${picture.projectId}/layers/order`, {
                method: "PUT", body: JSON.stringify({ layerIds: order }),
              });
              refreshed = await detailPicture(picture);
            } else {
              const byId = new Map(picture.layers?.map(layer => [layer.id, layer]));
              refreshed = { ...picture, layers: order.map((id, layerOrder) => ({ ...byId.get(id)!, layerOrder })) };
            }
            if (!update(value => ({ ...value,
              images: value.images.map(item => item.id === refreshed.id ? refreshed : item),
              assets: value.assets?.map(item => item.id === refreshed.id ? refreshed : item),
            }))) throw new Error("레이어 합성 순서를 표시하지 못했습니다.");
            setSelectedLayer("all");
          }
          if (assignments) setEdit(previous => ({ ...previous, rig: { ...normalizeRig(previous.rig), partAssignments: assignments } }));
          setRequestedLivePartId(null);
          if (order) toast("레이어 합성 순서를 저장했어요.");
        }} />}
      {dialog === "info" && <ImageInformation picture={picture}
        layerCount={picture.layers?.length ?? (isFox ? foregroundSrcs.length + 1 : 1)} close={() => setDialog(null)} />}
      {dialog === "apply" && draft && <ApplyWallpaperDialog picture={draft} devices={library.devices}
        busy={busy} pcMotionAvailable close={() => setDialog(null)}
        confirm={async ({ picture: ready, deviceId, displayId, frame, pcMotion }) => {
          setBusy(true);
          try {
            if (await apply(ready, deviceId, displayId, { frame, enablePcLiveMotion: pcMotion })) setDialog(null);
          } finally { setBusy(false); }
        }} />}
    </div>
  );
}
