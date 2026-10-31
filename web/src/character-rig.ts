import { defaultHeadRig, type HeadRig } from "./model";

export type RigPart = "fixed" | "head" | "frontHair" | "backHair" | "eye";
/** Semantic layer names from SeeThroughManifestReader; no pixel-content guessing. */
export function rigPart(type: string): RigPart {
  switch (type) {
    case "Face": case "Eyebrow": case "Mouth": case "Nose": return "head";
    case "Eye": return "eye";
    case "BackHair": return "backHair";
    case "FrontHair": case "Hair": return "frontHair";
    default: return "fixed";
  }
}
export function normalizeRig(value?: Partial<HeadRig> | null): HeadRig {
  const result = { ...defaultHeadRig, ...value };
  const safe = (x: number, fallback: number) => Number.isFinite(x) ? Math.min(100, Math.max(0, x)) : fallback;
  for (const field of ["headTilt", "eyeMove", "hairSway", "pivotX", "pivotY"] as const)
    result[field] = safe(result[field], defaultHeadRig[field]);
  result.enabled = result.enabled === true;
  result.idle = result.idle !== false;
  return result;
}
export function rigTargets(tilt: number, tiltY: number, look: number, lookY: number, time: number, settings: HeadRig) {
  const head = settings.headTilt / 100 * (Math.max(-1, Math.min(1, tilt)) * .115 + (settings.idle ? Math.sin(time * 1.5) * .009 : 0));
  const eye = Math.max(-1, Math.min(1, look)) * settings.eyeMove / 100 * .018;
  const front = -head * .55 + (settings.idle ? Math.sin(time * 1.5) * .013 : 0);
  const back = -head * .28 + (settings.idle ? Math.sin(time * 1.5) * .009 : 0);
  const boundedY = Math.max(-1, Math.min(1, tiltY));
  const boundedEyeY = Math.max(-1, Math.min(1, lookY));
  const shiftY = boundedY * settings.headTilt / 100 * .007 + (settings.idle ? Math.sin(time * 1.5) * .0015 : 0);
  const eyeY = boundedEyeY * settings.eyeMove / 100 * .014;
  return { head, eye, shiftY, eyeY, front: front * settings.hairSway / 100, back: back * settings.hairSway / 100 };
}
