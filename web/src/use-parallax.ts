import { useCallback, useEffect, useRef, useState } from "react";
import { clampMotion, combinedMotion, motionStep, orientationMotion } from "./parallax-motion";
import { requestOrientationPermission } from "./demo-input";

type OrientationConstructor = typeof DeviceOrientationEvent & { requestPermission?: () => Promise<string> };
type SensorPolicy = { allowsFeature: (name: string) => boolean };
function gyroSupported() {
  return window.isSecureContext && typeof DeviceOrientationEvent !== "undefined";
}

export function useGyroPermission(detectGranted = false) {
  const [gyroReady, setGyroReady] = useState(() => gyroSupported() && !(DeviceOrientationEvent as OrientationConstructor).requestPermission);
  const [gyroPending, setGyroPending] = useState(false);
  useEffect(() => {
    if (!detectGranted || gyroReady || !gyroSupported()) return;
    function receive(event: DeviceOrientationEvent) {
      if (event.gamma === null || event.beta === null || !Number.isFinite(event.gamma) || !Number.isFinite(event.beta)) return;
      setGyroReady(true);
    }
    window.addEventListener("deviceorientation", receive, { passive: true });
    return () => window.removeEventListener("deviceorientation", receive);
  }, [detectGranted, gyroReady]);
  const requestGyro = useCallback(async () => {
    if (!gyroSupported()) throw new Error("이 브라우저에서는 기울기 센서를 사용할 수 없습니다.");
    const policy = (document as Document & { permissionsPolicy?: SensorPolicy; featurePolicy?: SensorPolicy }).permissionsPolicy
      ?? (document as Document & { featurePolicy?: SensorPolicy }).featurePolicy;
    if (policy && (!policy.allowsFeature("gyroscope") || !policy.allowsFeature("accelerometer")))
      throw new Error("브라우저에서 기울기 센서 사용을 허용해 주세요.");
    setGyroPending(true);
    try {
      // Invoke directly from the user gesture, before any asynchronous work.
      const request = (DeviceOrientationEvent as OrientationConstructor).requestPermission;
      await requestOrientationPermission(request ? () => request.call(DeviceOrientationEvent) : undefined);
      setGyroReady(true);
    } catch (error) {
      setGyroReady(false);
      throw error instanceof Error ? error : new Error("기울기 센서 권한을 확인해 주세요.");
    } finally { setGyroPending(false); }
  }, []);
  return { gyroReady, gyroPending, requestGyro, gyroSupported: gyroSupported() };
}

/** Request sensor access automatically, using the first page gesture where the browser requires it. */
export function useAutomaticGyro(enabled: boolean, ready: boolean, requestGyro: () => Promise<void>) {
  const [error, setError] = useState("");
  useEffect(() => {
    setError("");
    if (!enabled) return;
    const orientation = typeof DeviceOrientationEvent !== "undefined"
      ? DeviceOrientationEvent as OrientationConstructor : undefined;
    let disposed = false;
    let attempted = false;
    const remove = () => {
      window.removeEventListener("pointerup", activate, true);
      window.removeEventListener("click", activate, true);
      window.removeEventListener("keydown", activate, true);
    };
    function activate() {
      if (attempted) return;
      attempted = true;
      remove();
      void requestGyro().catch(failure => { if (!disposed) setError((failure as Error).message); });
    }
    if (orientation?.requestPermission) {
      if (ready) return;
      window.addEventListener("pointerup", activate, { capture: true, passive: true });
      window.addEventListener("click", activate, { capture: true, passive: true });
      window.addEventListener("keydown", activate, true);
    } else activate();
    return () => { disposed = true; remove(); };
  }, [enabled, ready, requestGyro]);
  return error;
}

type PreviewInput = { gyroReady?: boolean; pointerPadding?: number; onPointerSettled?: () => void };

