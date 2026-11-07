import { useEffect, useRef, useState, type RefObject } from "react";
import { normalizeRig, rigTargets, type RigPart } from "./character-rig";
import type { HeadRig } from "./model";

/** PNG motion editor; each independent motion requires an actual separate transparent PNG. */
export function useCharacterRig(visible: boolean, value?: HeadRig, sourceKey?: string, externalMotion?: RefObject<{ x: number; y: number }>) {
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
    const bodyAngles: Record<string, number> = {};
    let pointerTilt = 0, pointerLook = 0, pointerY = 0;
    const images = Array.from(plane.querySelectorAll<HTMLImageElement>("img[data-rig-part]"));
    const move = (event: PointerEvent) => {
      if (!mouse || event.pointerType !== "mouse") return;
      const rect = box.getBoundingClientRect();
      if (!rect.width || !rect.height) return;
      pointerTilt = Math.max(-1, Math.min(1, (event.clientX - rect.left) / rect.width * 2 - 1));
      pointerLook = pointerTilt;
      pointerY = Math.max(-1, Math.min(1, (event.clientY - rect.top) / rect.height * 2 - 1));
    };
    const leave = () => { pointerTilt = 0; pointerLook = 0; pointerY = 0; };
    const step = (time: number) => {
      const dt = last ? Math.min(.05, Math.max(0, (time - last) / 1000)) : 1 / 60;
      last = time;
      phase += dt;
      const inputX = externalMotion?.current?.x ?? (mouse ? pointerTilt : tilt / 100);
      const inputY = externalMotion?.current?.y ?? (mouse ? pointerY : tiltY / 100);
      const target = rigTargets(inputX, inputY,
        externalMotion?.current?.x ?? (mouse ? pointerLook : look / 100),
        externalMotion?.current?.y ?? (mouse ? pointerY : lookY / 100), phase, settings);
      const smooth = (v: number, t: number, speed: number) => v + (t - v) * (1 - Math.exp(-speed * dt));
      head = smooth(head, target.head, 9);
      eye = smooth(eye, target.eye, 11);
      headY = smooth(headY, target.shiftY, 7);
      eyeY = smooth(eyeY, target.eyeY, 11);
      frontVelocity = (frontVelocity + (target.front - front) * 38 * dt) * Math.exp(-8 * dt);
      backVelocity = (backVelocity + (target.back - back) * 22 * dt) * Math.exp(-5 * dt);
      front += frontVelocity * dt;
      back += backVelocity * dt;
      for (const part of ["torso", "leftArm", "rightArm", "leftHand", "rightHand", "lowerBody", "leftLeg", "rightLeg"] as const)
        bodyAngles[part] = smooth(bodyAngles[part] ?? 0, target[part], part.includes("Hand") ? 5 : 7);
      const bounds = plane.getBoundingClientRect();
      const width = bounds.width;
      for (const img of images) {
        const part = img.dataset.rigPart as RigPart;
        if (part === "fixed") { img.style.removeProperty("transform"); continue; }
        const isBody = part === "torso" || part === "leftArm" || part === "rightArm" || part === "leftHand" || part === "rightHand";
        const isLower = part === "lowerBody" || part === "leftLeg" || part === "rightLeg";
        const pivotY = isBody ? settings.torsoPivotY : isLower ? settings.lowerPivotY : settings.pivotY;
        img.style.transformOrigin = `${settings.pivotX}% ${pivotY}%`;
        const headShift = settings.headTilt / 100 * inputX * width * .008;
        const gaze = part === "eye" ? eye * width : 0;
        const angle = (isBody || isLower) ? bodyAngles[part] :
          head + (part === "frontHair" ? front : part === "backHair" ? back : 0);
        const vertical = (isBody || isLower) ? 0 : (headY + (part === "eye" ? eyeY : 0)) * bounds.height;
        const horizontal = (isBody || isLower) ? bodyAngles[part] * width * .08 : headShift + gaze;
        img.style.transform = `translate(${horizontal}px, ${vertical}px) rotate(${angle}rad)`;
      }
      frame = requestAnimationFrame(step);
    };
    const stop = () => {
      cancelAnimationFrame(frame); frame = 0; last = 0;
      head = eye = headY = eyeY = front = back = frontVelocity = backVelocity = 0;
      for (const part of Object.keys(bodyAngles)) delete bodyAngles[part];
      for (const img of images) { img.style.removeProperty("transform"); img.style.removeProperty("transform-origin"); }
    };
    const reconcile = () => { stop(); if (!document.hidden) frame = requestAnimationFrame(step); };
    if (!externalMotion) {
      box.addEventListener("pointermove", move, { passive: true });
      box.addEventListener("pointerleave", leave, { passive: true });
    }
    reconcile();
    document.addEventListener("visibilitychange", reconcile);
    return () => {
      stop();
      document.removeEventListener("visibilitychange", reconcile);
      box.removeEventListener("pointermove", move);
      box.removeEventListener("pointerleave", leave);
    };
  }, [visible, sourceKey, settings.enabled, settings.headTilt, settings.eyeMove, settings.hairSway,
    settings.pivotX, settings.pivotY, settings.idle, settings.bodyMode, settings.upperBody, settings.lowerBody,
    settings.torso, settings.leftArm, settings.rightArm, settings.leftHand, settings.rightHand,
    settings.leftLeg, settings.rightLeg, settings.torsoPivotY, settings.lowerPivotY,
    JSON.stringify(settings.partAssignments), mouse, tilt, tiltY, look, lookY, externalMotion]);
  return { stage, layers, active: visible && settings.enabled, tilt, setTilt, tiltY, setTiltY, look, setLook, lookY, mouse, setMouse };
}
export type CharacterRigController = ReturnType<typeof useCharacterRig>;
