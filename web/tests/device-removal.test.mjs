import test from 'node:test';
import assert from 'node:assert/strict';
import { mapDeviceLibrary } from '../src/device-state.ts';

test('linked devices distinguish first connection, fresh heartbeat, stale heartbeat and revoked access', () => {
  const base={name:'PC',platform:'Windows',displays:[]};
  const states=[{...base,id:'fresh',isLinked:true,recentlySeen:true,lastSeenAt:'2026-10-28T00:00:00Z'},
    {...base,id:'first',isLinked:true,recentlySeen:false,lastSeenAt:null},
    {...base,id:'stale',isLinked:true,recentlySeen:false,lastSeenAt:'2026-10-27T00:00:00Z'},
    {...base,id:'revoked',isLinked:false,recentlySeen:true,lastSeenAt:null}];
  assert.deepEqual(mapDeviceLibrary(states).devices.map(d=>d.connection),['connected','connecting','disconnected','disconnected']);
});

test('pending removal keeps the current preview until the agent reports actual completion', () => {
  const state={id:'d',name:'PC',platform:'Windows',isLinked:true,recentlySeen:true,supportsWallpaperRemoval:true,
    displays:[{id:'m',name:'Monitor',width:1920,height:1080,isConnected:true,
      wallpapers:[{target:'Desktop',reportedAssetId:'a',observation:'Managed',pendingRemoval:true}]}]};
  const pending=mapDeviceLibrary([state]);
  assert.equal(pending.devices[0].displays[0].current,'asset:a');
  assert.equal(pending.devices[0].displays[0].removing,true);
  state.displays[0].wallpapers=[{target:'Desktop',reportedAssetId:null,observation:'Cleared',pendingRemoval:false}];
  const done=mapDeviceLibrary([state]);
  assert.equal(done.devices[0].displays[0].current,'');
  assert.equal(done.devices[0].displays[0].observation,'Cleared');
  assert.equal(done.devices[0].displays[0].removing,false);
  assert.equal(done.devices[0].supportsWallpaperRemoval,true);
  assert.equal(mapDeviceLibrary([{...state,supportsWallpaperRemoval:undefined}]).devices[0].supportsWallpaperRemoval,false);
});
