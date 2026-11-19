import { useEffect, useRef, type RefObject } from "react";
import { normalizeRig, rigTargets, type RigPart } from "./character-rig";
import type { HeadRig } from "./model";

/** PNG motion editor; each independent motion requires an actual separate transparent PNG. */
export function useCharacterRig(visible: boolean, value?: HeadRig, sourceKey?: string, externalMotion?: RefObject<{ x: number; y: number }>, automatic = false) {
  const stage = useRef<HTMLDivElement>(null);
  const layers = useRef<HTMLDivElement>(null);
  const settings = normalizeRig(value);
  useEffect(() => {
    const plane = layers.current;
    const box = stage.current;
    if (!plane || !box || !visible || !settings.enabled) return;
    let frame = 0, last = 0, phase = 0;
    let head = 0, eye = 0, headY = 0, eyeY = 0, front = 0, back = 0;
    let motionX = 0, motionY = 0;
    let frontVelocity = 0, backVelocity = 0;
    const bodyAngles: Record<string, number> = {};
    // Measure only on layout changes. getBoundingClientRect() inside each animation
    // frame can force synchronous layout when the editor is busy resizing panels.
    let width = 0, height = 0;
    const measure = () => {
      const bounds = plane.getBoundingClientRect();
      width = bounds.width; height = bounds.height;
    };
    measure();
    const observer = typeof ResizeObserver !== "undefined" ? new ResizeObserver(measure) : null;
    observer?.observe(plane);
    const images = Array.from(plane.querySelectorAll<HTMLImageElement>("img[data-rig-part]"));
    const animated = images.flatMap(img => {
      const part = img.dataset.rigPart as RigPart;
      if (part === "fixed") { img.style.removeProperty("transform"); return []; }
      const isBody = part === "torso" || part === "leftArm" || part === "rightArm" || part === "leftHand" || part === "rightHand";
      const isLower = part === "lowerBody" || part === "leftLeg" || part === "rightLeg";
      const pivotY = isBody ? settings.torsoPivotY : isLower ? settings.lowerPivotY : settings.pivotY;
      img.style.transformOrigin = `${settings.pivotX}% ${pivotY}%`;
      return [{ img, part, isBody, isLower }];
    });
    const step = (time: number) => {
      const dt = last ? Math.min(.05, Math.max(0, (time - last) / 1000)) : 1 / 60;
      last = time;
      phase += dt;
      const inputX = automatic ? Math.sin(phase * 2.4) * .9 : externalMotion?.current?.x ?? 0;
      const inputY = automatic ? Math.sin(phase * 1.5) * .65 : externalMotion?.current?.y ?? 0;
      // Even though the angular rig is smoothed, the old translation used the
      // raw pointer/gyro input. Smooth both axes so the head and body do not jerk.
      const inputAlpha = 1 - Math.exp(-22 * dt);
      const bound = (v: number) => Number.isFinite(v) ? Math.max(-1, Math.min(1, v)) : 0;
      motionX += (bound(inputX) - motionX) * inputAlpha;
      motionY += (bound(inputY) - motionY) * inputAlpha;
      const target = rigTargets(motionX, motionY, motionX, motionY, phase, settings);
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
      for (const { img, part, isBody, isLower } of animated) {
        const headShift = settings.faceEnabled !== false ? settings.headTilt / 100 * motionX * width * .008 : 0;
        const gaze = part === "eye" ? eye * width : 0;
        const angle = (isBody || isLower) ? bodyAngles[part] :
          head + (part === "frontHair" ? front : part === "backHair" ? back : 0);
        const vertical = (isBody || isLower) ? 0 : (headY + (part === "eye" ? eyeY : 0)) * height;
        const horizontal = (isBody || isLower)
          ? (settings.bodyMode === "follow" ? bodyAngles[part] * width * .13 + motionX * width * .0035 : 0) : headShift + gaze;
        const bodyVertical = (isBody || isLower) ? (settings.bodyMode === "follow" ? motionY * height * .0025 : 0) : vertical;
        img.style.transform = `translate(${horizontal}px, ${bodyVertical}px) rotate(${angle}rad)`;
      }
      frame = requestAnimationFrame(step);
    };
    const stop = (clearOrigin = false) => {
      cancelAnimationFrame(frame); frame = 0; last = 0;
      head = eye = headY = eyeY = front = back = frontVelocity = backVelocity = 0;
      motionX = motionY = 0;
      for (const part of Object.keys(bodyAngles)) delete bodyAngles[part];
      for (const img of images) {
        img.style.removeProperty("transform");
        if (clearOrigin) img.style.removeProperty("transform-origin");
      }
    };
    const reconcile = () => { stop(); if (!document.hidden) frame = requestAnimationFrame(step); };
    reconcile();
    document.addEventListener("visibilitychange", reconcile);
    return () => {
      stop(true);
      observer?.disconnect();
      document.removeEventListener("visibilitychange", reconcile);
    };
  }, [visible, sourceKey, settings.enabled, settings.faceEnabled, settings.headTilt, settings.eyeMove, settings.hairSway,
    settings.pivotX, settings.pivotY, settings.idle, settings.bodyMode, settings.upperBody, settings.lowerBody,
    settings.torso, settings.leftArm, settings.rightArm, settings.leftHand, settings.rightHand,
    settings.leftLeg, settings.rightLeg, settings.torsoPivotY, settings.lowerPivotY,
    JSON.stringify(settings.partAssignments), externalMotion, automatic]);
  return { stage, layers, active: visible && settings.enabled };
}
export type CharacterRigController = ReturnType<typeof useCharacterRig>;
