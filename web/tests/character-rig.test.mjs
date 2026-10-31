import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";

// Load the real implementation without a bundler; model import is replaced by
// the constant defaults used by the actual editor.
const raw = fs.readFileSync(new URL("../src/character-rig.ts", import.meta.url), "utf8")
  .replace(/^import \{ defaultHeadRig, type HeadRig \} from "\.\/model";/m, `const defaultHeadRig = {
    enabled: false, headTilt: 60, eyeMove: 35, hairSway: 55, pivotX: 50, pivotY: 16, idle: true,
  };`);
const js = stripTypeScriptTypes(raw, { mode: "transform" });
const { rigPart, rigTargets, normalizeRig } = await import(`data:text/javascript,${encodeURIComponent(js)}`);

test("character rig moves only semantic face, eyes and hair parts", () => {
  assert.equal(rigPart("Face"), "head");
  assert.equal(rigPart("Eye"), "eye");
  assert.equal(rigPart("Eyebrow"), "head");
  assert.equal(rigPart("FrontHair"), "frontHair");
  assert.equal(rigPart("BackHair"), "backHair");
  for (const fixed of ["Body", "Clothes", "Accessory", "FrontPart", "BackPart", "Background"])
    assert.equal(rigPart(fixed), "fixed", `${fixed} must not move`);
});

test("no head turn, eyes or hair when intensity is zero", () => {
  const s = normalizeRig({ enabled: true, headTilt: 0, eyeMove: 0, hairSway: 0, idle: false });
  assert.deepEqual(rigTargets(1, 1, 1, 1, 0, s), { head: 0, eye: 0, shiftY: 0, eyeY: 0, front: 0, back: 0 });
});

test("head turns and eyes move only within the bounded inputs", () => {
  const s = normalizeRig({ enabled: true, idle: false, headTilt: 100, eyeMove: 100, hairSway: 100 });
  assert.deepEqual(rigTargets(5, 3, -4, -2, 0, s), rigTargets(1, 1, -1, -1, 0, s));
  const t = rigTargets(1, 1, 1, 1, 0, s);
  assert(t.head > 0 && t.eye > 0 && t.front < 0 && t.back < 0);
});

test("rig settings clamp invalid values and old records get defaults", () => {
  const s = normalizeRig({ headTilt: 200, eyeMove: -7, hairSway: NaN, pivotY: 101, enabled: true });
  assert.equal(s.headTilt, 100);
  assert.equal(s.eyeMove, 0);
  assert.equal(s.hairSway, 55);
  assert.equal(s.pivotY, 100);
  assert.equal(normalizeRig(undefined).enabled, false);
});

test("character processing and rig options are isolated to the Live tab", () => {
  const source = fs.readFileSync(new URL("../src/Studio.tsx", import.meta.url), "utf8");
  assert.match(source, /tool === "live" \? "CharacterLayers" : "Layers"/);
  assert.match(source, /tool === "live" && <div className="studio-live-controls">/);
  assert.match(source, /tool === "layers" && <>/);
  assert.doesNotMatch(source, /setSeparationMode/);
});
