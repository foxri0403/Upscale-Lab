import { useMemo, useState, type CSSProperties } from "react";
import { ArrowDown, ArrowUp, ChevronLeft, ChevronRight, RotateCcw, Save, Undo2 } from "lucide-react";
import type { ApiLayer } from "./api";
import { assignableRigParts, rigPart, type RigPart } from "./character-rig";
import type { HeadRig } from "./model";
import { Modal, Busy } from "./ui";
import { automaticLayerIds, currentLayerIds, isCompleteStack, moveLayer } from "./layer-stack";
import "./layer-stack-editor.css";
import { layerLabel } from "./layer-label";
import { selectedLivePartId } from "./live-layer-preview";
import { useMediaQuery } from "./use-media-query";

type PartAssignments = HeadRig["partAssignments"];

export function LayerStackEditor({ layers, partAssignments, close, onSave }: {
  layers: ApiLayer[];
  partAssignments?: PartAssignments;
  close: () => void;
  onSave: (order?: string[], assignments?: PartAssignments) => Promise<void>;
}) {
  const [initial] = useState(() => currentLayerIds(layers));
  const [order, setOrder] = useState(initial);
  const [assignments, setAssignments] = useState(partAssignments);
  const [history, setHistory] = useState<string[][]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [stageRatio, setStageRatio] = useState(1);
  const [requestedPreviewPartId, setRequestedPreviewPartId] = useState<string | null>(null);
  const [partIndex, setPartIndex] = useState(0);
  const partsPerPage = useMediaQuery("(max-height: 550px)") ? 3 : 5;

  const indexed = useMemo(() => new Map(layers.map(layer => [layer.id, layer])), [layers]);
  const previewPartId = selectedLivePartId(layers, requestedPreviewPartId);
  const selectedLayer = previewPartId ? indexed.get(previewPartId) : undefined;
  const selectedIndex = selectedLayer ? order.indexOf(selectedLayer.id) : -1;
  const selectedName = selectedLayer ? `${layerLabel(selectedLayer.layerType)} · ${selectedIndex + 1}` : "파츠 선택";
  const background = layers.find(layer => layer.layerType === "Background");
  const complete = isCompleteStack(order, layers) && (!background || order[0] === background.id);
  const orderChanged = order.join(",") !== initial.join(",");
  const rolesChanged = assignments !== undefined && layers.some(layer =>
    rigPart(layer.layerType, layer.id, assignments) !== rigPart(layer.layerType, layer.id, partAssignments));
  const changed = orderChanged || rolesChanged;
  const automatic = automaticLayerIds(layers);
  const previewIds = selectedLayer ? [selectedLayer.id] : order;
  const page = Math.floor(partIndex / partsPerPage);
  const pageCount = Math.ceil(order.length / partsPerPage);
  const pageIds = [...order].reverse().slice(page * partsPerPage, (page + 1) * partsPerPage);

  function showOrder(next: string[]) {
    setOrder(next);
    if (selectedLayer) {
      const index = next.indexOf(selectedLayer.id);
      if (index >= 0) setPartIndex(next.length - 1 - index);
    }
    setError("");
  }
  function changeOrder(next: string[]) {
    setHistory(previous => [...previous.slice(-63), order]);
    showOrder(next);
  }
  function safeClose() {
    if (saving) return;
    if (changed && !window.confirm("적용하지 않은 레이어 변경을 버릴까요?")) return;
    close();
  }
  async function save() {
    if (!complete || saving || !changed) return;
    setSaving(true);
    setError("");
    try {
      await onSave(orderChanged ? order : undefined, rolesChanged ? assignments : undefined);
      close();
    } catch (e) {
      setError((e as Error).message);
    } finally { setSaving(false); }
  }

  return <Modal title="수동 레이어 조합" close={safeClose} wide className="layer-stack-dialog">
    <div className="layer-stack-editor">
      {layers.length < 2 ?
        <p role="status">조합할 레이어가 없습니다. 먼저 이미지 분리를 완료해 주세요.</p> : <>
        <div className={`layer-stack-columns${assignments ? " with-roles" : ""}`}>
          <div className="layer-stack-stage checker" role="img"
            style={{ aspectRatio: stageRatio, "--layer-stack-ratio": stageRatio } as CSSProperties}
            aria-label={selectedLayer ? `${selectedName} 파츠 미리보기` : "레이어 합성 미리보기"}>
            {previewIds.map(id => {
              const layer = indexed.get(id);
              return layer && <img key={id} src={layer.imageUrl} alt="" draggable={false}
                onLoad={event => {
                  const { naturalWidth, naturalHeight } = event.currentTarget;
                  if (id === previewIds[0] && naturalHeight) setStageRatio(naturalWidth / naturalHeight);
                }} />;
            })}
          </div>
          <section className="layer-stack-parts" aria-label="파츠 선택">
            <h3>파츠 선택</h3>
            <div className="layer-stack-items" role="group" aria-label="레이어 목록">
              {pageIds.map((id, index) => {
                const layer = indexed.get(id);
                if (!layer) return null;
                return <button type="button" className="layer-stack-item" key={id} aria-pressed={previewPartId === id}
                  disabled={saving} onClick={() => setRequestedPreviewPartId(previewPartId === id ? null : id)}>
                  <span className="layer-stack-thumb checker"><img src={layer.imageUrl} alt="" /></span>
                  <span>{layerLabel(layer.layerType)} · {order.length - page * partsPerPage - index}</span>
                </button>;
              })}
            </div>
            <div className="layer-stack-part-actions">
              {pageCount > 1 && <div className="layer-stack-pages" role="group" aria-label="파츠 목록 페이지">
                <button type="button" aria-label="이전 파츠 목록" disabled={page === 0 || saving}
                  onClick={() => setPartIndex((page - 1) * partsPerPage)}><ChevronLeft size={16} /></button>
                <span aria-live="polite">{page + 1} / {pageCount}</span>
                <button type="button" aria-label="다음 파츠 목록" disabled={page >= pageCount - 1 || saving}
                  onClick={() => setPartIndex((page + 1) * partsPerPage)}><ChevronRight size={16} /></button>
              </div>}
              <div className="layer-stack-order" role="group" aria-label="선택한 파츠의 순서">
                <button type="button" aria-label="앞쪽으로 한 칸" title="앞쪽으로 한 칸"
                  disabled={!selectedLayer || saving || selectedIndex >= order.length - 1 || selectedLayer.id === background?.id}
                  onClick={() => { if (selectedLayer) changeOrder(moveLayer(order, selectedLayer.id, 1)); }}><ArrowUp size={16} /></button>
                <button type="button" aria-label="뒤쪽으로 한 칸" title="뒤쪽으로 한 칸"
                  disabled={!selectedLayer || saving || selectedIndex <= (background ? 1 : 0)}
                  onClick={() => { if (selectedLayer) changeOrder(moveLayer(order, selectedLayer.id, -1)); }}><ArrowDown size={16} /></button>
              </div>
            </div>
          </section>
          {assignments && <fieldset className="layer-stack-roles" disabled={!selectedLayer || saving}>
            <legend>PNG 파츠 역할</legend>
            <label>
              {selectedLayer && <span>{selectedName}</span>}
              <select aria-label={`${selectedName} 움직임 부위`}
                value={selectedLayer ? rigPart(selectedLayer.layerType, selectedLayer.id, assignments) : ""}
                onChange={event => {
                  if (!selectedLayer) return;
                  const part = event.currentTarget.value as RigPart;
                  setAssignments(previous => ({ ...previous, [selectedLayer.id]: part }));
                }}>
                <option value="" disabled>파츠 선택</option>
                {assignableRigParts.map(part => <option key={part.value} value={part.value}>{part.label}</option>)}
              </select>
            </label>
          </fieldset>}
        </div>
        <div className="layer-stack-footer">
          <div className="layer-stack-tools">
            <button type="button" disabled={!history.length || saving}
              onClick={() => {
                showOrder(history[history.length - 1]);
                setHistory(previous => previous.slice(0, -1));
              }}><Undo2 size={16} /> 되돌리기</button>
            <button type="button" disabled={saving || automatic.join(",") === order.join(",")}
              onClick={() => changeOrder(automatic)}><RotateCcw size={16} /> AI 순서로 복원</button>
          </div>
          <div className="layer-stack-save-actions">
            <button type="button" className="button secondary" onClick={safeClose} disabled={saving}>취소</button>
            <button type="button" className="button primary" onClick={() => void save()}
              disabled={saving || !complete || !changed}>
              {saving ? <Busy /> : <Save size={16} />}
              {saving ? "저장 중…" : partAssignments ? "레이어 변경 적용" : "이 순서로 저장"}
            </button>
          </div>
        </div>
        {!complete && <p className="layer-stack-error" role="status">저장할 레이어 순서를 확인해 주세요.</p>}
      </>}
      {error && <p className="layer-stack-error" role="alert">{error}</p>}
    </div>
  </Modal>;
}
