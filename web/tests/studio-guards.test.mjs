import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";

// Run the actual hook event handlers with a small hook lifecycle and DOM event target.
// This verifies navigation/unload and pointer behavior without a browser or GPU jobs.
function harness() {
  const slots = [], cleanups = []; let cursor = 0;
  return {
    useRef(value) { const i = cursor++; return slots[i] ??= { current: value }; },
    useState(value) { const i = cursor++; if (!(i in slots)) slots[i] = value; return [slots[i], next => { slots[i] = typeof next === "function" ? next(slots[i]) : next; }]; },
    useEffect(effect, deps) { const i = cursor++, previous = slots[i]; if (!previous || deps.some((d, j) => d !== previous[j])) { cleanups[i]?.(); slots[i] = deps; cleanups[i] = effect(); } },
    render(callback) { cursor = 0; return callback(); },
    dispose() { for (const cleanup of cleanups) cleanup?.(); },
  };
}
async function loadHook(file, runtime, extra = "") {
  globalThis.__studioHooks = runtime;
  let code = fs.readFileSync(new URL(`../src/${file}.ts`, import.meta.url), "utf8")
    .replace(/^import .* from "react";$/m, "const { useEffect, useRef, useState } = globalThis.__studioHooks;")
    .replace(/^import .* from "\.\/navigation";$/m, "const setNavigationGuard = globalThis.__setStudioGuard;");
  code = stripTypeScriptTypes(code, { mode: "strip" });
  return import(`data:text/javascript,${encodeURIComponent(code + extra)}`);
}

test("processing blocks navigation and unload without discarding; unlocking restores unsaved confirmation", async () => {
  globalThis.window = new EventTarget(); globalThis.document = new EventTarget();
  let guard, blocked = 0, discarded = 0, confirmations = 0;
  window.confirm = () => { confirmations++; return true; };
  globalThis.__setStudioGuard = next => { guard = next; return () => { guard = null; }; };
  const hooks = harness(); const { useUnsavedWarning } = await loadHook("use-unsaved-warning", hooks);
  try {
    hooks.render(() => useUnsavedWarning(true, () => discarded++, { active: true, blocked: () => blocked++ }));
    assert.equal(guard("/app/billing"), false); assert.equal(blocked, 1); assert.equal(confirmations, 0);
    const unload = new Event("beforeunload", { cancelable: true });
    // Node Event does not provide a writable returnValue like browser BeforeUnloadEvent.
    Object.defineProperty(unload, "returnValue", { value: undefined, writable: true, configurable: true });
    window.dispatchEvent(unload);
    assert.equal(unload.defaultPrevented, true);
    window.dispatchEvent(new Event("pagehide")); assert.equal(discarded, 0);
    hooks.render(() => useUnsavedWarning(true, () => discarded++, { active: false, blocked: () => blocked++ }));
    assert.equal(guard("/app/images"), true); assert.equal(confirmations, 1); assert.equal(discarded, 1);
  } finally { hooks.dispose(); }
});

test("preview pan supports the hand, space and middle button while preserving editing when off", async () => {
  globalThis.window = new EventTarget(); const hooks = harness();
  const { usePreviewPan } = await loadHook("use-preview-pan", hooks);
  const target = { scrollLeft: 120, scrollTop: 90, setPointerCapture() {}, hasPointerCapture: () => true, releasePointerCapture() {} };
  const event = (button = 0, x = 100, y = 100) => ({ isPrimary: true, pointerId: 1, button, clientX: x, clientY: y,
    target: { closest: () => null }, currentTarget: target, preventDefault() {}, stopPropagation() {} });
  try {
    let pan = hooks.render(() => usePreviewPan(true, false));
    pan.events.onPointerDownCapture(event()); pan.events.onPointerMoveCapture(event(0, 75, 60));
    assert.equal(target.scrollLeft, 120); // Editing has priority until hand/space/middle is chosen.
    pan = hooks.render(() => usePreviewPan(true, true));
    pan.events.onPointerDownCapture(event()); pan.events.onPointerMoveCapture(event(0, 75, 60));
    assert.equal(target.scrollLeft, 145); assert.equal(target.scrollTop, 130);
    pan.events.onPointerUpCapture(event());
    pan = hooks.render(() => usePreviewPan(true, false));
    pan.events.onPointerDownCapture(event(1)); pan.events.onPointerMoveCapture(event(1, 90, 80));
    assert.equal(target.scrollLeft, 155); assert.equal(target.scrollTop, 150); pan.events.onPointerUpCapture(event(1));
    const space = new Event("keydown", { cancelable: true }); Object.defineProperty(space, "code", { value: "Space" }); window.dispatchEvent(space);
    pan = hooks.render(() => usePreviewPan(true, false)); assert.equal(pan.canPan, true);
    pan.events.onPointerDownCapture(event()); pan.events.onPointerMoveCapture(event(0, 90, 90));
    assert.equal(target.scrollLeft, 165); pan.events.onPointerUpCapture(event());
    hooks.render(() => usePreviewPan(false, false)); pan = hooks.render(() => usePreviewPan(false, false));
    assert.equal(pan.canPan, false);
    pan.events.onPointerDownCapture(event(1)); pan.events.onPointerMoveCapture(event(1, 0, 0)); assert.equal(target.scrollLeft, 165);
  } finally { hooks.dispose(); }
});
