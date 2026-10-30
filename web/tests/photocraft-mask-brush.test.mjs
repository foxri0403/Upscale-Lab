import test from "node:test";
import assert from "node:assert/strict";
import { PhotoCraftBrushCoverage, segmentDistance, softCoverage, combineCoverage } from "../src/vendor/photocraft-mask-brush.ts";
import { MaskStrokePainter, paintMaskStroke } from "../src/subject-mask.ts";

const flatten = brush => {
  const result = new Map();
  for (const tile of brush.tiles.values()) for (let y = 0; y < tile.height; y++) for (let x = 0; x < tile.width; x++) {
    const coverage = tile.coverage[y * tile.width + x];
    if (coverage) result.set(`${tile.x+x},${tile.y+y}`,coverage);
  }
  return result;
};

test("PhotoCraft coverage has a solid core, smooth falloff and antialiased hard rim", () => {
  assert.equal(segmentDistance({x:4,y:3},{x:0,y:0},{x:8,y:0}),3);
  assert.equal(segmentDistance({x:10,y:0},{x:0,y:0},{x:8,y:0}),2);
  assert.equal(segmentDistance({x:3,y:4},{x:0,y:0},{x:0,y:0}),5);
  assert.equal(softCoverage(0,10,10),1);
  assert.equal(softCoverage(10,10,10),.5);
  assert.equal(softCoverage(11,10,10),0);
  assert.equal(softCoverage(5.25,10,0),.5);
  assert(softCoverage(8,10,0)>0 && softCoverage(8,10,0)<.5);
});

test("sparse brush tiles clip safely at canvas boundaries and do not allocate a full large canvas", () => {
  const brush=new PhotoCraftBrushCoverage(8000,8000,12,30);
  brush.append({x:0,y:0}); brush.append({x:20,y:20});
  assert.equal(brush.tiles.size,1);
  assert(flatten(brush).size < 2000);
  const edge=new PhotoCraftBrushCoverage(513,257,12,100);
  edge.append({x:513,y:257});
  for(const tile of edge.tiles.values()) assert(tile.x+tile.width<=513 && tile.y+tile.height<=257);
});

test("collinear pointer event density and repeated dabs do not darken one soft stroke", () => {
  const sparse=new PhotoCraftBrushCoverage(600,400,40,10),dense=new PhotoCraftBrushCoverage(600,400,40,10);
  sparse.append({x:50,y:100}); sparse.append({x:550,y:100});
  for(let x=50;x<=550;x+=5) dense.append({x,y:100});
  dense.append({x:550,y:100});
  assert.deepEqual(flatten(sparse),flatten(dense));
});

function context(width=64,height=64,base=200){
  const data=new Uint8ClampedArray(width*height*4);
  for(let i=0;i<data.length;i+=4){data[i]=data[i+1]=data[i+2]=base;data[i+3]=255;}
  return {canvas:{width,height},data,
    getImageData(x,y,w,h){const pixels=new Uint8ClampedArray(w*h*4);for(let row=0;row<h;row++)pixels.set(data.subarray(((y+row)*width+x)*4,((y+row)*width+x+w)*4),row*w*4);return {data:pixels,width:w,height:h};},
    putImageData(image,x,y){for(let row=0;row<image.height;row++)data.set(image.data.subarray(row*image.width*4,(row+1)*image.width*4),((y+row)*width+x)*4);}};
}

test("incremental drawing and source-pixel replay produce the same opaque mask without overlapping erase buildup", () => {
  const stroke={mode:"erase",size:30,hardness:0,points:[{x:15,y:20},{x:30,y:20},{x:45,y:20}]};
  const live=context(),replay=context(); const painter=new MaskStrokePainter(live,stroke);
  for(const p of stroke.points)painter.append(p);
  paintMaskStroke(replay,stroke);assert.deepEqual(live.data,replay.data);
  painter.append(stroke.points.at(-1));assert.deepEqual(live.data,replay.data);
  assert.equal(live.data[(20*64+30)*4],0);
  assert(live.data[(30*64+30)*4]>0 && live.data[(30*64+30)*4]<200);
  assert.equal(live.data[(63*64+63)*4],200);
  for(let i=3;i<live.data.length;i+=4)assert.equal(live.data[i],255);
  assert.equal(combineCoverage(128,100,"restore"),128);
  assert.equal(combineCoverage(128,200,"restore"),200);
  assert.equal(combineCoverage(128,100,"erase"),28);
});

test("invalid sizes, coordinates and oversized work are rejected before drawing", () => {
  assert.throws(()=>new PhotoCraftBrushCoverage(0,1,2));
  assert.throws(()=>new PhotoCraftBrushCoverage(10,10,NaN));
  assert.throws(()=>new PhotoCraftBrushCoverage(10,10,5001));
  const brush=new PhotoCraftBrushCoverage(100,100,10);
  assert.throws(()=>brush.append({x:NaN,y:2}));
  assert.throws(()=>brush.append({x:101,y:2}));
});
