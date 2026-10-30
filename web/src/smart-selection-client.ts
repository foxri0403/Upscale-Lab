import type { SmartSelectionInput, SmartSelectionResult } from "./smart-selection";

export class SmartSelectionClient {
  private worker = new Worker(new URL("./smart-selection.worker.ts", import.meta.url), { type: "module" });
  private id = 0;
  private pending = new Map<number, { resolve: (result: SmartSelectionResult) => void; reject: (error: Error) => void; progress: (message: string) => void; timer: ReturnType<typeof setTimeout> }>();
  constructor(private status: (message: string) => void) {
    this.worker.onmessage = event => {
      const { id, type, message, alpha, score } = event.data;
      const pending = this.pending.get(id);
      if (type === "progress") { (pending?.progress || this.status)(message); return; }
      if (!pending) { if (type === "error") this.status(message); return; }
      clearTimeout(pending.timer); this.pending.delete(id);
      if (type === "error") pending.reject(new Error(message)); else pending.resolve({ alpha, score });
    };
    this.worker.onerror = () => {
      this.status("선택 AI 연결을 확인해 주세요.");
      for (const pending of this.pending.values()) { clearTimeout(pending.timer); pending.reject(new Error("선택 AI를 시작하지 못했습니다. 페이지를 새로고침해 주세요.")); }
      this.pending.clear();
    };
    this.worker.postMessage({ id: 0 });
  }
  refine(input: SmartSelectionInput, progress: (message: string) => void) {
    const id = ++this.id;
    return new Promise<SmartSelectionResult>((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error("선택 AI 응답이 지연되고 있습니다. 잠시 후 다시 선택해 주세요.")); }, 180000);
      this.pending.set(id, { resolve, reject, progress, timer });
      this.worker.postMessage({ id, input }, [input.pixels.buffer, input.guide.buffer, input.prior.buffer, input.reference.buffer]);
    });
  }
  dispose() {
    this.worker.terminate();
    for (const pending of this.pending.values()) { clearTimeout(pending.timer); pending.reject(new Error("선택 작업이 종료되었습니다.")); }
    this.pending.clear();
  }
}
