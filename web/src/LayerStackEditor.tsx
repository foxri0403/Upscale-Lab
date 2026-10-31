import { useEffect, useMemo, useState } from "react";
import { ArrowDown, ArrowUp, Check, Layers3, RotateCcw, Save, Undo2 } from "lucide-react";
import { request, type ApiLayer } from "./api";
import { Modal, Busy } from "./ui";
import {
  addLayerToTop, automaticLayerIds, currentLayerIds, isCompleteStack,
  moveLayer, removeLayer, type LayerStack,
} from "./layer-stack";
import "./layer-stack-editor.css";
import { layerLabel } from "./layer-label";

export function LayerStackEditor({ projectId, close, onSaved }: {
  projectId: string;
  close: () => void;
  onSaved: () => Promise<void>;
}) {
  const [layers, setLayers] = useState<ApiLayer[]>([]);
  const [initial, setInitial] = useState<string[]>([]);
  const [stack, setStack] = useState<LayerStack>({ mode: "automatic", ids: [] });
  const [history, setHistory] = useState<LayerStack[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [stageRatio, setStageRatio] = useState("1 / 1");
  const [stageBackground, setStageBackground] = useState<"checker" | "light">("checker");

  useEffect(() => {
    let active = true;
    request<ApiLayer[]>(`/api/projects/${projectId}/layers`)
      .then(data => {
        if (!active) return;
        const ordered = currentLayerIds(data);
        setLayers(data);
        setInitial(ordered);
        setStack({ mode: ordered.join(",") === automaticLayerIds(data).join(",")
          ? "automatic" : "manual", ids: ordered });
        setLoading(false);
      })
      .catch(e => { if (active) { setError((e as Error).message); setLoading(false); } });
    return () => { active = false; };
  }, [projectId]);

  const indexed = useMemo(() => new Map(layers.map(layer => [layer.id, layer])), [layers]);
  const pending = layers.filter(layer => !stack.ids.includes(layer.id));
  const backgroundLayer = layers.find(layer => layer.layerType === "Background");
  const backgroundCorrect = !backgroundLayer || stack.ids[0] === backgroundLayer.id;
  const complete = isCompleteStack(stack.ids, layers) && backgroundCorrect;
  const changed = stack.ids.join(",") !== initial.join(",");

  function changeStack(next: LayerStack) {
    setHistory(previous => [...previous.slice(-63), stack]);
    setStack(next);
    setError("");
  }
  function safeClose() {
    if (saving) return;
    if (changed && !window.confirm("저장하지 않은 레이어 순서 변경을 버릴까요?")) return;
    close();
  }
  async function save() {
    if (!complete || saving || !changed) return;
    setSaving(true);
    setError("");
    try {
      await request<ApiLayer[]>(`/api/projects/${projectId}/layers/order`, {
        method: "PUT", body: JSON.stringify({ layerIds: stack.ids }),
      });
      // Reload project detail so the main canvas, cached URLs and exports use the
      // persisted order (the editor is not the source of truth after saving).
      await onSaved();
      close();
    } catch (e) {
      setError((e as Error).message);
    } finally { setSaving(false); }
  }

  return <Modal title="레이어 조합" close={safeClose} wide>
    <div className="layer-stack-editor">
      <p className="layer-stack-help">자동 합성이 기본값입니다. 직접 수정하려면 <strong>수동 조합</strong>을 선택하고 가장 밑에 둘 파츠부터 차례로 추가하세요.</p>
      {loading ? <div className="layer-stack-loading"><Busy /> 레이어 불러오는 중…</div> : layers.length < 2 ?
        <p role="status">조합할 레이어가 없습니다. 먼저 이미지 분리를 완료해 주세요.</p> : <>
        <div className="layer-stack-tabs" role="group" aria-label="레이어 합성 방식">
          <button type="button" className={stack.mode === "automatic" ? "active" : ""}
            aria-pressed={stack.mode === "automatic"}
            onClick={() => changeStack({ mode: "automatic", ids: automaticLayerIds(layers) })}>
            <Layers3 size={17} /> AI 자동 순서
          </button>
          <button type="button" className={stack.mode === "manual" ? "active" : ""}
            aria-pressed={stack.mode === "manual"}
            onClick={() => changeStack({ mode: "manual", ids: [] })}>
            <ArrowUp size={17} /> 수동 조합 새로 시작
          </button>
        </div>
        <div className="layer-stack-columns">
          <div className="layer-stack-preview">
            <div className="layer-stack-preview-heading">
              <strong>실시간 합성 결과</strong>
              <button type="button" onClick={() => setStageBackground(p => p === "checker" ? "light" : "checker")}>배경 전환</button>
            </div>
            <div className={`layer-stack-stage ${stageBackground}`} style={{ aspectRatio: stageRatio }} role="img" aria-label="레이어 합성 미리보기">
              {stack.ids.map(id => {
                const layer = indexed.get(id);
                return layer && <img key={id} src={layer.imageUrl} alt="" draggable={false}
                  onLoad={event => { if (id === stack.ids[0] && event.currentTarget.naturalHeight)
                    setStageRatio(`${event.currentTarget.naturalWidth} / ${event.currentTarget.naturalHeight}`); }} />;
              })}
              {!stack.ids.length && <p>맨 아래에 놓을 파츠를 선택하세요.</p>}
            </div>
            <span className="layer-stack-count">{stack.ids.length}/{layers.length}개 쌓음 · 아래에서 위로 합성</span>
          </div>
          <div className="layer-stack-lists">
            <section className="layer-stack-list-section">
              <h3>추가할 파츠 <small>({pending.length}개)</small></h3>
              <div className="layer-stack-items" aria-label="추가할 파츠 목록">
                {!pending.length && <p className="layer-stack-empty">모든 파츠를 추가했어요.</p>}
                {pending.map((layer) => <button className="layer-stack-item pending" type="button" key={layer.id}
                  disabled={!!backgroundLayer && !stack.ids.length && layer.id !== backgroundLayer.id}
                  onClick={() => changeStack({ mode: "manual", ids: addLayerToTop(stack.ids, layer.id, layers) })}>
                  <span className="layer-stack-thumb"><img src={layer.imageUrl} alt="" /></span>
                  <span>{layerLabel(layer.layerType)} · {layer.automaticOrder ?? layer.layerOrder}</span>
                  <ArrowUp size={15} aria-hidden="true" />
                  <span className="sr-only">맨 위에 추가</span>
                </button>)}
              </div>
            </section>
            <section className="layer-stack-list-section">
              <h3>쌓은 파츠 <small>({stack.ids.length}개, 맨 위부터 표시)</small></h3>
              <div className="layer-stack-items" aria-label="쌓은 파츠 목록">
                {[...stack.ids].reverse().map((id, reversedIndex) => {
                  const layer = indexed.get(id);
                  if (!layer) return null;
                  const index = stack.ids.length - 1 - reversedIndex;
                  return <div className="layer-stack-item stacked" key={id}>
                    <span className="layer-stack-num">{index + 1}</span>
                    <span className="layer-stack-thumb"><img src={layer.imageUrl} alt="" /></span>
                    <span className="layer-stack-item-name">{layerLabel(layer.layerType)} · {layer.automaticOrder ?? layer.layerOrder}</span>
                    <button type="button" title="앞쪽으로 한 칸" aria-label={`${layerLabel(layer.layerType)} 앞쪽으로 한 칸`}
                      disabled={index === stack.ids.length - 1 || layer.layerType === "Background"}
                      onClick={() => changeStack({ mode: "manual", ids: moveLayer(stack.ids, id, 1) })}><ArrowUp size={15} /></button>
                    <button type="button" title="뒤쪽으로 한 칸" aria-label={`${layerLabel(layer.layerType)} 뒤쪽으로 한 칸`}
                      disabled={index === 0 || (index === 1 && !!backgroundLayer)}
                      onClick={() => changeStack({ mode: "manual", ids: moveLayer(stack.ids, id, -1) })}><ArrowDown size={15} /></button>
                    <button type="button" title="쌓기에서 제거" aria-label={`${layerLabel(layer.layerType)} 제거`}
                      onClick={() => changeStack({ mode: "manual", ids: removeLayer(stack.ids, id) })}>×</button>
                  </div>;
                })}
                {!stack.ids.length && <p className="layer-stack-empty">아직 쌓은 파츠가 없습니다.</p>}
              </div>
            </section>
          </div>
        </div>
        <div className="layer-stack-footer">
          <div className="layer-stack-tools">
            <button type="button" disabled={!history.length || saving}
              onClick={() => {
                setStack(history[history.length - 1]);
                setHistory(h => h.slice(0, -1));
              }}><Undo2 size={16} /> 되돌리기</button>
            <button type="button" disabled={saving}
              onClick={() => changeStack({ mode: "automatic", ids: automaticLayerIds(layers) })}>
              <RotateCcw size={16} /> AI 순서로 복원</button>
          </div>
          <div className="layer-stack-save-actions">
            <button type="button" className="button secondary" onClick={safeClose} disabled={saving}>취소</button>
            <button type="button" className="button primary" onClick={() => void save()}
              disabled={saving || !complete || !changed}>
              {saving ? <Busy /> : <Save size={16} />}
              {saving ? "저장 중…" : "이 순서로 저장"}
              {!changed && complete && <Check size={14} />}
            </button>
          </div>
        </div>
        {!complete && <p className="layer-stack-warning" role="status">
          {backgroundCorrect ? `저장하려면 모든 파츠를 한 번씩 쌓아야 합니다. (${pending.length}개 남음)`
            : "배경 레이어는 가장 아래에 배치해야 합니다."}
        </p>}
      </>}
      {error && <p className="layer-stack-error" role="alert">{error}</p>}
    </div>
  </Modal>;
}
