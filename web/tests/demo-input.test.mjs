import test from "node:test";
import assert from "node:assert/strict";
import { defaultDemoInput, createOrientationPermissionRequest } from "../src/demo-input.ts";

test("desktop and touch laptops default to mouse; touch-first phones/tablets default to gyro", () => {
  assert.equal(defaultDemoInput(false, true, 0), "mouse");
  assert.equal(defaultDemoInput(false, true, 10), "mouse");
  assert.equal(defaultDemoInput(true, false, 5), "gyro");
  assert.equal(defaultDemoInput(true, true, 5), "gyro");
  assert.equal(defaultDemoInput(false, false, 5), "gyro");
});

test("both demos share one synchronous native request from the first gesture", async () => {
  const request = createOrientationPermissionRequest();
  let calls = 0, finish;
  const native = () => { calls++; return new Promise(resolve => { finish = resolve; }); };
  const subject = request(native), character = request(native);
  assert.equal(calls, 1, "native permission must be invoked before leaving the gesture");
  assert.equal(subject, character);
  finish("granted");
  await Promise.all([subject, character]);
  await request(native);
  assert.equal(calls, 1, "the second listener must also reuse already-settled permission");
});

test("denied and synchronous failures propagate without latching a stale permission request", async () => {
  const request = createOrientationPermissionRequest();
  await assert.rejects(request(async () => "denied"), /권한/);
  await new Promise(resolve => setTimeout(resolve, 0));
  await assert.rejects(request(() => { throw new Error("requires activation"); }), /activation/);
  await new Promise(resolve => setTimeout(resolve, 0));
  await request(async () => "granted");
  await request(); // Browsers without a requestPermission API need no prompt.
});

test("even an immediately denied permission is shared until the native gesture ends", async () => {
  const request = createOrientationPermissionRequest();
  let calls = 0;
  const native = async () => { calls++; return "denied"; };
  await assert.rejects(request(native), /권한/);
  await assert.rejects(request(native), /권한/);
  assert.equal(calls, 1);
});
