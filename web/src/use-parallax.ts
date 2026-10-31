import { useCallback, useEffect, useRef, useState } from "react";
import { clampMotion, combinedMotion, motionStep, orientationMotion } from "./parallax-motion";

type OrientationConstructor = typeof DeviceOrientationEvent & { requestPermission?: () => Promise<string> };
type SensorPolicy = { allowsFeature: (name: string) => boolean };
function gyroSupported() {
  return window.isSecureContext && typeof DeviceOrientationEvent !== "undefined";
}

export function useParallax(enabled: boolean, strength: number, mouseEnabled: boolean, gyroEnabled: boolean, sourceKey?: string) {
  const stage = useRef<HTMLDivElement>(null);
  const layers = useRef<HTMLDivElement>(null);
  const gain = useRef(strength);
  gain.current = strength;
  const [gyroReady, setGyroReady] = useState(() => gyroSupported() && !(DeviceOrientationEvent as OrientationConstructor).requestPermission);
  const [gyroPending, setGyroPending] = useState(false);
  const active = enabled && (mouseEnabled || (gyroEnabled && gyroReady));
  const requestGyro = useCallback(async () => {
    if (!gyroSupported()) throw new Error("이 브라우저에서는 기울기 센서를 사용할 수 없습니다.");
    const policy = (document as Document & { permissionsPolicy?: SensorPolicy; featurePolicy?: SensorPolicy }).permissionsPolicy
      ?? (document as Document & { featurePolicy?: SensorPolicy }).featurePolicy;
    if (policy && (!policy.allowsFeature("gyroscope") || !policy.allowsFeature("accelerometer")))
      throw new Error("브라우저에서 기울기 센서 사용을 허용해 주세요.");
    setGyroPending(true);
    try {
      // Invoke directly from the button gesture, before any asynchronous work.
      const request = (DeviceOrientationEvent as OrientationConstructor).requestPermission;
      if (request && await request.call(DeviceOrientationEvent) !== "granted")
        throw new Error("기울기 센서 권한을 허용해 주세요.");
      setGyroReady(true);
    } catch (error) {
      setGyroReady(false);
      throw error instanceof Error ? error : new Error("기울기 센서 권한을 확인해 주세요.");
    } finally { setGyroPending(false); }
  }, []);

  useEffect(() => {
    const element = stage.current;
    const planes = layers.current;
    if (!element || !planes) return;
    let frame = 0, previous = 0, x = 0, y = 0;
    let mouse = { x: 0, y: 0 }, gyro = { x: 0, y: 0 };
    let origin: { gamma: number; beta: number; angle: number } | null = null;
    const paint = () => {
      const bounds = element.getBoundingClientRect();
      planes.style.setProperty("--motion-x", `${x * bounds.width * .035 * gain.current / 100}px`);
      planes.style.setProperty("--motion-y", `${y * bounds.height * .035 * gain.current / 100}px`);
    };
    const animate = (time: number) => {
      const target = combinedMotion(mouse, gyro);
      x = motionStep(x, target.x, previous ? time - previous : 1000 / 60);
      y = motionStep(y, target.y, previous ? time - previous : 1000 / 60);
      previous = time; paint(); frame = requestAnimationFrame(animate);
    };
    const pointer = (event: PointerEvent) => {
      if (event.pointerType !== "mouse") return;
      const bounds = element.getBoundingClientRect();
      mouse = { x: clampMotion((event.clientX - bounds.left) / bounds.width * 2 - 1),
        y: clampMotion((event.clientY - bounds.top) / bounds.height * 2 - 1) };
    };
    const resetMouse = () => { mouse = { x: 0, y: 0 }; };
    const calibrate = () => { origin = null; gyro = { x: 0, y: 0 }; };
    const orientation = (event: DeviceOrientationEvent) => {
      if (event.gamma === null || event.beta === null || !Number.isFinite(event.gamma) || !Number.isFinite(event.beta)) return;
      const angle = screen.orientation?.angle ?? (window as Window & { orientation?: number }).orientation ?? 0;
      if (!origin || origin.angle !== angle) origin = { gamma: event.gamma, beta: event.beta, angle };
      gyro = orientationMotion(event.gamma, event.beta, origin.gamma, origin.beta, angle);
    };
    const stop = () => {
      cancelAnimationFrame(frame); frame = 0; previous = 0;
      element.removeEventListener("pointermove", pointer); element.removeEventListener("pointerleave", resetMouse);
      window.removeEventListener("deviceorientation", orientation);
      window.removeEventListener("orientationchange", calibrate); screen.orientation?.removeEventListener("change", calibrate);
      resetMouse(); calibrate(); x = 0; y = 0; paint();
    };
    const reconcile = () => {
      stop();
      if (!active || document.hidden) return;
      if (mouseEnabled) {
        element.addEventListener("pointermove", pointer, { passive: true });
        element.addEventListener("pointerleave", resetMouse, { passive: true });
      }
      if (gyroEnabled && gyroReady) {
        window.addEventListener("deviceorientation", orientation, { passive: true });
        window.addEventListener("orientationchange", calibrate, { passive: true }); screen.orientation?.addEventListener("change", calibrate);
      }
      frame = requestAnimationFrame(animate);
    };
    reconcile(); document.addEventListener("visibilitychange", reconcile);
    return () => { stop(); document.removeEventListener("visibilitychange", reconcile); };
  }, [active, mouseEnabled, gyroEnabled, gyroReady, sourceKey]);
  return { stage, layers, active, gyroReady, gyroPending, requestGyro };
}

export type ParallaxMotion = ReturnType<typeof useParallax>;
