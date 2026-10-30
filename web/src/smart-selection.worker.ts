import { AutoProcessor, RawImage, SamModel, SamProcessor, Tensor, env } from "@huggingface/transformers";
import { chooseSelection, selectionPrompts, type SmartSelectionInput } from "./smart-selection";

// This is the primary interactive selector, on WebAssembly CPU; never a local GPU runner.
env.allowLocalModels = false;
env.backends.onnx.wasm!.numThreads = 1;
env.backends.onnx.wasm!.proxy = false;
const modelId = "Xenova/slimsam-77-uniform", revision = "5850ab45f587c112167512ffef949107115e26a0";
const scope = self as unknown as { onmessage: (event: MessageEvent) => void; postMessage: (value: unknown, transfer?: Transferable[]) => void };
let loading: Promise<{ model: SamModel; processor: SamProcessor }> | undefined;
let frame: { key: string; width: number; height: number; sizes: [number, number][]; reshaped: [number, number][]; embeddings: { image_embeddings: Tensor; image_positional_embeddings: Tensor } } | undefined;
function progress(id: number, message: string) { scope.postMessage({ id, type: "progress", message }); }
function load(id: number) {
  return loading ||= Promise.all([
    SamModel.from_pretrained(modelId, { revision, device: "wasm", dtype: "fp32", progress_callback: (event: { status: string; progress?: number }) => {
      if (event.status === "progress") progress(id, `선택 AI 준비 중 · ${Math.round(event.progress || 0)}%`);
    } }),
    AutoProcessor.from_pretrained(modelId, { revision }),
  ]).then(([model, processor]) => ({ model: model as SamModel, processor: processor as SamProcessor })).catch(error => { loading = undefined; throw error; });
}
async function run(message: { id: number; input?: SmartSelectionInput }) {
  const { id, input } = message;
  const { model, processor } = await load(id);
  if (!input) { progress(id, "선택 AI 준비 완료"); scope.postMessage({ id, type: "ready" }); return; }
  const prompts = selectionPrompts(input);
  if (prompts.empty || prompts.unchanged) {
    const alpha = prompts.empty ? new Uint8Array(input.prior.length) : input.prior;
    scope.postMessage({ id, type: "result", alpha, score: 1 }, [alpha.buffer]); return;
  }
  if (frame?.key !== input.imageKey || frame.width !== input.width || frame.height !== input.height) {
    progress(id, "이미지 특징을 분석하는 중…");
    frame?.embeddings.image_embeddings.dispose(); frame?.embeddings.image_positional_embeddings.dispose(); frame = undefined;
    const image = new RawImage(input.pixels, input.width, input.height, 4).rgb();
    const prepared = await processor(image);
    try {
      frame = { key: input.imageKey, width: input.width, height: input.height, sizes: prepared.original_sizes,
        reshaped: prepared.reshaped_input_sizes, embeddings: await model.get_image_embeddings(prepared) };
    } finally { prepared.pixel_values.dispose(); }
  }
  progress(id, "대략 선택한 범위에서 피사체 경계를 찾는 중…");
  const [height, width] = frame.reshaped[0];
  const coordinates = prompts.points.flatMap(([x, y]) => [x / input.width * width, y / input.height * height]);
  const inputPoints = new Tensor("float32", new Float32Array(coordinates), [1, 1, prompts.points.length, 2]);
  const inputLabels = new Tensor("int64", BigInt64Array.from(prompts.labels.map(BigInt)), [1, 1, prompts.labels.length]);
  let output: Awaited<ReturnType<SamModel["forward"]>> | undefined;
  let masks: Tensor[] | undefined;
  try {
    // The SDK's declaration requires pixel_values, but its decoder explicitly supports cached embeddings without them.
    output = await model.forward({ ...frame.embeddings, input_points: inputPoints, input_labels: inputLabels } as Parameters<SamModel["forward"]>[0]);
    const processed = await processor.post_process_masks(output.pred_masks, frame.sizes, frame.reshaped, { binarize: false }); masks = processed;
    const result = chooseSelection(processed[0].data as Float32Array, output.iou_scores.data as Float32Array, input, prompts.points, prompts.labels, prompts.referenceHits);
    scope.postMessage({ id, type: "result", ...result }, [result.alpha.buffer]);
  } finally {
    inputPoints.dispose(); inputLabels.dispose(); masks?.forEach(x => x.dispose());
    output?.pred_masks.dispose(); output?.iou_scores.dispose();
  }
}
let queue = Promise.resolve();
scope.onmessage = event => {
  const message = event.data as { id: number; input?: SmartSelectionInput };
  queue = queue.then(() => run(message)).catch((error: Error) => {
    scope.postMessage({ id: message.id, type: "error", message: /^[가-힣]/.test(error.message) ? error.message : "선택 AI를 실행하지 못했습니다. 모델 다운로드 연결을 확인한 뒤 다시 시도해 주세요." });
  });
};
