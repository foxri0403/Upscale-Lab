import test from "node:test";
import assert from "node:assert/strict";
import { interiorPoint, selectionPrompts, chooseSelection } from "../src/smart-selection.ts";

function fixture() {
  const width = 40, height = 30, guide = new Uint8Array(width * height), prior = guide.slice(), reference = guide.slice();
  for (let y = 3; y < 27; y++) for (let x = 3; x < 37; x++) guide[y * width + x] = 255;
  for (let y = 9; y < 22; y++) for (let x = 12; x < 28; x++) prior[y * width + x] = reference[y * width + x] = 255;
  return { width, height, guide, prior, reference, mode: "restore", tool: "brush" };
}
test("a broad brush including background seeds the interior of the known subject", () => {
  const input = fixture(), prompts = selectionPrompts(input), [x, y] = prompts.points[0];
  assert.equal(input.reference[y * input.width + x], 255);
  assert.equal(prompts.labels[0], 1);
});
test("rough lasso background corners are negative hints, not selected foreground", () => {
  const input = { ...fixture(), tool: "lasso" }, prompts = selectionPrompts(input);
  assert.ok(prompts.labels.includes(0));
  for (let i = 0; i < prompts.labels.length; i++) if (!prompts.labels[i]) {
    const [x,y] = prompts.points[i]; assert.equal(input.reference[y * input.width + x], 0);
  }
});
test("erase keeps a positive seed in the remaining subject and a negative seed in the unwanted region", () => {
  const input = { ...fixture(), mode: "erase" }; input.guide.fill(0);
  for (let y = 10; y < 17; y++) for (let x = 13; x < 20; x++) input.guide[y * input.width + x] = 255;
  const prompts = selectionPrompts(input);
  assert.deepEqual(prompts.labels, [1, 0]);
  const [keep, remove] = prompts.points;
  assert.equal(input.guide[keep[1] * input.width + keep[0]], 0);
  assert.equal(input.guide[remove[1] * input.width + remove[0]], 255);
});
test("candidate selection rejects a background mask and keeps the object rather than the painted rectangle", () => {
  const input = fixture(), n = input.width * input.height, logits = new Float32Array(n * 2).fill(-5), prompts = selectionPrompts(input);
  logits.fill(5, 0, n);
  for (let i = 0; i < n; i++) if (input.reference[i]) logits[n + i] = 5;
  const result = chooseSelection(logits, new Float32Array([.99, .9]), input, prompts.points, prompts.labels, prompts.referenceHits);
  assert.deepEqual(result.alpha, input.reference);
  assert.notDeepEqual(result.alpha, input.guide);
});
test("empty hints and invalid dimensions fail, and erasing only background leaves the subject unchanged", () => {
  assert.throws(() => interiorPoint(new Uint8Array(4), 3, 3));
  const input = fixture(); input.guide.fill(0);
  assert.throws(() => selectionPrompts(input));
  input.guide[0] = 255;
  assert.equal(selectionPrompts({ ...input, mode: "erase" }).unchanged, true);
});
test("unrelated islands in a textured background are removed while the prompted object remains", () => {
  const input = fixture(), logits = Float32Array.from(input.reference, value => value ? 5 : -5), prompts = selectionPrompts(input);
  for (let y = 2; y < 5; y++) for (let x = 2; x < 5; x++) logits[y * input.width + x] = 5;
  const result = chooseSelection(logits, new Float32Array([.95]), input, prompts.points, prompts.labels, prompts.referenceHits);
  assert.deepEqual(result.alpha, input.reference);
});
