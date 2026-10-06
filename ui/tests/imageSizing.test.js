import assert from 'node:assert/strict';
import ResizableImage, { boundedImageWidth } from '../src/utils/resizableImage.js';

for (const natural of [1, 24, 60, 1200]) {
  assert.equal(boundedImageWidth(null, natural), natural);
  assert.equal(boundedImageWidth(natural * 10, natural), natural);
  assert.equal(boundedImageWidth(-100, natural), 1);
  assert.equal(boundedImageWidth(0, natural), 1);
  assert.equal(boundedImageWidth(1, natural), 1);
}
assert.equal(boundedImageWidth(800, 0), null, 'unknown dimensions must not stretch');

// Exercise the actual node view through load, saved widths, handle drags and
// replacement sources. Model only the DOM operations this view uses.
const makeElement = tag => {
  const listeners = new Map();
  const element = { tag, style: {}, children: [], naturalWidth: 0,
    classList: { toggle() {} },
    addEventListener: (type, handler) => listeners.set(type, handler),
    removeEventListener: type => listeners.delete(type),
    fire: (type, event = {}) => listeners.get(type)?.(event),
    appendChild: child => element.children.push(child),
    getAttribute: name => element[name],
    getBoundingClientRect: () => ({ width: parseFloat(element.style.width) || 24 }), listeners
  };
  return element;
};
const documentListeners = new Map();
globalThis.document = {
  createElement: makeElement,
  addEventListener: (type, handler) => documentListeners.set(type, handler),
  removeEventListener: type => documentListeners.delete(type)
};
const writes = [];
const chain = { focus: () => chain, setNodeSelection: () => chain, run() {} };
const editor = { state: { selection: {} }, chain: () => chain, on() {}, off() {},
  commands: { command: callback => callback({ tr: { setNodeMarkup: (_pos, _type, attrs) => writes.push(attrs) } }) }
};
try {
  let node = { type: { name: 'image' }, attrs: { src: '/small.png', width: 800 } };
  const view = ResizableImage.config.addNodeView()({ node, editor, getPos: () => 1 });
  const [image, handle] = view.dom.children;
  assert.equal(view.dom.style.width, '', 'wait for intrinsic dimensions');
  image.naturalWidth = 24;
  image.fire('load');
  assert.equal(view.dom.style.width, '24px', 'old oversized saved widths are bounded on load');
  handle.fire('pointerdown', { clientX: 0, preventDefault() {}, stopPropagation() {} });
  documentListeners.get('pointermove')({ clientX: 400 });
  assert.equal(writes.at(-1).width, 24, 'resize handle cannot enlarge a small image');
  documentListeners.get('pointermove')({ clientX: -20 });
  assert.equal(writes.at(-1).width, 4, 'small images can still shrink');
  documentListeners.get('pointerup')();
  assert.equal(documentListeners.size, 0);
  node = { ...node, attrs: { ...node.attrs, width: 12 } };
  assert.equal(view.update(node), true);
  assert.equal(view.dom.style.width, '12px', 'saved smaller dimensions survive updates');
  node = { ...node, attrs: { src: '/large.png', width: null } };
  view.update(node);
  image.naturalWidth = 1200;
  image.fire('load');
  assert.equal(view.dom.style.width, '1200px', 'CSS max-width additionally fits large images to the column');
  handle.fire('pointerdown', { clientX: 0, preventDefault() {}, stopPropagation() {} });
  view.destroy();
  assert.equal(documentListeners.size, 0, 'unmount during resizing removes pointer listeners');
  assert.equal(image.listeners.size, 0);
} finally { delete globalThis.document; }
console.log('Images preserve intrinsic upper bounds, smaller widths and resize cleanup');
