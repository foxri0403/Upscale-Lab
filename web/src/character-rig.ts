import { defaultHeadRig, type HeadRig } from "./model";

export type RigPart = "fixed" | "head" | "frontHair" | "backHair" | "eye" |
  "torso" | "leftArm" | "rightArm" | "leftHand" | "rightHand" |
  "lowerBody" | "leftLeg" | "rightLeg";
export const rigMovementGroups = [
  { id: "head", label: "머리·얼굴", parts: [
    { value: "head", label: "머리·얼굴" }, { value: "eye", label: "눈" },
    { value: "frontHair", label: "앞머리" }, { value: "backHair", label: "뒷머리" },
  ] },
  { id: "upperBody", label: "상체", parts: [
    { value: "torso", label: "몸통·의상" },
    { value: "leftArm", label: "왼팔" }, { value: "rightArm", label: "오른팔" },
    { value: "leftHand", label: "왼손" }, { value: "rightHand", label: "오른손" },
  ] },
  { id: "lowerBody", label: "하체", parts: [
    { value: "lowerBody", label: "하체 전체" }, { value: "leftLeg", label: "왼다리" }, { value: "rightLeg", label: "오른다리" },
  ] },
  { id: "fixed", label: "고정", parts: [{ value: "fixed", label: "고정" }] },
] as const satisfies ReadonlyArray<{ id: string; label: string; parts: readonly { value: RigPart; label: string }[] }>;
export type RigMovementGroup = (typeof rigMovementGroups)[number]["id"];
const groupsByPart = new Map<RigPart, RigMovementGroup>(rigMovementGroups.flatMap(group =>
  group.parts.map(part => [part.value, group.id] as const)));

export function rigMovementGroup(part: RigPart): RigMovementGroup {
  return groupsByPart.get(part) ?? "fixed";
}
/** See-through currently has no guaranteed separate arm/hand/leg segmentation.
 * Never infer nonexistent independent PNG parts. Users can map actual layer IDs.
 */
export function rigPart(type: string, layerIdOrSourceName?: string, assignments?: Record<string, RigPart>, sourceName?: string): RigPart {
  // Studio uses the second argument as a layer ID for explicit overrides;
  // the showcase uses it as the original See-through source name.
  const manual = layerIdOrSourceName && assignments?.[layerIdOrSourceName];
  if (manual && groupsByPart.has(manual)) return manual;
  const semanticName = sourceName ?? (assignments ? "" : layerIdOrSourceName ?? "");
  // Eye whites and lashes follow the head, only irides get independent gaze.
  if (type === "Eye" && /eye[ _-]?(white|lash)|sclera/i.test(semanticName)) return "head";
  switch (type) {
    case "Face": case "Eyebrow": case "Mouth": case "Nose": return "head";
    case "Eye": return "eye";
    case "BackHair": return "backHair";
    case "FrontHair": case "Hair": return "frontHair";
    case "Body": case "Clothes": return "torso";
    default: return "fixed";
  }
}
const intensityFields = ["headTilt", "eyeMove", "hairSway", "pivotX", "pivotY", "upperBody",
  "lowerBody", "torso", "leftArm", "rightArm", "leftHand", "rightHand", "leftLeg", "rightLeg",
  "torsoPivotY", "lowerPivotY"] as const;
export function normalizeRig(value?: Partial<HeadRig> | null): HeadRig {
  const result: HeadRig = { ...defaultHeadRig, ...value };
  const safe = (x: number, fallback: number) => Number.isFinite(x) ? Math.min(100, Math.max(0, x)) : fallback;
  for (const field of intensityFields) result[field] = safe(result[field], defaultHeadRig[field]);
  result.enabled = result.enabled === true;
  result.idle = result.idle !== false;
  result.bodyMode = result.bodyMode === "follow" ? "follow" : "fixed";
  const provided = result.partAssignments && typeof result.partAssignments === "object" &&
    !Array.isArray(result.partAssignments) ? result.partAssignments : {};
  result.partAssignments = Object.fromEntries(Object.entries(provided)
    .filter(([id, part]) => id.length <= 100 && groupsByPart.has(part))) as Record<string, RigPart>;
  return result;
}
const clamp = (value: number) => Math.max(-1, Math.min(1, value));
export function rigTargets(tilt: number, tiltY: number, look: number, lookY: number, time: number, settings: HeadRig) {
  const head = settings.headTilt / 100 * (clamp(tilt) * .115 + (settings.idle ? Math.sin(time * 1.5) * .009 : 0));
  const eye = clamp(look) * settings.eyeMove / 100 * .018;
  const front = -head * .55 + (settings.idle ? Math.sin(time * 1.5) * .013 : 0);
  const back = -head * .28 + (settings.idle ? Math.sin(time * 1.5) * .009 : 0);
  const shiftY = clamp(tiltY) * settings.headTilt / 100 * .007 + (settings.idle ? Math.sin(time * 1.5) * .0015 : 0);
  const eyeY = clamp(lookY) * settings.eyeMove / 100 * .014;
  const active = settings.bodyMode === "follow";
  const upper = active ? head * settings.upperBody / 100 : 0;
  const lower = active ? head * settings.lowerBody / 100 : 0;
  const torso = upper * settings.torso / 100 * .60;
  const leftArm = torso + upper * settings.leftArm / 100 * .42;
  const rightArm = torso + upper * settings.rightArm / 100 * .42;
  const leftHand = leftArm + upper * settings.leftHand / 100 * .24;
  const rightHand = rightArm + upper * settings.rightHand / 100 * .24;
  const lowerBody = lower * .32;
  const leftLeg = lowerBody + lower * settings.leftLeg / 100 * .18;
  const rightLeg = lowerBody + lower * settings.rightLeg / 100 * .18;
  return { head, eye, shiftY, eyeY, front: front * settings.hairSway / 100,
    back: back * settings.hairSway / 100, torso, leftArm, rightArm, leftHand, rightHand, lowerBody, leftLeg, rightLeg };
}