export function useParallax(enabled: boolean, strength: number, mouseEnabled: boolean, gyroEnabled: boolean,
  sourceKey?: string, manualMotion?: { x: number; y: number }, globalPointer = false, preview?: PreviewInput) {
  const stage = useRef<HTMLDivElement>(null);
  const layers = useRef<HTMLDivElement>(null);
  const gain = useRef(strength);
  gain.current = strength;
  const manual = useRef(manualMotion);
  manual.current = manualMotion;
  const position = useRef({ x: 0, y: 0 });
  const permission = useGyroPermission();
  const gyroReady = preview?.gyroReady ?? permission.gyroReady;
  const pointerPadding = preview?.pointerPadding;
  const onPointerSettled = preview?.onPointerSettled;
  const active = enabled && (mouseEnabled || (gyroEnabled && gyroReady));

  useEffect(() => {
    const element = stage.current;
    const planes = layers.current;
    if (!element || !planes) return;
    let frame = 0, previous = 0, x = 0, y = 0, settleTimer = 0;
    let mouse = { x: 0, y: 0 }, gyro = { x: 0, y: 0 };
    let origin: { gamma: number; beta: number; angle: number } | null = null;
    const paint = () => {
      position.current = { x, y };
      const bounds = element.getBoundingClientRect();
      planes.style.setProperty("--motion-x", `${x * bounds.width * .035 * gain.current / 100}px`);
      planes.style.setProperty("--motion-y", `${y * bounds.height * .035 * gain.current / 100}px`);
    };
    const animate = (time: number) => {
      const target = combinedMotion(combinedMotion(mouse, gyro), manual.current ?? { x: 0, y: 0 });
      x = motionStep(x, target.x, previous ? time - previous : 1000 / 60);
      y = motionStep(y, target.y, previous ? time - previous : 1000 / 60);
      previous = time; paint(); frame = requestAnimationFrame(animate);
    };
    const pointer = (event: PointerEvent) => {
      if (event.pointerType !== "mouse") return;
      const bounds = globalPointer ? { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight } : element.getBoundingClientRect();
      if (pointerPadding !== undefined && (event.clientX < bounds.left - pointerPadding ||
        event.clientY < bounds.top - pointerPadding || event.clientX > bounds.left + bounds.width + pointerPadding ||
        event.clientY > bounds.top + bounds.height + pointerPadding)) {
        leavePointer();
        return;
      }
      cancelSettle();
      mouse = { x: clampMotion((event.clientX - bounds.left) / bounds.width * 2 - 1),
        y: clampMotion((event.clientY - bounds.top) / bounds.height * 2 - 1) };
    };
    const resetMouse = () => { mouse = { x: 0, y: 0 }; };
    function cancelSettle() { window.clearTimeout(settleTimer); settleTimer = 0; }
    function leavePointer() {
      resetMouse();
      if (onPointerSettled && !settleTimer) settleTimer = window.setTimeout(onPointerSettled, 240);
    }
    const leaveWindow = (event: PointerEvent) => { if (!event.relatedTarget) leavePointer(); };
    const calibrate = () => { origin = null; gyro = { x: 0, y: 0 }; };
    const orientation = (event: DeviceOrientationEvent) => {
      if (event.gamma === null || event.beta === null || !Number.isFinite(event.gamma) || !Number.isFinite(event.beta)) return;
      const angle = screen.orientation?.angle ?? (window as Window & { orientation?: number }).orientation ?? 0;
      if (!origin || origin.angle !== angle) origin = { gamma: event.gamma, beta: event.beta, angle };
      gyro = orientationMotion(event.gamma, event.beta, origin.gamma, origin.beta, angle);
    };
    const stop = () => {
      cancelAnimationFrame(frame); frame = 0; previous = 0;
      cancelSettle();
      element.removeEventListener("pointermove", pointer); element.removeEventListener("pointerleave", resetMouse);
      window.removeEventListener("pointermove", pointer); window.removeEventListener("pointerout", leaveWindow);
      window.removeEventListener("blur", leavePointer);
      window.removeEventListener("deviceorientation", orientation);
      window.removeEventListener("orientationchange", calibrate); screen.orientation?.removeEventListener("change", calibrate);
      resetMouse(); calibrate(); x = 0; y = 0; paint();
    };
    const reconcile = () => {
      stop();
      if (!active || document.hidden) return;
      if (mouseEnabled) {
        if (globalPointer || pointerPadding !== undefined) {
          window.addEventListener("pointermove", pointer, { passive: true });
          window.addEventListener("pointerout", leaveWindow, { passive: true });
          window.addEventListener("blur", leavePointer);
        } else {
          element.addEventListener("pointermove", pointer, { passive: true });
          element.addEventListener("pointerleave", resetMouse, { passive: true });
        }
      }
      if (gyroEnabled && gyroReady) {
        window.addEventListener("deviceorientation", orientation, { passive: true });
        window.addEventListener("orientationchange", calibrate, { passive: true }); screen.orientation?.addEventListener("change", calibrate);
      }
      frame = requestAnimationFrame(animate);
    };
    reconcile(); document.addEventListener("visibilitychange", reconcile);
    return () => { stop(); document.removeEventListener("visibilitychange", reconcile); };
  }, [active, mouseEnabled, gyroEnabled, gyroReady, sourceKey, globalPointer, pointerPadding, onPointerSettled]);
  return { stage, layers, position, active, gyroReady, gyroPending: permission.gyroPending, requestGyro: permission.requestGyro };
}

export type ParallaxMotion = ReturnType<typeof useParallax>;
