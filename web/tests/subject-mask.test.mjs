import test from "node:test";
import assert from "node:assert/strict";
import { maskPoint } from "../src/subject-mask.ts";

test("brush positions stay in source pixels through zoom, scrolling and touch", () => {
  assert.deepEqual(maskPoint(300, 240, { left: 100, top: 40, width: 800, height: 600 }, 400, 300), { x: 100, y: 100 });
  assert.deepEqual(maskPoint(300, 240, { left: -100, top: -160, width: 1600, height: 1200 }, 400, 300), { x: 100, y: 100 });
  assert.deepEqual(maskPoint(-20, 900, { left: 0, top: 0, width: 400, height: 300 }, 400, 300), { x: 0, y: 300 });
});
