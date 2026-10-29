import test from "node:test";
import assert from "node:assert/strict";
import { combinedMotion, motionStep, orientationMotion } from "../src/parallax-motion.ts";

test("smoothing follows elapsed time equally at 60 and 120fps", () => {
  function simulate(fps) {
    let value = 0;
    for (let frame = 0; frame < fps; frame++) value = motionStep(value, 1, 1000 / fps);
    return value;
  }
  assert.ok(Math.abs(simulate(60) - simulate(120)) < 1e-9);
});
test("orientation calibrates neutral, remaps landscape and bounds motion", () => {
  assert.deepEqual(orientationMotion(10, 80, 10, 80, 0), { x: 0, y: 0 });
  const landscape = orientationMotion(20, 0, 0, 0, 90);
  assert.ok(Math.abs(landscape.x) < 1e-9);
  assert.equal(landscape.y, -1);
  assert.deepEqual(orientationMotion(170, 170, 0, 0, 0), { x: 1, y: 1 });
});

test("simultaneous mouse and gyro inputs combine without exceeding their shared motion range", () => {
  assert.deepEqual(combinedMotion({ x: .4, y: -.2 }, { x: .3, y: .5 }), { x: .7, y: .3 });
  assert.deepEqual(combinedMotion({ x: .8, y: -.8 }, { x: .8, y: -.8 }), { x: 1, y: -1 });
  assert.deepEqual(combinedMotion({ x: .5, y: -.5 }, { x: -.5, y: .5 }), { x: 0, y: 0 });
});
