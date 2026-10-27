// Authenticated long polling: the server completes the request as soon as a device changes.
export function watchDeviceUpdates(
  read: (after: string | undefined, signal: AbortSignal) => Promise<{ cursor: string }>,
  refresh: () => Promise<void>,
) {
  const controller = new AbortController();
  const pause = (ms: number) => new Promise<void>(resolve => {
    const done = () => { clearTimeout(timer); controller.signal.removeEventListener("abort", done); resolve(); };
    const timer = setTimeout(done, ms); controller.signal.addEventListener("abort", done, { once: true });
  });
  void (async () => {
    let cursor: string | undefined;
    while (!controller.signal.aborted) {
      try {
        const next = await read(cursor, controller.signal);
        if (controller.signal.aborted) break;
        if (next.cursor !== cursor) { cursor = next.cursor; await refresh(); }
      } catch {
        if (controller.signal.aborted) break;
        try { await refresh(); } catch { /* Retry after the network returns. */ }
        await pause(3000);
      }
    }
  })();
  return () => controller.abort();
}
