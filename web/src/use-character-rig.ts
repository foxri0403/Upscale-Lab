import { useEffect, useRef, useState } from "react";
import { normalizeRig, rigTargets, type RigPart } from "./character-rig";
import type { HeadRig } from "./model";

/** Browser implementation of the earlier PNG head-motion MVP.
 * Only face, eye and hair layers move. Body, arms, clothes and accessories stay fixed.
 * Preview-only: does not imply native Android/Windows rig playback.
 */
export function useCharacterRig(visible: boolean, value?: HeadRig, sourceKey?: string) {
  const stage = useRef<HTMLDivElement>(null);
  const layers = useRef<HTMLDivElement>(null);
  const [tilt, setTilt] = useState(0);
  const [look, setLook] = useState(0);
  const [tiltY, setTiltY] = useState(0);
  const [lookY, setLookY] = useState(0);
  const [mouse, setMouse] = useState(false);
  const settings = normalizeRig(value);
  useEffect(() => {
    const plane = layers.current;
    const box = stage.current;
    if (!plane || !box || !visible || !settings.enabled) return;
    let frame = 0, last = 0, phase = 0;
    let head = 0, eye = 0, headY = 0, eyeY = 0, front = 0, back = 0;
    let frontVelocity = 0, backVelocity = 0;
    let pointerTilt = 0, pointerLook = 0, pointerY = 0;
    const images = Array.from(plane.querySelectorAll<HTMLImageElement>("img[data-rig-part]"));
    const move = (event: PointerEvent) => {
      if (!mouse || event.pointerType !== "mouse") return;
      const rect = box.getBoundingClientRect();
      if (!rect.width) return;
      pointerTilt = Math.max(-1, Math.min(1, (event.clientX - rect.left) / rect.width * 2 - 1));
      pointerLook = pointerTilt;
      pointerY = Math.max(-1, Math.min(1, (event.clientY - rect.top) / rect.height * 2 - 1));
    };
    const leave = () => { pointerTilt = 0; pointerLook = 0; pointerY = 0; };
    const step = (time: number) => {
      const dt = last ? Math.min(.05, Math.max(0, (time - last) / 1000)) : 1 / 60;
      last = time;
      phase += dt;
      const target = rigTargets(mouse ? pointerTilt : tilt / 100, mouse ? pointerY : tiltY / 100,
        mouse ? pointerLook : look / 100, mouse ? pointerY : lookY / 100, phase, settings);
      const smooth = (v: number, t: number, speed: number) => v + (t - v) * (1 - Math.exp(-speed * dt));
      head = smooth(head, target.head, 9);
      eye = smooth(eye, target.eye, 11);
      headY = smooth(headY, target.shiftY, 7);
      eyeY = smooth(eyeY, target.eyeY, 11);
      frontVelocity = (frontVelocity + (target.front - front) * 38 * dt) * Math.exp(-8 * dt);
      backVelocity = (backVelocity + (target.back - back) * 22 * dt) * Math.exp(-5 * dt);
      front += frontVelocity * dt;
      back += backVelocity * dt;
      const bounds = plane.getBoundingClientRect();
      const width = bounds.width;
      for (const img of images) {
        const part = img.dataset.rigPart as RigPart;
        // Pivot is relative to the full untrimmed PNG canvas.
        img.style.transformOrigin = `${settings.pivotX}% ${settings.pivotY}%`;
        if (part === "fixed") continue;
        const shift = settings.headTilt / 100 * (mouse ? pointerTilt : tilt / 100) * width * .008;
        const gaze = part === "eye" ? eye * width : 0;
        const angle = head + (part === "frontHair" ? front : part === "backHair" ? back : 0);
        const vertical = (headY + (part === "eye" ? eyeY : 0)) * bounds.height;
        img.style.transform = `translate(${shift + gaze}px, ${vertical}px) rotate(${angle}rad)`;
      }
      frame = requestAnimationFrame(step);
    };
    box.addEventListener("pointermove", move, { passive: true });
    box.addEventListener("pointerleave", leave, { passive: true });
    frame = requestAnimationFrame(step);
    return () => {
      cancelAnimationFrame(frame);
      box.removeEventListener("pointermove", move);
      box.removeEventListener("pointerleave", leave);
      for (const img of images) { img.style.removeProperty("transform"); img.style.removeProperty("transform-origin"); }
    };
  }, [visible, sourceKey, settings.enabled, settings.headTilt, settings.eyeMove, settings.hairSway,
    settings.pivotX, settings.pivotY, settings.idle, mouse, tilt, tiltY, look, lookY]);
  return { stage, layers, active: visible && settings.enabled, tilt, setTilt, tiltY, setTiltY, look, setLook, lookY, setLookY, mouse, setMouse };
}
export type CharacterRigController = ReturnType<typeof useCharacterRig>;
