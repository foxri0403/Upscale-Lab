export const defaultMotionStrength = 50;
export const defaultSelectionSize = 150;
export const maxSelectionSize = 500;

export function motionStrength(value?: { strength: number; motionDefaultsVersion?: number }) {
  if (!value || !Number.isFinite(value.strength) || !value.motionDefaultsVersion && value.strength === 57) return defaultMotionStrength;
  return Math.max(0, Math.min(100, Math.round(value.strength)));
}
