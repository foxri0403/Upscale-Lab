import { useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { ArrowDown, ArrowUp, Check, Play, RotateCcw, Save, Square, Undo2 } from "lucide-react";
import type { ApiLayer } from "./api";
import { rigMovementGroup, rigMovementGroups, rigPart, toggleRigMovementGroup, type RigMovementGroup, type RigPart } from "./character-rig";
import type { HeadRig } from "./model";
import { Modal, Busy } from "./ui";
import { automaticLayerIds, currentLayerIds, isCompleteStack, moveLayer } from "./layer-stack";
import "./layer-stack-editor.css";
import { layerLabel } from "./layer-label";
import { selectedLivePartId } from "./live-layer-preview";
import { useCharacterRig } from "./use-character-rig";

type PartAssignments = HeadRig["partAssignments"];

export function LayerStackEditor({ layers, rig, close, onSave }: {
  layers: ApiLayer[];
  rig?: HeadRig;
  close: () => void;
  onSave: (order?: string[], assignments?: PartAssignments) => Promise<void>;
}) {
  const partAssignments = rig?.partAssignments;
  const [initial] = useState(() => currentLayerIds(layers));
  const [order, setOrder] = useState(initial);
  const [assignments, setAssignments] = useState(partAssignments);
  const [history, setHistory] = useState<string[][]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [stageRatio, setStageRatio] = useState(1);
  const [requestedPreviewPartId, setRequestedPreviewPartId] = useState<string | null>(null);
  const [activeGroup, setActiveGroup] = useState<RigMovementGroup | null>(null);
  const [testing, setTesting] = useState(false);
  const partsList = useRef<HTMLDivElement>(null);
  const allLayersRow = useRef<HTMLButtonElement>(null);
  const movedPart = useRef<string | null>(null);

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
  const groupById = new Map(layers.map(layer => [layer.id, rigMovementGroup(rigPart(layer.layerType, layer.id, assignments))]));
  const selectedGroup = rigMovementGroups.find(group => group.id === activeGroup);
  const previewIds = selectedLayer && !selectedGroup ? [selectedLayer.id] : order;
  const topFirst = [...order].reverse();
  const motion = useCharacterRig(testing && !!selectedGroup && !saving, rig && { ...rig, enabled: true, partAssignments: assignments ?? {} },
    `${activeGroup ?? ""}:${previewIds.join(",")}`, undefined, true);

  useLayoutEffect(() => {
    const list = partsList.current;
    const all = allLayersRow.current;
    const id = movedPart.current ?? previewPartId;
    movedPart.current = null;
    const row = id ? list?.querySelector(`[data-layer-id="${CSS.escape(id)}"]`) : null;
    if (!list || !all || !row) return;
    const item = row.getBoundingClientRect();
    const top = all.getBoundingClientRect().bottom;
    const bottom = list.getBoundingClientRect().bottom;
    if (item.top < top) list.scrollTop -= top - item.top;
    else if (item.bottom > bottom) list.scrollTop += item.bottom - bottom;
  }, [order, previewPartId]);

  function showOrder(next: string[]) {
    setOrder(next);
    setError("");
  }
  function changeOrder(next: string[]) {
    setHistory(previous => [...previous.slice(-63), order]);
    showOrder(next);
  }
  function move(id: string, offset: -1 | 1) {
    movedPart.current = id;
    if (!selectedGroup) setRequestedPreviewPartId(null);
    changeOrder(moveLayer(order, id, offset));
  }
  function toggleGroupPart(layer: ApiLayer, group: RigMovementGroup) {
    setAssignments(previous => ({ ...previous, [layer.id]: toggleRigMovementGroup(group,
      rigPart(layer.layerType, layer.id, previous), rigPart(layer.layerType, layer.id, partAssignments), rigPart(layer.layerType)) }));
  }
  function selectGroup(group: RigMovementGroup | null) {
    setActiveGroup(group);
    if (!group) setTesting(false);
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
        <div className={`layer-stack-columns${assignments ? " with-groups" : ""}`}>
          <div className="layer-stack-stage checker" role="img" ref={motion.stage}
            style={{ aspectRatio: stageRatio, "--layer-stack-ratio": stageRatio } as CSSProperties}
            aria-label={selectedGroup ? `${selectedGroup.label} 그룹 미리보기` : selectedLayer ? `${selectedName} 파츠 미리보기` : "레이어 합성 미리보기"}>
            <div className="layer-stack-preview-plane" ref={motion.layers}>
              {previewIds.map(id => {
                const layer = indexed.get(id);
                const inGroup = selectedGroup && groupById.get(id) === selectedGroup.id;
                return layer && <img key={id} src={layer.imageUrl} alt="" draggable={false}
                  className={selectedGroup && !inGroup ? "layer-stack-dimmed" : undefined}
                  data-rig-part={inGroup ? rigPart(layer.layerType, id, assignments) : undefined}
                  onLoad={event => {
                    const { naturalWidth, naturalHeight } = event.currentTarget;
                    if (id === previewIds[0] && naturalHeight) setStageRatio(naturalWidth / naturalHeight);
                  }} />;
              })}
            </div>
          </div>
          <section className="layer-stack-parts" aria-label="파츠 선택">
            <h3>파츠 선택</h3>
            <div className="layer-stack-items" ref={partsList} role="group" aria-label="레이어 목록">
              <button type="button" className="layer-stack-item layer-stack-all" ref={allLayersRow}
                aria-pressed={!selectedLayer && !selectedGroup} disabled={saving} onClick={() => {
                  setRequestedPreviewPartId(null);
                  selectGroup(null);
                }}>
                <span className="layer-stack-thumb layer-stack-composite checker">{order.map(id => {
                  const layer = indexed.get(id);
                  return layer && <img key={id} src={layer.imageUrl} alt="" />;
                })}</span>
                <span>전체 레이어</span>
              </button>
              {topFirst.map((id, index) => {
                const layer = indexed.get(id);
                if (!layer) return null;
                const position = order.length - index;
                const name = `${layerLabel(layer.layerType)} ${position}`;
                return <div className="layer-stack-part-row" key={id} data-layer-id={id}>
                  <span className="layer-stack-position">{position}</span>
                  <button type="button" className="layer-stack-item" aria-label={`${name} 파츠 미리보기`} aria-pressed={previewPartId === id}
                    disabled={saving} onClick={() => setRequestedPreviewPartId(previewPartId === id ? null : id)}>
                    <span className="layer-stack-thumb checker"><img src={layer.imageUrl} alt="" loading="lazy" /></span>
                    <span>{layerLabel(layer.layerType)}</span>
                  </button>
                  <div className="layer-stack-order" role="group" aria-label={`${name} 순서`}>
                    <button type="button" aria-label={`${name} 앞쪽으로 한 칸`} title="앞쪽으로 한 칸"
                      disabled={saving || index === 0 || id === background?.id}
                      onClick={() => move(id, 1)}><ArrowUp size={16} /></button>
                    <button type="button" aria-label={`${name} 뒤쪽으로 한 칸`} title="뒤쪽으로 한 칸"
                      disabled={saving || position <= (background ? 2 : 1)}
                      onClick={() => move(id, -1)}><ArrowDown size={16} /></button>
                  </div>
                </div>;
              })}
            </div>
          </section>
          {assignments && <section className="layer-stack-groups" aria-label="움직임 그룹 수정">
            <h3>움직임 그룹 수정</h3>
            <div className="layer-stack-group-options" role="group" aria-label="미리 볼 움직임 그룹">
              {rigMovementGroups.map(group => <button key={group.id} type="button" aria-pressed={activeGroup === group.id}
                disabled={saving} onClick={() => selectGroup(activeGroup === group.id ? null : group.id)}>
                <span>{group.label}</span>
                <small>{[...groupById.values()].filter(groupId => groupId === group.id).length}개</small>
              </button>)}
            </div>
            {selectedGroup && <section className="layer-stack-members" aria-label={`${selectedGroup.label} 그룹 파츠 선택`}>
              <h3>파츠 선택</h3>
              <div className="layer-stack-member-list" role="group" aria-label={`${selectedGroup.label} 그룹 포함 여부`}>
                {topFirst.map((id, index) => {
                  const layer = indexed.get(id);
                  if (!layer) return null;
                  const name = `${layerLabel(layer.layerType)} ${order.length - index}`;
                  const included = groupById.get(id) === selectedGroup.id;
                  return <button type="button" key={id} title={name} aria-label={name} aria-pressed={included}
                    disabled={saving} onClick={() => toggleGroupPart(layer, selectedGroup.id)}>
                    <span className="layer-stack-thumb checker"><img src={layer.imageUrl} alt="" loading="lazy" /></span>
                    {included && <Check size={12} aria-hidden="true" />}
                  </button>;
                })}
              </div>
            </section>}
            {selectedLayer && !selectedGroup && <label>
              <span>{selectedName}</span>
              <select aria-label={`${selectedName} 움직임 그룹 배정`} disabled={saving}
                value={rigPart(selectedLayer.layerType, selectedLayer.id, assignments)}
                onChange={event => {
                  const part = event.currentTarget.value as RigPart;
                  setAssignments(previous => ({ ...previous, [selectedLayer.id]: part }));
                }}>
                {rigMovementGroups.map(group => <optgroup key={group.id} label={group.label}>
                  {group.parts.map(part => <option key={part.value} value={part.value}>{part.label}</option>)}
                </optgroup>)}
              </select>
            </label>}
            <button className="button secondary layer-stack-motion-test" type="button" aria-pressed={testing} disabled={saving || !selectedGroup}
              title={!selectedGroup ? "움직임 그룹을 먼저 선택해 주세요" : undefined}
              onClick={() => setTesting(value => !value)}>
              {testing ? <Square size={15} /> : <Play size={15} />}{testing ? "테스트 중지" : "움직임 테스트"}
            </button>
          </section>}
        </div>
        <div className="layer-stack-footer">
          <div className="layer-stack-tools">
            <button type="button" aria-label="되돌리기" title="되돌리기" disabled={!history.length || saving}
              onClick={() => {
                showOrder(history[history.length - 1]);
                setHistory(previous => previous.slice(0, -1));
              }}><Undo2 size={16} /><span>되돌리기</span></button>
            <button type="button" aria-label="AI 순서로 복원" title="AI 순서로 복원"
              disabled={saving || automatic.join(",") === order.join(",")}
              onClick={() => changeOrder(automatic)}><RotateCcw size={16} /><span>AI 순서로 복원</span></button>
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
