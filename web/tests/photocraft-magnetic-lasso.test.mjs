import test from 'node:test';
import assert from 'node:assert/strict';
import { MagneticLasso } from '../src/vendor/photocraft-magnetic-lasso.ts';
function field(w, h, color) {
  const rgba = new Uint8ClampedArray(w*h*4);
  for(let y=0;y<h;y++)for(let x=0;x<w;x++)rgba.set(color(x,y),(y*w+x)*4);
  return new MagneticLasso(w,h,rgba);
}
test('magnetic lasso snaps to subpixel colour boundaries and follows a nearby edge',()=>{
  const f=field(100,100,x=>x<50?[255,0,0,255]:[0,255,0,255]);
  const a=f.snap({x:55,y:10}), b=f.snap({x:55,y:90});
  assert(Math.abs(a.x-50)<.01); assert(Math.abs(b.x-50)<.01);
  const path=f.trace(a,b);
  assert(path.length>60); assert(path.every(p=>Math.abs(p.x-50)<.01));
});
test('flat or transparent areas preserve the pointer guide, and distant edges are ignored',()=>{
  const flat=field(100,100,()=>[90,90,90,255]);
  const a={x:10,y:10},b={x:80,y:80}; assert.deepEqual(flat.snap(a),a); assert.deepEqual(flat.trace(a,b),[a,b]);
  const transparent=field(100,100,x=>[x<50?255:0,0,0,0]);assert.deepEqual(transparent.snap({x:55,y:40}),{x:55,y:40});
  const f=field(100,100,x=>x<50?[0,0,0,255]:[255,255,255,255]);
  assert.deepEqual(f.snap({x:80,y:40}),{x:80,y:40}); assert(f.trace({x:80,y:10},{x:80,y:90}).every(p=>p.x>=68));
});
test('alpha boundaries are edges, long traces are bounded pieces, invalid input is rejected',()=>{
  const f=field(400,40,x=>[0,0,0,x<200?0:255]); assert(Math.abs(f.snap({x:204,y:20}).x-200)<.01);
  const p=f.trace({x:10,y:20},{x:390,y:20}); assert(p.length>1&&p.length<1000);
  assert.throws(()=>f.snap({x:NaN,y:0}));assert.throws(()=>new MagneticLasso(2000,2000,new Uint8ClampedArray(0)));
});
