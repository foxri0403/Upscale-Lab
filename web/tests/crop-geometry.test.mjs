import test from 'node:test';
import assert from 'node:assert/strict';
import { cropPresets, cropRegion, pixelCrop, moveCrop, resizeCrop, zoomCrop, validCrop } from '../src/crop-geometry.ts';
test('preset order and inscribed pixel bounds preserve the source',()=>{
 assert.deepEqual(cropPresets.map(v=>v[0]),['original','wide','portrait','square','custom']);
 assert.deepEqual(pixelCrop(400,300,{crop:'wide'}),{x:0,y:38,width:400,height:225});
 assert.deepEqual(pixelCrop(400,300,{crop:'portrait'}),{x:116,y:0,width:169,height:300});
 assert.deepEqual(pixelCrop(400,300,{crop:'original',cropRegion:{x:.2,y:.2,width:.2,height:.2}}),{x:0,y:0,width:400,height:300});
});
test('movement is constrained to the source and does not change aspect',()=>{
 const original=cropRegion(400,300,{crop:'wide'}),moved=moveCrop(original,100,-100);
 assert.equal(moved.x,0);assert.equal(moved.y,0);assert.equal(moved.width,original.width);assert.equal(moved.height,original.height);assert(validCrop(moved));
});
test('custom handles resize each edge, keep the opposite corner and stay in bounds',()=>{
 const rect={x:.2,y:.2,width:.4,height:.4};
 const a=resizeCrop(rect,'se',.2,.1,400,300);assert(Math.abs(a.width-.6)<1e-10);assert(Math.abs(a.height-.5)<1e-10);assert.equal(a.x,.2);
 const b=resizeCrop(rect,'nw',-10,-10,400,300);assert.equal(b.x,0);assert.equal(b.y,0);assert(Math.abs(b.width-.6)<1e-10);
 for(const handle of ['n','ne','e','se','s','sw','w','nw'])for(const delta of [-100,100])assert(validCrop(resizeCrop(rect,handle,delta,delta,400,300)));
});
test('zoom keeps its aspect, remains bounded, and never shrinks below one source pixel',()=>{
 const rect=cropRegion(400,300,{crop:'portrait'}),zoomed=zoomCrop(rect,2,400,300);assert(Math.abs(zoomed.width/zoomed.height-rect.width/rect.height)<1e-10);
 const tiny=zoomCrop(zoomed,1e8,400,300);assert(tiny.width*400>=1-1e-9&&tiny.height*300>=1-1e-9);assert(validCrop(tiny));
 const back=zoomCrop(zoomed,1e-8,400,300);assert(validCrop(back));assert(Math.abs(back.width/back.height-rect.width/rect.height)<1e-10);
});
test('persisted non-centered selections resolve to the exact pixels after reload and upscale',()=>{
 const edit={crop:'custom',cropRegion:{x:.25,y:.1,width:.5,height:.6}};
 assert.deepEqual(pixelCrop(400,300,JSON.parse(JSON.stringify(edit))),{x:100,y:30,width:200,height:180});
 assert.deepEqual(pixelCrop(800,600,edit),{x:200,y:60,width:400,height:360});assert.equal(validCrop({x:.9,y:0,width:.2,height:1}),false);
});
