import test from "node:test";
import assert from "node:assert/strict";
import { registeredDevices, deviceDisplays } from "../src/device-layout.ts";
import { mapDeviceLibrary } from "../src/device-state.ts";
test("registration order survives refresh order and uses stable ID ties", () => {
  const devices = [{id:'b',created:'2026-10-28T00:00:00Z'}, {id:'c',created:'2026-10-29T00:00:00Z'}, {id:'a',created:'2026-10-28T00:00:00Z'}];
  assert.deepEqual(registeredDevices(devices).map(d=>d.id), ['a','b','c']);
  assert.deepEqual(registeredDevices([...devices].reverse()).map(d=>d.id), ['a','b','c']);
  assert.equal(devices[0].id,'b');
});
test("Android always shows lock then home even when the agent only reports home", () => {
  const device = mapDeviceLibrary([{id:'phone',name:'Phone',platform:'Android',displays:[{id:'screen',width:1080,height:2220,isConnected:true,wallpapers:[{target:'Home',observation:'Unknown'}]}]}], [], []).devices[0];
  assert.deepEqual(deviceDisplays(device).map(d=>d.target),['Lock','Home']);
  assert.equal(device.physicalDisplayCount,1);
});
