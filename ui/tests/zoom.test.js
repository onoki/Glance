import assert from 'node:assert/strict';
import { BASE_FONT_SIZE, isPixelAlignedZoom, zoomStep } from '../src/utils/zoom.js';
assert.equal(BASE_FONT_SIZE, 7, 'the selected IBM Plex Sans base size');
for (const [scale, aligned] of [[1,100],[1.25,80],[1.5,114],[1.75,98],[2,50]]) {
  assert.equal(isPixelAlignedZoom(aligned,scale),true);
  assert.equal(isPixelAlignedZoom(aligned+1,scale),false);
  let value=50; const seen=new Set([value]);
  while(value<400) { const next=zoomStep(value,1,scale); assert.ok(next>value && next-value<=10); seen.add(next); value=next; }
  assert.ok(seen.has(aligned));
  assert.equal(zoomStep(400,1,scale),400);
  assert.equal(zoomStep(50,-1,scale),50);
  while(value>50) { const next=zoomStep(value,-1,scale); assert.ok(next<value && value-next<=10); value=next; }
}
assert.equal(isPixelAlignedZoom(100,1.5),false,'10.5 device pixels is not a whole-pixel size');
assert.equal(isPixelAlignedZoom(200,1.5),true,'outline text has no obsolete 12-pixel bitmap minimum');
assert.equal(isPixelAlignedZoom(0,1.5),false);
assert.equal(isPixelAlignedZoom(100,NaN),false);
assert.equal(isPixelAlignedZoom(100,Infinity),false);
assert.equal(isPixelAlignedZoom(100,-1),false);
assert.equal(zoomStep(100,1,NaN),110,'an unavailable monitor scale retains practical steps');
console.log('Zoom: outline-font alignment hints, intermediate steps, monitor scales and limits');
