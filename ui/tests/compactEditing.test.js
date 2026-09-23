import assert from 'node:assert/strict';
import { pixelCorrection } from '../src/utils/pixelText.js';
import { revealCaret } from '../src/utils/editorVisibility.js';
import { titleJoinPosition } from '../src/utils/taskUtils.js';
for (const scale of [1, 1.25, 1.5, 2]) {
  for (const coordinate of [0, 25, 57.66667, 103.125, -4.5]) {
    const correction = pixelCorrection(coordinate, scale);
    assert.ok(Math.abs(correction * scale) <= .500001);
    assert.ok(Math.abs((coordinate + correction) * scale - Math.round(coordinate * scale)) < .000001);
  }
}
assert.equal(titleJoinPosition({type:'doc',content:[{type:'paragraph',content:[{type:'text',text:'A😀',marks:[{type:'bold'}]},{type:'hardBreak'},{type:'text',text:'B'}]}]}), 6);
assert.equal(titleJoinPosition({type:'doc',content:[{type:'paragraph'}]}), 1);
const container = { scrollTop: 40, clientHeight: 200, getBoundingClientRect: () => ({top:10}), parentElement:null };
globalThis.getComputedStyle = () => ({overflowY:'auto'});
let caret = {top:190,bottom:202};
const editor = {view:{hasFocus:()=>true,dom:{parentElement:container},coordsAtPos:()=>caret},state:{selection:{head:1}}};
revealCaret(editor);
assert.equal(container.scrollTop,64, 'reserve room below caret');
caret={top:70,bottom:82}; revealCaret(editor);
assert.equal(container.scrollTop,64,'editing a visible earlier row does not jump to bottom');
caret={top:5,bottom:17}; revealCaret(editor);
assert.equal(container.scrollTop,55,'reveal caret above viewport');
editor.view.hasFocus=()=>false; caret={top:500,bottom:512}; revealCaret(editor);
assert.equal(container.scrollTop,55,'background editors cannot steal scroll');
delete globalThis.getComputedStyle;
console.log('Pixel alignment, merge boundary and caret scrolling passed');
