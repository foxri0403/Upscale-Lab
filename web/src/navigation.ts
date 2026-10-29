type Guard = (destination: string) => boolean;
let guard: Guard | null = null;
const indexKey = "upscaleNavigationIndex";
let index: number = window.history.state?.[indexKey] ?? 0;
let restoring = false;
window.history.replaceState({ ...window.history.state, [indexKey]: index }, "", location.href);

export function setNavigationGuard(next: Guard) {
  guard = next;
  return () => { if (guard === next) guard = null; };
}
export function canNavigate(destination: string) { return guard?.(destination) ?? true; }
export function navigate(path: string, replace = false) {
  if (!canNavigate(path)) return;
  if (!replace) index++;
  window.history[replace ? "replaceState" : "pushState"]({ [indexKey]: index }, "", path);
  window.dispatchEvent(new Event("upscale:route"));
  window.scrollTo(0, 0);
}
window.addEventListener("popstate", event => {
  if (restoring) { restoring = false; event.stopImmediatePropagation(); return; }
  const next = event.state?.[indexKey] ?? index - 1;
  if (!canNavigate(location.pathname + location.search)) {
    event.stopImmediatePropagation(); restoring = true;
    window.history.go(index - next);
    return;
  }
  index = next;
}, true);
