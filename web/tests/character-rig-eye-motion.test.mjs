import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";

// Load actual defaults, target math, and the React frame hook. Only DOM/RAF are simulated.
test("preview eye gaze stays independent of the head and honors face/body switches", async () => {
  const model = fs.readFileSync(new URL("../src/model.ts", import.meta.url), "utf8");
  const defaults = model.match(/export const defaultHeadRig: HeadRig = (\{[\s\S]*?\n\});/)[1];
  const rigSource = fs.readFileSync(new URL("../src/character-rig.ts", import.meta.url), "utf8")
    .replace(/^import .*?;\r?\n/gm, `const defaultHeadRig = ${defaults};\n`);
  const rig = await import(`data:text/javascript,${encodeURIComponent(stripTypeScriptTypes(rigSource))}`);
  const hookSource = fs.readFileSync(new URL("../src/use-character-rig.ts", import.meta.url), "utf8")
    .replace(/^import .*?;\r?\n/gm, "");
  const hookJS = stripTypeScriptTypes(hookSource)
    .replace(/\b(useRef|useEffect|normalizeRig|rigTargets)\s*\(/g, "globalThis.__eyeHarness.$1(");
  const keys = ["__eyeHarness", "document", "ResizeObserver", "requestAnimationFrame", "cancelAnimationFrame"];
  const saved = Object.fromEntries(keys.map(k => [k, Object.getOwnPropertyDescriptor(globalThis, k)]));
  try {
    const { useCharacterRig } = await import(`data:text/javascript,${encodeURIComponent(hookJS)}`);
    function pose(overrides = {}) {
      let setup, id = 0;
      const frames = new Map();
      globalThis.__eyeHarness = { ...rig, useRef: () => ({ current: null }), useEffect: cb => { setup = cb; } };
      globalThis.document = { hidden: false, addEventListener() {}, removeEventListener() {} };
      globalThis.ResizeObserver = class { observe() {} disconnect() {} };
      globalThis.requestAnimationFrame = cb => { frames.set(++id, cb); return id; };
      globalThis.cancelAnimationFrame = key => frames.delete(key);
      const parts = ["head", "eye", "torso", "fixed"].map(part => ({ dataset: { rigPart: part }, style: {
        transform: "", transformOrigin: "", removeProperty(key) { if (key === "transform") this.transform = ""; },
      } }));
      const input = { current: { x: 1, y: 1 } };
      const controller = useCharacterRig(true, { ...rig.normalizeRig(), enabled: true, idle: false, ...overrides }, "eye", input);
      controller.stage.current = {};
      controller.layers.current = { getBoundingClientRect: () => ({ width: 1000, height: 2000 }), querySelectorAll: () => parts };
      const stop = setup();
      for (let i = 0; i < 300; i++) {
        const [key, cb] = frames.entries().next().value; frames.delete(key); cb(1000 + i * 16);
      }
      const result = Object.fromEntries(parts.map(p => [p.dataset.rigPart,
        p.style.transform ? p.style.transform.match(/translate\(([-.\d]+)px, ([-.\d]+)px\) rotate\(([-.\d]+)rad\)/).slice(1).map(Number) : [0, 0, 0]]));
      stop(); return result;
    }
    const normal = pose();
    assert(Math.abs(normal.eye[0] - normal.head[0] - 6.3) < .0001);
    assert(Math.abs(normal.eye[1] - normal.head[1] - 9.8) < .0001);
    assert(Math.abs(normal.eye[2] - normal.head[2]) < .0001);
    const zero = pose({ eyeMove: 0 });
    assert.deepEqual(zero.eye, zero.head);
    const off = pose({ faceEnabled: false });
    assert.deepEqual(off.eye, [0, 0, 0]); assert.deepEqual(off.head, [0, 0, 0]);
    assert(off.torso[0] > 0 && off.torso[2] > 0);
    assert.deepEqual(pose({ bodyMode: "fixed" }).torso, [0, 0, 0]);
    assert.deepEqual(normal.fixed, [0, 0, 0]);
  } finally {
    for (const [key, descriptor] of Object.entries(saved)) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor); else delete globalThis[key];
    }
  }
});
