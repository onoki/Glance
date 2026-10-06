import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { compileScript, parse } from '@vue/compiler-sfc';
import { createRenderer, getCurrentInstance, h, nextTick, ref } from 'vue';

const node = type => ({ type, children: [], parent: null });
const renderer = createRenderer({
  createElement: node, createText: () => node('text'), createComment: () => node('comment'),
  setText() {}, setElementText() {}, patchProp() {},
  parentNode: child => child.parent,
  nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] || null,
  insert(child, parent) { child.parent = parent; parent.children.push(child); },
  remove(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); }
});
const component = async name => {
  const url = new URL(`../src/components/views/${name}.vue`, import.meta.url);
  const { descriptor } = parse(await readFile(url, 'utf8'));
  let script = compileScript(descriptor, { id: name }).content;
  script = script.replace(/import (\w+) from "[^"\n]+\.vue";/g, 'const $1 = {};');
  script = script.replace(/from "([^"]+)"/g, (_, specifier) => `from ${JSON.stringify(specifier.startsWith('.') ? new URL(specifier, url).href : import.meta.resolve(specifier))}`);
  return (await import(`data:text/javascript;base64,${Buffer.from(script).toString('base64')}`)).default;
};
const storage = new Map();
globalThis.localStorage = { getItem: key => storage.get(key) || null, setItem: (key, value) => storage.set(key, value) };
globalThis.sessionStorage = globalThis.localStorage;
const noop = () => {};
try {
  const History = await component('HistoryView');
  let state;
  History.render = () => { state = getCurrentInstance().setupState; return h('history'); };
  const mountHistory = () => {
    const app = renderer.createApp({ render: () => h(History, {
      historyBars: [{count:1}], historyScale: [], historySeries: [], historyGroups: [],
      onMoveCompletedToHistory: noop, onComplete: noop, onDelete: noop, noop, noopAsync: noop,
      onStatusMarkers: noop, onLoadSendEvents: noop, onDismissSendMarker: noop
    }) });
    app.mount(node('root')); return app;
  };
  let history = mountHistory();
  assert.equal(state.activityExpanded, true, 'first visit defaults visible');
  state.rememberActivity({ currentTarget: { open: false } });
  history.unmount();
  history = mountHistory();
  assert.equal(state.activityExpanded, false, 'choice survives view/app remount');
  state.rememberActivity({ currentTarget: { open: true } });
  history.unmount();
  history = mountHistory();
  assert.equal(state.activityExpanded, true);
  history.unmount();
  localStorage.getItem = () => { throw new Error('storage blocked'); };
  localStorage.setItem = () => { throw new Error('storage blocked'); };
  history = mountHistory();
  assert.equal(state.activityExpanded, true);
  assert.doesNotThrow(() => state.rememberActivity({ currentTarget: { open: false } }));
  history.unmount();
  localStorage.getItem = key => storage.get(key) || null;
  localStorage.setItem = (key, value) => storage.set(key, value);

  const Dashboard = await component('DashboardView');
  storage.set('glance:column-widths', JSON.stringify({ new:260, notes:320 }));
  const maximized = ref(null);
  const categories = ref([{id:'notes', label:'Notes', tasks:[{id:'note'}]}]);
  const columns = {scrollLeft:160};
  Dashboard.render = () => {
    state = getCurrentInstance().setupState;
    state.columnsRef = columns;
    return h('dashboard');
  };
  const mountDashboard = () => {
    const app = renderer.createApp({ render: () => h(Dashboard, {
    newTasks: [], mainCategories:categories.value, maximizedCategoryId:maximized.value, isDashboardDragging:false,
    getTaskItemBindings:noop, groupTasksByWeekday:noop, isThisWeekCategory:category=>category.label==='This week',
    onToggleMaximize:id=>{maximized.value=maximized.value===id ? null : id;},
    onMoveNewToMain:noop, onDropOnCategory:noop, onPointerDown:noop, onPointerMove:noop, onPointerUp:noop,
    onCreateNewTask:noop, onCreateDayTask:noop, onDropOnWeekday:noop
  }) });
    app.mount(node('root'));
    return app;
  };
  let dashboard = mountDashboard();
  for (const id of ['new','notes']) {
    columns.scrollLeft=160;
    maximized.value=id; await nextTick(); await nextTick();
    assert.equal(columns.scrollLeft,0,'maximized column starts at the window edge');
    assert.equal(state.getColumnStyle('new').width,'260px');
    assert.equal(state.getColumnStyle('notes').width,'320px');
    maximized.value=null; await nextTick(); await nextTick();
    assert.equal(columns.scrollLeft,160,'restore returns to the original horizontal viewport');
  }
  maximized.value='notes'; await nextTick(); await nextTick();
  dashboard.unmount();
  dashboard = mountDashboard();
  maximized.value=null; await nextTick(); await nextTick();
  assert.equal(columns.scrollLeft,160,'restore retains the viewport after leaving Dashboard and returning');
  maximized.value='notes'; await nextTick();
  categories.value=[]; await nextTick(); await nextTick();
  assert.equal(maximized.value,null,'a disappearing maximized category restores other columns');
  dashboard.unmount();
  maximized.value='notes';
  dashboard=mountDashboard();
  await nextTick(); await nextTick();
  assert.equal(maximized.value,null,'returning after the category disappeared elsewhere restores immediately');
  dashboard.unmount();
} finally { delete globalThis.localStorage; delete globalThis.sessionStorage; }
console.log('History visibility persists and column maximization preserves normal widths/viewport');
