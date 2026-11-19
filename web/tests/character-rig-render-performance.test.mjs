import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";

// Exercise the real hook's frame loop with a small browser/React harness.
// No ReactDOM, browser driver or external mocking dependency is required.
test("Live2D preview caches layout/rig parts and smooths raw input before translation", async () => {
  let setup;
  const hooks = {
    useRef: () => ({ current: null }),
    useEffect: callback => { setup = callback; },
    normalizeRig: value => ({ enabled: true, idle: false, headTilt: 100, eyeMove: 0,
      hairSway: 50, pivotX: 50, pivotY: 16, torsoPivotY: 40, lowerPivotY: 68,
      bodyMode: "follow", upperBody: 50, lowerBody: 25, torso: 50, leftArm: 30,
      rightArm: 30, leftHand: 25, rightHand: 25, leftLeg: 20, rightLeg: 20,
      partAssignments: {}, ...value }),
    rigTargets: (x, y) => ({ head: x * .1, eye: 0, shiftY: y * .01,
      eyeY: 0, front: 0, back: 0, torso: x * .03,
      leftArm: x * .03, rightArm: x * .03, leftHand: x * .03,
      rightHand: x * .03, lowerBody: x * .01, leftLeg: x * .01, rightLeg: x * .01 }),
  };
  const original = fs.readFileSync(new URL("../src/use-character-rig.ts", import.meta.url), "utf8");
  const raw = original.replace(/^import .*?;\r?\n/gm, "");
  const compiled = stripTypeScriptTypes(raw).replace(/\buseRef\s*\(/g, "globalThis.__rigHarness.useRef(")
    .replace(/\buseEffect\(/g, "globalThis.__rigHarness.useEffect(")
    .replace(/\bnormalizeRig\(/g, "globalThis.__rigHarness.normalizeRig(")
    .replace(/\brigTargets\(/g, "globalThis.__rigHarness.rigTargets(");
  const saved = Object.fromEntries(["__rigHarness", "document", "ResizeObserver", "requestAnimationFrame", "cancelAnimationFrame"]
    .map(key => [key, Object.getOwnPropertyDescriptor(globalThis, key)]));
  try {
    globalThis.__rigHarness = hooks;
    const frames = new Map(); let nextFrame = 1, measureCount = 0, observer;
    globalThis.requestAnimationFrame = callback => { const id = nextFrame++; frames.set(id, callback); return id; };
    globalThis.cancelAnimationFrame = id => frames.delete(id);
    globalThis.ResizeObserver = class {
      constructor(callback) { this.callback = callback; observer = this; }
      observe() { this.active = true; }
      disconnect() { this.active = false; }
    };
    globalThis.document = { hidden: false, addEventListener() {}, removeEventListener() {} };
    const style = () => ({ transform: "", transformOrigin: "", removeProperty(key) {
      if (key === "transform") this.transform = "";
      if (key === "transform-origin") this.transformOrigin = "";
    } });
    const head = { dataset: { rigPart: "head" }, style: style() };
    const torso = { dataset: { rigPart: "torso" }, style: style() };
    const background = { dataset: { rigPart: "fixed" }, style: style() };
    const plane = {
      getBoundingClientRect() { measureCount++; return { width: 1000, height: 1600 }; },
      querySelectorAll() { return [background, head, torso]; },
    };
    const { useCharacterRig } = await import(`data:text/javascript,${encodeURIComponent(compiled)}`);
    const input = { current: { x: 0, y: 0 } };
    const state = useCharacterRig(true, { enabled: true }, "example", input);
    state.stage.current = {};
    state.layers.current = plane;
    const stop = setup();
    const tick = timestamp => {
      const [id, callback] = frames.entries().next().value;
      frames.delete(id); callback(timestamp);
    };
    tick(1000);
    input.current.x = 1;
    tick(1016);
    const early = Number(head.style.transform.match(/translate\(([-.\d]+)px/)?.[1]);
    for (let i = 2; i < 60; i++) tick(1000 + i * 16);
    const settled = Number(head.style.transform.match(/translate\(([-.\d]+)px/)?.[1]);
    assert(early > 0 && early < settled, `translation should approach the input smoothly: ${early} -> ${settled}`);
    assert.equal(measureCount, 1, "layout should not be measured on every frame");
    assert.equal(background.style.transform, "", "background must remain fixed");
    assert.equal(torso.style.transformOrigin, "50% 40%");
    observer.callback();
    assert.equal(measureCount, 2, "a resize must update the cached bounds");
    stop();
    assert.equal(observer.active, false, "clean up the resize observer");
    assert.equal(head.style.transform, "", "remove motion on unmount");
  } finally {
    for (const [key, descriptor] of Object.entries(saved)) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor);
      else delete globalThis[key];
    }
  }
});
