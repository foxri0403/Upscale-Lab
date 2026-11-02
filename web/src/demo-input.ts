export type DemoInput = "mouse" | "gyro";

export function defaultDemoInput(coarsePointer: boolean, finePointer: boolean, touchPoints: number): DemoInput {
  return coarsePointer || (touchPoints > 0 && !finePointer) ? "gyro" : "mouse";
}

/** Both showcase previews share one native permission prompt from the same gesture. */
export function createOrientationPermissionRequest() {
  let pending: Promise<void> | undefined;
  let granted = false;
  return (request?: () => Promise<string>): Promise<void> => {
    if (!request || granted) return Promise.resolve();
    if (!pending) {
      try {
        // Call synchronously while transient user activation is still present.
        pending = request().then(state => {
          if (state !== "granted") throw new Error("기울기 센서 권한을 허용해 주세요.");
          granted = true;
        });
      } catch (error) { pending = Promise.reject(error); }
      const current = pending;
      // A browser may run microtasks between listeners for one native gesture.
      // Keep settled requests through that gesture so the second preview shares it.
      const clear = () => { setTimeout(() => { if (pending === current) pending = undefined; }, 0); };
      void current.then(clear, clear);
    }
    return pending;
  };
}

export const requestOrientationPermission = createOrientationPermissionRequest();
