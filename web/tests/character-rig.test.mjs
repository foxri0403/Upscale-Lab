import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { stripTypeScriptTypes } from "node:module";

// Load the real implementation without a bundler; model import is replaced by
// the constant defaults used by the actual editor.
const raw = fs.readFileSync(new URL("../src/character-rig.ts", import.meta.url), "utf8")
  .replace(/^import \{ defaultHeadRig, type HeadRig \} from "\.\/model";/m, `const defaultHeadRig = {
    enabled: false, headTilt: 60, eyeMove: 35, hairSway: 55, pivotX: 50, pivotY: 16, idle: true, bodyMode: "fixed", upperBody: 40, lowerBody: 20, torso: 50, leftArm: 35, rightArm: 35, leftHand: 25, rightHand: 25, leftLeg: 20, rightLeg: 20, torsoPivotY: 40, lowerPivotY: 68, partAssignments: {},
  };`);
const js = stripTypeScriptTypes(raw, { mode: "strip" });
const { rigPart, rigMovementGroup, rigTargets, normalizeRig, toggleRigMovementGroup } = await import(`data:text/javascript,${encodeURIComponent(js)}`);

test("movement groups follow the effective roles while preserving head and body subroles", () => {
  const groupOf = (layerType, assignments = {}) => rigMovementGroup(rigPart(layerType, "part", assignments));
  for (const part of ["Face", "Eye", "Nose", "Mouth", "Eyebrow"])
    assert.equal(groupOf(part), "face");
  for (const part of ["FrontHair", "BackHair"]) assert.equal(groupOf(part), "hair");
  assert.equal(groupOf("Clothes"), "upperBody");
  assert.equal(groupOf("Face", { part: "leftArm" }), "upperBody");
  assert.equal(groupOf("Body", { part: "rightLeg" }), "lowerBody");
  assert.equal(groupOf("Accessory"), "fixed");
});

test("group toggling keeps matching roles and restores a moving role when leaving fixed", () => {
  assert.equal(toggleRigMovementGroup("face", "head", "head", "eye"), "fixed");
  assert.equal(toggleRigMovementGroup("face", "fixed", "head", "eye"), "head");
  assert.equal(toggleRigMovementGroup("fixed", "fixed", "backHair", "frontHair"), "backHair");
  assert.equal(toggleRigMovementGroup("upperBody", "frontHair", "frontHair", "frontHair"), "torso");
});

test("semantic rig roles keep unmatched parts fixed and allow body assignments", () => {
  assert.equal(rigPart("Face"), "head");
  assert.equal(rigPart("Eye"), "eye");
  assert.equal(rigPart("Eyebrow"), "head");
  assert.equal(rigPart("FrontHair"), "frontHair");
  assert.equal(rigPart("BackHair"), "backHair");
  assert.equal(rigPart("Body"), "torso");
  assert.equal(rigPart("Clothes"), "torso");
  assert.equal(rigPart("Other", "layer-1", { "layer-1": "leftHand" }), "leftHand");
  for (const fixed of ["Accessory", "FrontPart", "BackPart", "Background"])
    assert.equal(rigPart(fixed), "fixed", `${fixed} must not move`);
});

test("See-through demo: irides move alone, eyelash and eyewhite follow the head", () => {
  assert.equal(rigPart("Eye", "irides"), "eye");
  assert.equal(rigPart("Eye", "eyewhite"), "head");
  assert.equal(rigPart("Eye", "eyelash"), "head");
  assert.equal(rigPart("Eye"), "eye");
  assert.equal(rigPart("Eye", "eye-layer", { "eye-layer": "leftHand" }, "eyewhite"), "leftHand", "explicit assignments take priority");
});

test("no head turn, eyes or hair when intensity is zero", () => {
  const s = normalizeRig({ enabled: true, headTilt: 0, eyeMove: 0, hairSway: 0, idle: false });
  const target = rigTargets(1, 1, 1, 1, 0, s);
  assert(Object.values(target).every(value => value === 0));
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
  assert.match(source, /tool === "layers" && !separationConflict && <>/);
  assert.doesNotMatch(source, /setSeparationMode/);
});

test("body fixed vs follow and independent upper/lower strength", () => {
  const locked = rigTargets(1, 0, 0, 0, 0, normalizeRig({ enabled: true, idle: false, bodyMode: "fixed", headTilt: 100 }));
  assert(locked.head > 0); assert.equal(locked.torso, 0); assert.equal(locked.leftLeg, 0);
  const following = rigTargets(1, 0, 0, 0, 0, normalizeRig({ enabled: true, idle: false, bodyMode: "follow", headTilt: 100 }));
  assert(following.torso > 0); assert(following.leftHand > following.torso); assert(following.leftLeg > 0);
  const upperOnly = rigTargets(1, 0, 0, 0, 0, normalizeRig({ enabled: true, idle: false, bodyMode: "follow", upperBody: 0 }));
  assert.equal(upperOnly.torso, 0); assert(upperOnly.leftLeg > 0);
});

test("legacy rig saved without body properties defaults to fixed and sanitized assignments", () => {
  const old = normalizeRig({ enabled: true, headTilt: 60, partAssignments: { "id": "leftArm", "other": "notValid" } });
  assert.equal(old.bodyMode, "fixed");
  assert.equal(old.partAssignments.id, "leftArm");
  assert.equal(old.partAssignments.other, undefined);
  assert.equal(rigPart("Background", "bg", old.partAssignments), "fixed");
});
