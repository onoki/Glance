import { openExternalTarget } from './externalLinks.js';

// Capture the destination before editor focus expands presentation-only URLs.
// Every anchor's native default is cancelled, including plain editable clicks,
// middle-clicks and keyboard activation; Glance must never become a browser.
export function installExternalLinkActivation({ root = document, open = openExternalTarget,
  report = error => window.alert(error?.message || 'Could not open the link.') } = {}) {
  let pressed = null;
  const inspect = event => {
    const anchor = event.target?.closest?.('a[href]');
    if (!anchor) return null;
    const editor = anchor.closest('.ProseMirror');
    const editable = editor?.getAttribute('contenteditable') === 'true';
    const raw = anchor.getAttribute('href');
    let href = raw;
    // App-owned documents, such as the bundled font license, use relative URLs.
    if (!editor) {
      try { href = new URL(raw, root.baseURI).href; } catch { /* validation reports the invalid target */ }
    }
    return { anchor, editor, href, active: root.activeElement,
      open: !editable || event.ctrlKey || event.metaKey || event.button === 1 };
  };
  const cancel = event => { event.preventDefault(); event.stopPropagation(); };
  const activate = item => {
    try {
      // Invoke synchronously while browser user activation is still available.
      const opened = open(item.href);
      void Promise.resolve(opened).then(() => {
        if (item.editor && item.editor.isConnected !== false && root.activeElement === item.active) {
          item.editor.focus({ preventScroll: true });
        }
      }).catch(report);
    } catch (error) { report(error); }
  };
  const down = event => {
    pressed = null;
    if (event.button !== 0 && event.button !== 1) return;
    const item = inspect(event);
    if (!item?.open) return;
    pressed = { ...item, button: event.button, x: event.clientX, y: event.clientY };
    cancel(event); // no focus, selection or URL expansion before the click
  };
  const click = event => {
    const captured = pressed?.button === event.button
      && Math.hypot((event.clientX ?? 0) - (pressed.x ?? 0), (event.clientY ?? 0) - (pressed.y ?? 0)) <= 5 ? pressed : null;
    pressed = null;
    const current = inspect(event);
    if (!current && !captured) return;
    event.preventDefault();
    const item = captured || current;
    if (!item.open) return; // plain text editing stays native, navigation does not
    event.stopPropagation();
    activate(item);
  };
  const key = event => {
    if (event.key !== 'Enter') return;
    const item = inspect(event);
    if (!item || event.target !== item.anchor) return;
    pressed = null;
    cancel(event);
    activate(item);
  };
  // Browser defaults can also replace the app when a URL/file is dropped on
  // its background. Editors retain their own text/image drop handling.
  const drop = event => { if (!event.target?.closest?.('.ProseMirror')) event.preventDefault(); };
  const handlers = [['mousedown', down], ['click', click], ['auxclick', click], ['keydown', key], ['drop', drop]];
  for (const [name, handler] of handlers) root.addEventListener(name, handler, true);
  return () => {
    pressed = null;
    for (const [name, handler] of handlers) root.removeEventListener(name, handler, true);
  };
}
