import test from "node:test";
import assert from "node:assert/strict";
import { androidFormFactor, registeredDevices, deviceDisplays } from "../src/device-layout.ts";
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

test("tablet uses logical short edge, independent of rotation and physical pixel count", () => {
  const screen = { width: 1600, height: 2560, scale: 2, isPrimary: true, isConnected: true };
  assert.equal(androidFormFactor([screen]), 'tablet');
  assert.equal(androidFormFactor([{ ...screen, width: 2560, height: 1600 }]), 'tablet');
  assert.equal(androidFormFactor([{ ...screen, width: 1440, height: 3200, scale: 4 }]), 'phone');
  assert.equal(androidFormFactor([{ ...screen, width: 1200 }]), 'tablet');
  assert.equal(androidFormFactor([{ ...screen, width: 1199 }]), 'phone');
});

test("external displays and old agents without density cannot misclassify a phone", () => {
  const phone = { width: 1080, height: 2400, scale: 3, isPrimary: true, canApplyWallpaper: true, isConnected: true };
  const external = { width: 3840, height: 2160, scale: 1, isPrimary: false, canApplyWallpaper: false, isConnected: true };
  assert.equal(androidFormFactor([external, phone]), 'phone');
  for (const scale of [undefined, 0, NaN, Infinity]) assert.equal(androidFormFactor([{ ...phone, scale }]), 'phone');
  assert.equal(androidFormFactor([external]), 'phone');
  const mapped = mapDeviceLibrary([{ id: 'tablet', name: 'Tablet', platform: 'Android', displays: [
    { ...phone, id: 'built-in', width: 2560, height: 1600, scale: 2, wallpapers: [] },
  ] }]).devices[0];
  assert.equal(mapped.formFactor, 'tablet');
  assert.equal(mapped.physicalDisplayCount, 1);
  assert.deepEqual(deviceDisplays(mapped).map(d => d.target), ['Lock', 'Home']);
});
