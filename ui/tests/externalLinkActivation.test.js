import assert from 'node:assert/strict';
import { installExternalLinkActivation } from '../src/utils/externalLinkActivation.js';

const handlers = new Map(), calls = [], errors = [];
const root = { baseURI: 'http://localhost:5588/', activeElement: {},
  addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener: name => handlers.delete(name) };
const editor = { getAttribute: () => 'true', isConnected: true,
  focus: () => { calls.push('focus'); root.activeElement = editor; } };
let href = 'https://example.com/first';
const anchor = { getAttribute: () => href, closest: () => editor };
const target = { closest: selector => selector === 'a[href]' ? anchor : editor };
const dispatch = (name, extra = {}) => {
  const event = { target, button: 0, clientX: 20, clientY: 20, preventDefault() { this.prevented = true; },
    stopPropagation() { this.stopped = true; }, ...extra };
  handlers.get(name)(event); return event;
};
const stop = installExternalLinkActivation({ root, open: value => { calls.push(value); }, report: error => errors.push(error.message) });
assert.equal(dispatch('mousedown').prevented, undefined, 'plain editable click retains caret placement');
assert.equal(dispatch('click').prevented, true, 'but never navigates internally');
assert.deepEqual(calls, []);
dispatch('mousedown', { ctrlKey: true });
href = 'https://example.com/now-under-pointer';
dispatch('click', { ctrlKey: true });
assert.deepEqual(calls, ['https://example.com/first'], 'destination is captured before focus/reflow');
await Promise.resolve();
assert.equal(calls.at(-1), 'focus', 'opening precedes editor focus');
calls.length = 0;
dispatch('mousedown', { button: 1 }); dispatch('auxclick', { button: 1 });
await Promise.resolve();
assert.equal(calls[0], href, 'middle-click goes through the external bridge');
calls.length = 0;
editor.getAttribute = () => 'false';
dispatch('click'); await Promise.resolve();
assert.equal(calls[0], href, 'readonly links open without modifiers');
calls.length = 0;
dispatch('keydown', { key: 'Enter', target: { closest: () => anchor } });
assert.deepEqual(calls, [], 'Enter in surrounding text does not activate a link');
anchor.getAttribute = () => '/assets/fonts/IBMPlexSans-OFL.txt';
anchor.closest = selector => selector === 'a[href]' ? anchor : null;
dispatch('keydown', { key: 'Enter', target: anchor });
assert.equal(calls[0], 'http://localhost:5588/assets/fonts/IBMPlexSans-OFL.txt');
calls.length = 0;
anchor.closest = () => editor;
dispatch('mousedown');
dispatch('click', { clientX: 70, target: { closest: () => null } });
assert.deepEqual(calls, [], 'dragging away cannot activate a stale captured destination');
assert.equal(dispatch('drop', { target: { closest: () => null } }).prevented, true);
assert.equal(dispatch('drop').prevented, undefined, 'editor drop handling is retained');
stop(); assert.equal(handlers.size, 0);
const stopFailed = installExternalLinkActivation({ root, open: () => Promise.reject(new Error('Blocked')), report: error => errors.push(error.message) });
dispatch('click'); await Promise.resolve(); await Promise.resolve();
assert.deepEqual(errors, ['Blocked']); stopFailed();
console.log('External link activation: captured URLs, editing, middle/keyboard clicks, drop guard and cleanup passed');
