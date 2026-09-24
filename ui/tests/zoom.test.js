import assert from 'node:assert/strict';
import { isCrispZoom, zoomStep } from '../src/utils/zoom.js';
for (const [scale, crisp] of [[1,150],[1.25,120],[1.5,100],[2,75]]) {
  assert.equal(isCrispZoom(crisp,scale),true);
  assert.equal(isCrispZoom(crisp+10,scale),false);
  let value=50; const seen=new Set([value]);
  while(value<400) { const next=zoomStep(value,1,scale); assert.ok(next>value && next-value<=10); seen.add(next); value=next; }
  assert.ok(seen.has(crisp));
  assert.equal(zoomStep(400,1,scale),400);
  assert.equal(zoomStep(50,-1,scale),50);
  while(value>50) { const next=zoomStep(value,-1,scale); assert.ok(next<value && value-next<=10); value=next; }
}
assert.equal(isCrispZoom(100,1),false);
assert.equal(isCrispZoom(200,1.5),true);
assert.equal(isCrispZoom(50,1.5),false,'half native resolution is not marked crisp');
console.log('Zoom: native-grid hints, intermediate steps, monitor scales and limits');
