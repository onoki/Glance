import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { compileScript, parse } from '@vue/compiler-sfc';
import { createRenderer, getCurrentInstance, h, nextTick, ref } from 'vue';
import { categoryWeekGroups, deriveCategories, isPlanningWeekCategory, nextWeekDays } from '../src/utils/categoryUtils.js';
import { useDashboardDrag } from '../src/composables/useDashboardDrag.js';
import { TASK_DRAG_TYPE } from '../src/utils/taskDrag.js';

const node = type => ({ type, children: [], parent: null });
const renderer = createRenderer({
  createElement: node, createText: () => node('text'), createComment: () => node('comment'),
  setText() {}, setElementText() {}, patchProp(child,key,previous,value) { (child.props ||= {})[key]=value; },
  parentNode: child => child.parent,
  nextSibling: child => child.parent?.children[child.parent.children.indexOf(child) + 1] || null,
  insert(child, parent) { child.parent = parent; parent.children.push(child); },
  remove(child) { if (child.parent) child.parent.children.splice(child.parent.children.indexOf(child), 1); }
});
const component = async (name, inlineTemplate = false) => {
  const url = new URL(`../src/components/views/${name}.vue`, import.meta.url);
  const { descriptor } = parse(await readFile(url, 'utf8'));
  let script = compileScript(descriptor, { id: name, inlineTemplate }).content;
  script = script.replace(/import (\w+) from "[^"\n]+\.vue";/g, inlineTemplate ? 'const $1 = {render:()=>null};' : 'const $1 = {};');
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
      onLoadSendEvents: noop, onDismissSendMarker: noop
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
  const dayRequests = [];
  Dashboard.render = () => {
    state = getCurrentInstance().setupState;
    state.columnsRef = columns;
    return h('dashboard');
  };
  const mountDashboard = () => {
    const app = renderer.createApp({ render: () => h(Dashboard, {
    newTasks: [], mainCategories:categories.value, maximizedCategoryId:maximized.value, isDashboardDragging:false,
    getTaskItemBindings:noop, categoryWeekGroups:noop, isPlanningWeekCategory:category=>['This week','Next week'].includes(category.label),
    onToggleMaximize:id=>{maximized.value=maximized.value===id ? null : id;},
    onMoveNewToMain:noop, onDropOnCategory:noop, onPointerDown:noop, onPointerMove:noop, onPointerUp:noop,
    onCreateNewTask:noop, onCreateDayTask:date=>dayRequests.push(date), onDropOnWeekday:noop
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
  categories.value=[{id:'week-2026-10-05',label:'This week',tasks:[]},{id:'week-2026-10-12',label:'Next week',tasks:[]}];
  await nextTick();
  await state.appendTask([], 'week-2026-10-05');
  assert.ok(dayRequests.at(-1) instanceof Date, 'empty This week tail still adds today, not the past Monday');
  await state.appendTask([], 'week-2026-10-12');
  assert.equal(dayRequests.at(-1),'2026-10-12','empty Next week tail adds on its Monday');
  dashboard.unmount();

  // Render the actual template: composable state must reach an empty day's line.
  const RenderedDashboard = await component('DashboardView', true);
  const root = node('root');
  const source = {id:'drag-source',page:'dashboard:new'};
  const planningCategories = ref(deriveCategories([]));
  const moves = [];
  const drag = useDashboardDrag({findTaskById:id=>id===source.id?source:null,getCategoryTasks:()=>[],applyTaskMove:(...args)=>moves.push(args)});
  const rendered = renderer.createApp({render:()=>h(RenderedDashboard,{
    newTasks:[],mainCategories:planningCategories.value,isDashboardDragging:false,
    getTaskItemBindings:noop,categoryWeekGroups,isPlanningWeekCategory,
    onMoveNewToMain:noop,onToggleMaximize:noop,onDropOnCategory:noop,
    onPointerDown:noop,onPointerMove:noop,onPointerUp:noop,onCreateNewTask:noop,onCreateDayTask:noop,
    dragOverDay:drag.dragOverDay.value,onDragOverWeekday:drag.setWeekdayDragOver,
    onDragLeaveWeekday:drag.clearWeekdayDragOver,onDropOnWeekday:drag.dropOnWeekdayFromView
  })});
  rendered.mount(root);
  const findClass = (name, current=root) => [ ...(current.props?.class?.split(' ').includes(name)?[current]:[]), ...current.children.flatMap(child=>findClass(name,child)) ];
  const column = findClass('dashboard-column').at(-1);
  const groups = findClass('weekday-group',column);
  const days = nextWeekDays();
  const headings = groups.map((group,index)=>({
    dataset:{weekdayDate:group.props['data-weekday-date']},
    querySelector:()=>({getBoundingClientRect:()=>({top:50+index*40})})
  }));
  column.querySelectorAll = () => headings;
  let stopped = false;
  const event = {dataTransfer:{types:[TASK_DRAG_TYPE]},currentTarget:column,
    target:{closest:selector=>selector==='.task-list'?{}:null},clientY:290,
    preventDefault(){},stopPropagation(){stopped=true;}};
  const hover = () => column.props.onDragoverCapture(event);
  const drop = () => column.props.onDropCapture(event);
  assert.equal(findClass('weekday-drop-indicator').length,0);
  hover();await nextTick();
  assert.equal(findClass('weekday-drop-indicator').length,0,'inactive drags cannot render a marker');
  drag.startDrag(source);
  hover();await nextTick();
  assert.equal(drag.dragOverDay.value,nextWeekDays().at(-1).dateKey);
  assert.equal(findClass('weekday-drop-indicator').length,1,'empty Sunday visibly offers a whole-task drop');
  assert.equal(findClass('weekday-drop-indicator',groups.at(-1)).length,1,'the line belongs to the targeted weekday');
  await drop();await nextTick();
  assert.equal(moves.at(-1)[3],nextWeekDays().at(-1).dateKey);
  assert.equal(findClass('weekday-drop-indicator').length,0,'the rendered line disappears after dropping');
  // Includes group margins, the list's flex gap and the next header's blank top.
  for (let day=0;day<6;day++) for (const offset of [20,28,32,36,39.75]) {
    event.clientY=50+day*40+offset;
    stopped=false;
    hover();await nextTick();
    assert.equal(stopped,true,'background hover cannot fall through to column defaults');
    assert.equal(drag.dragOverDay.value,days[day].dateKey,'all space before the next heading belongs to the preceding day');
    assert.equal(findClass('weekday-drop-indicator',groups[day]).length,1);
    stopped=false;
    await drop();await nextTick();
    assert.equal(stopped,true,'background drop cannot fall through and redate to Monday');
    assert.equal(moves.at(-1)[3],days[day].dateKey);
  }
  event.clientY=290;
  hover();await nextTick();
  assert.equal(drag.dragOverDay.value,days[6].dateKey,'Sunday starts at its heading text, not earlier whitespace');
  const otherColumn=findClass('dashboard-column').at(-2);
  otherColumn.querySelectorAll=()=>[{dataset:{weekdayDate:'2000-01-01'}}];
  otherColumn.props.onDragleave({currentTarget:otherColumn});await nextTick();
  assert.equal(drag.dragOverDay.value,days[6].dateKey,'a late leave from another column cannot clear the current marker');
  const originalElement = globalThis.Element;
  // The DOM-free renderer's comment-root task stubs have no CSS transitions.
  globalThis.Element = class {};
  try {
    const category = planningCategories.value.at(-1);
    category.tasks.push({id:'existing-saturday',scheduledDate:days[5].dateKey});
    await nextTick();
    event.clientY=289.75;
    hover();await nextTick();
    assert.equal(findClass('weekday-drop-indicator',groups[5]).length,1,'the same spacing remains a visible append target after Saturday gains a task');
    await drop();await nextTick();
    assert.equal(moves.at(-1)[3],days[5].dateKey);
    category.tasks.length=0;
    await nextTick();
  } finally {globalThis.Element=originalElement;}
  drag.endDrag();await nextTick();
  assert.equal(findClass('weekday-drop-indicator').length,0,'cancel/end removes the rendered line');
  const moveCount=moves.length;
  drag.startDrag(source);
  event.target={closest:selector=>selector==='.task-item'||selector==='.task-list'?{}:null};
  stopped=false;
  hover();await drop();await nextTick();
  assert.equal(stopped,false,'task rows retain their own before/after drop handling');
  assert.equal(moves.length,moveCount);
  event.target={closest:selector=>selector==='.task-list'?{}:null};
  event.dataTransfer.types=['text/plain'];
  hover();await drop();await nextTick();
  assert.equal(moves.length,moveCount,'text/image drags cannot be treated as tasks');
  drag.endDrag();
  rendered.unmount();
} finally { delete globalThis.localStorage; delete globalThis.sessionStorage; }
console.log('History visibility persists and column maximization preserves normal widths/viewport');
