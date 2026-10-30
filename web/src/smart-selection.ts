export type SmartSelectionInput = {
  imageKey: string; width: number; height: number; pixels: Uint8ClampedArray;
  guide: Uint8Array; prior: Uint8Array; reference: Uint8Array;
  mode: "restore" | "erase"; tool: "brush" | "lasso";
};
export type SmartSelectionResult = { alpha: Uint8Array; score: number };
export type SmartSelection = {
  refine: (input: SmartSelectionInput, progress: (message: string) => void) => Promise<SmartSelectionResult>;
  onRefined?: () => void;
};

/** Find an interior point, rather than treating every roughly painted pixel as foreground. */
export function interiorPoint(mask: Uint8Array, width: number, height: number): [number, number] | undefined {
  if (width * height !== mask.length || !width || !height) throw new Error("Invalid selection dimensions");
  const distances = new Uint16Array(mask.length);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const i = y * width + x;
    if (mask[i]) distances[i] = Math.min(x && y ? 32767 : 1, x ? distances[i - 1] + 1 : 1, y ? distances[i - width] + 1 : 1);
  }
  let best = -1, distance = 0;
  for (let y = height - 1; y >= 0; y--) for (let x = width - 1; x >= 0; x--) {
    const i = y * width + x;
    if (!mask[i]) continue;
    distances[i] = Math.min(distances[i], x + 1 < width ? distances[i + 1] + 1 : 1, y + 1 < height ? distances[i + width] + 1 : 1);
    if (distances[i] > distance) { best = i; distance = distances[i]; }
  }
  return best < 0 ? undefined : [best % width, Math.floor(best / width)];
}

export function selectionPrompts(input: Pick<SmartSelectionInput, "guide" | "prior" | "reference" | "width" | "height" | "mode" | "tool">) {
  const { guide, prior, reference, width, height, mode } = input;
  if ([guide, prior, reference].some(x => x.length !== width * height)) throw new Error("Invalid selection canvas");
  const positive = new Uint8Array(guide.length), negative = new Uint8Array(guide.length);
  let referenceHits = 0;
  for (let i = 0; i < guide.length; i++) {
    if (mode === "erase") { positive[i] = prior[i] > 200 && guide[i] < 64 ? 1 : 0; negative[i] = prior[i] > 127 && guide[i] > 127 ? 1 : 0; }
    else { positive[i] = guide[i] > 127 && reference[i] > 200 ? 1 : 0; referenceHits += positive[i]; }
  }
  const point = interiorPoint(positive, width, height) || (mode === "restore" ? interiorPoint(guide, width, height) : undefined);
  if (!point) {
    if (mode === "erase") return { points: [] as number[][], labels: [] as number[], referenceHits, empty: true };
    throw new Error("피사체를 중심으로 영역을 그려 주세요.");
  }
  const points: number[][] = [point], labels = [1];
  if (mode === "erase") {
    const excluded = interiorPoint(negative, width, height);
    if (!excluded) return { points, labels, referenceHits, unchanged: true };
    points.push(excluded); labels.push(0);
  }
  // A rough lasso supplies background hints outside its bounds. Its interior is not a solid cutout.
  if (input.tool === "lasso" && mode === "restore") {
    let left = width, right = 0, top = height, bottom = 0;
    for (let i = 0; i < guide.length; i++) if (guide[i] > 127) {
      const x = i % width, y = Math.floor(i / width); left = Math.min(left, x); right = Math.max(right, x); top = Math.min(top, y); bottom = Math.max(bottom, y);
    }
    const pad = Math.max(4, Math.round(Math.max(width, height) * .015));
    for (const [x, y] of [[left - pad, top - pad], [right + pad, top - pad], [left - pad, bottom + pad], [right + pad, bottom + pad]]) {
      const px = Math.max(0, Math.min(width - 1, x)), py = Math.max(0, Math.min(height - 1, y));
      if (reference[py * width + px] < 32) { points.push([px, py]); labels.push(0); }
    }
  }
  return { points, labels, referenceHits };
}

export function chooseSelection(logits: Float32Array, scores: Float32Array, input: Pick<SmartSelectionInput, "guide" | "prior" | "reference" | "width" | "height" | "mode" | "tool">, points: number[][], labels: number[], referenceHits: number): SmartSelectionResult {
  const count = input.width * input.height;
  if (logits.length !== count * scores.length || !scores.length) throw new Error("AI 선택 결과의 크기가 올바르지 않습니다.");
  let chosen = -1, best = -Infinity;
  for (let c = 0; c < scores.length; c++) {
    const offset = c * count;
    if (points.some(([x, y], i) => (logits[offset + y * input.width + x] > 0) !== !!labels[i])) continue;
    let area = 0, intersection = 0, union = 0;
    for (let i = 0; i < count; i++) {
      const selected = logits[offset + i] > 0, reference = input.reference[i] > 127;
      area += selected ? 1 : 0; intersection += selected && reference ? 1 : 0; union += selected || reference ? 1 : 0;
    }
    if (area < count * .001 || area > count * .97 || !Number.isFinite(scores[c])) continue;
    const value = scores[c] + (referenceHits > 16 ? .65 * intersection / Math.max(1, union) : 0);
    if (value > best) { best = value; chosen = c; }
  }
  if (chosen < 0) throw new Error("피사체 경계를 찾지 못했습니다. 피사체 안쪽을 중심으로 다시 그려 주세요.");
  const alpha = new Uint8Array(count);
  for (let i = 0; i < count; i++) {
    const value = logits[chosen * count + i];
    const selected = value <= -2 ? 0 : value >= 2 ? 255 : Math.round(255 / (1 + Math.exp(-value * 2)));
    alpha[i] = input.mode === "erase" ? Math.min(input.prior[i], selected) : selected;
  }
  // SAM can predict unrelated islands in textured scenery. Keep the component containing
  // the positive object prompt, with a one-pixel soft rim, rather than adding those islands.
  const seed = points.find((_, index) => labels[index] === 1)!;
  const start = seed[1] * input.width + seed[0], kept = new Uint8Array(count), queue = new Uint32Array(count);
  let read = 0, write = 0; kept[start] = 1; queue[write++] = start;
  while (read < write) {
    const i = queue[read++], x = i % input.width, y = Math.floor(i / input.width);
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const px = x + dx, py = y + dy, next = py * input.width + px;
      if (px >= 0 && px < input.width && py >= 0 && py < input.height && !kept[next] && alpha[next] > 127) {
        kept[next] = 1; queue[write++] = next;
      }
    }
  }
  for (let i = 0; i < count; i++) if (alpha[i] && !kept[i]) {
    const x = i % input.width, y = Math.floor(i / input.width);
    let rim = false;
    if (alpha[i] < 128) for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const px = x + dx, py = y + dy;
      if (px >= 0 && px < input.width && py >= 0 && py < input.height && kept[py * input.width + px]) rim = true;
    }
    if (!rim) alpha[i] = 0;
  }
  return { alpha, score: scores[chosen] };
}
