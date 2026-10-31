/** Coalesce rapid edits per image and serialize writes so old requests never finish last. */
export class LatestSaveQueue<T> {
  private pending = new Map<string, T>();
  private running: Promise<void> | null = null;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private write: (value: T) => Promise<void>;
  private changed: (pending: boolean) => void;
  private failed: (error: Error, value: T) => void;
  private delay: number;
  constructor(write: (value: T) => Promise<void>, changed: (pending: boolean) => void,
    failed: (error: Error, value: T) => void, delay = 200) {
    this.write = write; this.changed = changed; this.failed = failed; this.delay = delay;
  }
  enqueue(key: string, value: T) {
    this.pending.set(key, value); this.changed(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => { void this.flush(); }, this.delay);
  }
  flush(): Promise<void> {
    clearTimeout(this.timer); this.timer = undefined;
    if (this.running) return this.running;
    if (!this.pending.size) return Promise.resolve();
    this.running = this.drain().finally(() => {
      this.running = null;
      if (this.pending.size) return this.flush();
      clearTimeout(this.timer); this.timer = undefined;
      this.changed(false);
    });
    return this.running;
  }
  private async drain() {
    while (this.pending.size) {
      const [key, value] = this.pending.entries().next().value!;
      this.pending.delete(key);
      try { await this.write(value); }
      catch (error) { this.failed(error instanceof Error ? error : new Error(String(error)), value); }
    }
  }
}
