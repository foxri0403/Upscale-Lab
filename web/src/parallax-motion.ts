export const clampMotion = (value: number) => Number.isFinite(value) ? Math.max(-1, Math.min(1, value)) : 0;

export function combinedMotion(mouse: { x: number; y: number }, gyro: { x: number; y: number }) {
  return { x: clampMotion(mouse.x + gyro.x), y: clampMotion(mouse.y + gyro.y) };
}

// A time-based filter gives the same response at 60, 90, 120 and 144Hz.
export function motionStep(current: number, target: number, elapsedMs: number) {
  return current + (target - current) * (1 - Math.exp(-Math.max(0, Math.min(100, elapsedMs)) / 65));
}

export function orientationMotion(gamma: number, beta: number, originGamma: number, originBeta: number, angle: number) {
  const delta = (value: number) => ((value + 540) % 360) - 180;
  const x = delta(gamma - originGamma) / 20;
  const y = delta(beta - originBeta) / 20;
  const radians = angle * Math.PI / 180;
  return { x: clampMotion(x * Math.cos(radians) + y * Math.sin(radians)),
    y: clampMotion(y * Math.cos(radians) - x * Math.sin(radians)) };
}
