import { isAllowedExternalTarget } from './externalLinks.js';

export const CLIPBOARD_ATTRIBUTE = 'data-glance-tasks';
const escape = (value) => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
const nodes = new Set(['doc', 'paragraph', 'bulletList', 'listItem', 'text', 'hardBreak', 'image']);
const marks = new Set(['bold', 'italic', 'highlight', 'link']);

export function validateClipboardDoc(node, depth = 0) {
  if (!node || depth > 50 || !nodes.has(node.type)) throw new Error('Unsupported task clipboard content.');
  if (node.type === 'text' && typeof node.text !== 'string') throw new Error('Invalid clipboard text.');
  if (node.marks && (!Array.isArray(node.marks) || node.marks.some(m => !marks.has(m.type)))) throw new Error('Unsupported task formatting.');
  for (const mark of node.marks || []) {
    if (mark.type === 'link' && !isAllowedExternalTarget(mark.attrs?.href)) throw new Error('Unsupported clipboard link.');
  }
  if (node.type === 'image' && !/^\/attachments\//.test(node.attrs?.src || '')) throw new Error('Only Glance attachments can be pasted as task images.');
  if (node.content !== undefined && !Array.isArray(node.content)) throw new Error('Invalid clipboard structure.');
  for (const child of node.content || []) validateClipboardDoc(child, depth + 1);
}

export function taskDocHtml(node) {
  if (node.type === 'text') {
    let html = escape(node.text);
    for (const mark of node.marks || []) {
      if (mark.type === 'bold') html = `<strong>${html}</strong>`;
      if (mark.type === 'italic') html = `<em>${html}</em>`;
      if (mark.type === 'link') html = `<a href="${escape(mark.attrs.href)}">${html}</a>`;
      if (mark.type === 'highlight') html = `<mark>${html}</mark>`;
    }
    return html;
  }
  if (node.type === 'hardBreak') return '<br>';
  if (node.type === 'image') return `<img src="${escape(node.attrs.src)}" alt="${escape(node.attrs.alt || 'Attachment')}">`;
  const inner = (node.content || []).map(taskDocHtml).join('');
  const tag = { paragraph: 'p', bulletList: 'ul', listItem: 'li' }[node.type];
  return tag ? `<${tag}>${inner}</${tag}>` : inner;
}

export function taskDocText(node, depth = 0) {
  if (node.type === 'text') return node.text;
  if (node.type === 'hardBreak') return '\n';
  if (node.type === 'image') return '[Attachment]';
  const inner = (node.content || []).map(child => taskDocText(child, depth + (node.type === 'bulletList' ? 1 : 0))).join('');
  if (node.type === 'listItem') return `${'  '.repeat(Math.max(0, depth - 1))}• ${inner.trimEnd()}\n`;
  return inner + (node.type === 'paragraph' ? '\n' : '');
}

export function encodeTaskClipboard(tasks, scope) {
  const data = JSON.parse(JSON.stringify({ format: 'glance-tasks', version: 1, scope, tasks: tasks.map(({ title, content }) => ({ title, content })) }));
  const normalizeImages = node => {
    if (node.type === 'image' && /^https?:/.test(node.attrs?.src || '')) {
      const url = new URL(node.attrs.src);
      if (['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)) node.attrs.src = url.pathname;
    }
    for (const child of node.content || []) normalizeImages(child);
  };
  for (const task of data.tasks) { normalizeImages(task.title); normalizeImages(task.content); }
  for (const task of data.tasks) { validateClipboardDoc(task.title); validateClipboardDoc(task.content); }
  const json = JSON.stringify(data);
  if (json.length > 5_000_000 || !tasks.length || tasks.length > 200) throw new Error('Select between 1 and 200 tasks, up to 5 MB.');
  return {
    html: `<div ${CLIPBOARD_ATTRIBUTE}="${encodeURIComponent(json)}">${data.tasks.map(t => `<section>${taskDocHtml(t.title)}${taskDocHtml(t.content)}</section>`).join('')}</div>`,
    text: data.tasks.map(t => `${taskDocText(t.title).trimEnd()}\n${taskDocText(t.content).trimEnd()}`.trimEnd()).join('\n\n')
  };
}

export function decodeTaskClipboard(html) {
  if (!html?.includes(CLIPBOARD_ATTRIBUTE)) return null;
  const match = html.match(/data-glance-tasks=["']([^"']+)["']/);
  if (!match) return null;
  if (match[1].length > 15_000_000) throw new Error('Invalid task clipboard.');
  const data = JSON.parse(decodeURIComponent(match[1]));
  if (data.format !== 'glance-tasks' || data.version !== 1 || !Array.isArray(data.tasks) || !data.tasks.length || data.tasks.length > 200) throw new Error('Unsupported task clipboard version.');
  for (const task of data.tasks) {
    if (task.title?.type !== 'doc' || task.content?.type !== 'doc') throw new Error('Invalid task clipboard document.');
    validateClipboardDoc(task.title); validateClipboardDoc(task.content);
  }
  return data;
}

export const clipboardHasImages = (data) => JSON.stringify(data.tasks).includes('"type":"image"');
