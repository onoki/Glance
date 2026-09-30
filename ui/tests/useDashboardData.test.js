import assert from "node:assert/strict";
import { shouldDeleteEmptyOnComplete } from "../src/utils/taskCompletionUtils.js";

const emptyDoc = { type: "doc", content: [{ type: "paragraph" }] };
const textDoc = { type: "doc", content: [{ type: "paragraph", content: [{ type: "text", text: "hi" }] }] };

assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: null, title: emptyDoc, content: emptyDoc }),
  true
);
assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: Date.now(), title: emptyDoc, content: emptyDoc }),
  false
);
assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: null, title: textDoc, content: emptyDoc }),
  false
);
assert.equal(
  shouldDeleteEmptyOnComplete({ completedAt: null, title: emptyDoc, content: textDoc }),
  false
);

const { useDashboardData } = await import('../src/composables/useDashboardData.js');
const doc = text => ({type:'doc',content:[{type:'paragraph',content:[{type:'text',text}]}]});
const rows = ['First task','Second task'].map((text,index)=>({id:String(index),page:'dashboard:new',title:doc(text),content:emptyDoc,position:index,updatedAt:1,completedAt:null}));
const originalFetch = globalThis.fetch;
const originalDocument = globalThis.document;
const originalFrame = globalThis.requestAnimationFrame;
globalThis.document = { querySelector: () => null, querySelectorAll: () => [] };
globalThis.requestAnimationFrame = callback => callback();
const removedRows=new Map();
globalThis.fetch = async (path,request) => {
  if(path==='/api/dashboard') return {ok:true,json:async()=>({newTasks:structuredClone(rows.filter(row=>row.page==='dashboard:new')),mainTasks:structuredClone(rows.filter(row=>row.page==='dashboard:main'))})};
  if(path.endsWith('/restore')) {
    const id=path.split('/')[3];const restored=removedRows.get(id);
    assert.ok(restored); restored.updatedAt++; rows.push(restored); removedRows.delete(id);
    return {ok:true,json:async()=>({updatedAt:restored.updatedAt})};
  }
  if (path === '/api/tasks' && request.method === 'POST') {
    const payload = JSON.parse(request.body);
    const id = `created-${rows.length}`;
    rows.push({ ...payload, id, updatedAt: 1, completedAt: null });
    return {ok:true,json:async()=>({taskId:id,updatedAt:1})};
  }
  const row=rows.find(row=>path.split('?')[0]===`/api/tasks/${row.id}`);
  assert.ok(row, `unexpected request: ${path}`);
  if(request.method==='DELETE') { removedRows.set(row.id,structuredClone(row)); rows.splice(rows.indexOf(row),1); return {ok:true,json:async()=>({ok:true})}; }
  const payload=JSON.parse(request.body);
  assert.equal(payload.baseUpdatedAt,row.updatedAt,'merge must use current revision');
  Object.assign(row,payload,{updatedAt:row.updatedAt+1});
  return {ok:true,json:async()=>({updatedAt:row.updatedAt})};
};
try {
  const dashboard=useDashboardData();
  await dashboard.loadDashboard();
  await dashboard.mergeTaskToPrevious(dashboard.newTasks.value[1]);
  assert.deepEqual(dashboard.focusTaskId.value,{taskId:'0',selection:{from:11,to:11}});
  assert.equal(rows[0].title.content[0].content.map(node=>node.text).join(''),'First taskSecond task');
  rows.push({id:'empty',page:'dashboard:new',title:emptyDoc,content:emptyDoc,position:2,updatedAt:1,completedAt:null},
    {id:'next',page:'dashboard:new',title:doc('Next'),content:emptyDoc,position:3,updatedAt:1,completedAt:null});
  await dashboard.loadDashboard();
  await dashboard.deleteTask(dashboard.newTasks.value.find(task=>task.id==='empty'),{direction:'forward'});
  assert.deepEqual(dashboard.focusTaskId.value,{taskId:'next',selection:{from:1,to:1}});


  // Server ordering interleaves categories: Backspace must stay in its column.
  rows.push(
    {id:'notes-first',page:'dashboard:main',title:doc('Notes first'),content:emptyDoc,position:1,updatedAt:1,completedAt:null,recurrence:{type:'notes'}},
    {id:'other-column',page:'dashboard:main',title:doc('Other column'),content:emptyDoc,position:2,updatedAt:1,completedAt:null},
    {id:'notes-second',page:'dashboard:main',title:doc('Notes second'),content:emptyDoc,position:3,updatedAt:1,completedAt:null,recurrence:{type:'notes'}});
  await dashboard.loadDashboard();
  const beforeBoundary=structuredClone(rows);
  assert.equal(await dashboard.mergeTaskToPrevious(dashboard.mainTasks.value.find(task=>task.id==='other-column')),false);
  assert.deepEqual(rows,beforeBoundary,'first task in a column cannot merge into another column');
  await dashboard.mergeTaskToPrevious(dashboard.mainTasks.value.find(task=>task.id==='notes-second'));
  assert.equal(rows.find(task=>task.id==='notes-first').title.content[0].content.map(node=>node.text).join(''),'Notes firstNotes second');
  assert.equal(rows.find(task=>task.id==='other-column').title.content[0].content[0].text,'Other column');
  assert.equal(dashboard.focusTaskId.value.taskId,'notes-first');

  const lines={type:'doc',content:[{type:'bulletList',content:[{type:'listItem',content:[{type:'paragraph',content:[{type:'text',text:'Tail'}]}]}]}]};
  rows.find(task=>task.id==='notes-first').content=structuredClone(lines);
  rows.push({id:'notes-third',page:'dashboard:main',title:doc('Next title'),content:structuredClone(lines),position:4,updatedAt:1,completedAt:null,recurrence:{type:'notes'}});
  await dashboard.loadDashboard();
  await dashboard.mergeTaskWithNext(dashboard.mainTasks.value.find(task=>task.id==='notes-first'));
  assert.deepEqual(dashboard.focusContentTarget.value,{taskId:'notes-first',selection:{from:7,to:7}});
  assert.equal(rows.find(task=>task.id==='notes-first').content.content[0].content[0].content[0].content.map(node=>node.text).join(''),'TailNext title');
  assert.equal(rows.some(task=>task.id==='notes-third'),false);
  await dashboard.undo();
  assert.equal(rows.find(task=>task.id==='notes-third').page,'dashboard:main');
  assert.deepEqual(rows.find(task=>task.id==='notes-first').content,lines);
  await dashboard.redo();
  assert.equal(rows.some(task=>task.id==='notes-third'),false);
  const { currentWeekDays } = await import('../src/utils/categoryUtils.js');
  const sunday = currentWeekDays().at(-1).dateKey;
  const created = await dashboard.createTask('dashboard:main', doc('Sunday task'), emptyDoc, 10, `day-${sunday}`);
  assert.equal(rows.find(row => row.id === created).scheduledDate, sunday);
  const movable = dashboard.newTasks.value[0];
  const original = structuredClone(rows.find(row => row.id === movable.id));
  await dashboard.setTaskCategory(movable, `day-${sunday}`);
  assert.equal(rows.find(row => row.id === movable.id).scheduledDate, sunday);
  assert.equal(rows.find(row => row.id === movable.id).page, 'dashboard:main');
  await dashboard.undo();
  assert.equal(rows.find(row => row.id === movable.id).page, original.page);
  await dashboard.redo();
  assert.equal(rows.find(row => row.id === movable.id).scheduledDate, sunday);
  await assert.rejects(dashboard.setTaskCategory(movable, 'day-2000-01-01'));
  const current = dashboard.mainTasks.value.find(row => row.id === movable.id);
  await dashboard.applyTaskMove(current, 'new', 20);
  assert.equal(rows.find(row => row.id === movable.id).scheduledDate, null);
  await dashboard.undo();
  assert.equal(rows.find(row => row.id === movable.id).scheduledDate, sunday);
  await dashboard.redo();
  assert.equal(rows.find(row => row.id === movable.id).page, 'dashboard:new');
  const newTask = dashboard.newTasks.value.find(row => row.id === movable.id);
  await dashboard.applyTaskMove(newTask, `week-${sunday}`, 21, sunday);
  assert.equal(rows.find(row => row.id === movable.id).page, 'dashboard:main');
  assert.equal(rows.find(row => row.id === movable.id).scheduledDate, sunday);

  const { formatDateKey, getWeekStart } = await import('../src/utils/dateUtils.js');
  for (const offset of [1, 3, -1, 8]) {
    const date = new Date(); date.setDate(date.getDate() + offset);
    const dateKey = formatDateKey(date);
    const category = `week-${formatDateKey(getWeekStart(date))}`;
    const sourceId = await dashboard.createTask('dashboard:main', doc('Source'), emptyDoc, 50 + offset, category);
    const source = rows.find(row => row.id === sourceId);
    source.scheduledDate = dateKey;
    await dashboard.loadDashboard();
    const childId = await dashboard.createTaskBelow(source, category);
    assert.equal(rows.find(row => row.id === childId).scheduledDate, dateKey, 'Enter inherits the source date');
    assert.equal(dashboard.focusTaskId.value, childId);
    await dashboard.undo();
    assert.equal(rows.some(row => row.id === childId), false);
    await dashboard.redo();
    assert.equal(rows.find(row => row.id === childId).scheduledDate, dateKey, 'Redo retains the inherited date');
    const independentId = await dashboard.createTaskBelow(source, category, null, {inheritScheduledDate:false});
    const expectedDefault = category === `week-${formatDateKey(getWeekStart(new Date()))}` ? formatDateKey(new Date()) : formatDateKey(getWeekStart(date));
    assert.equal(rows.find(row => row.id === independentId).scheduledDate, expectedDefault, 'column tail retains its independent date default');
    await dashboard.splitTitleToNewTask(source, category, { newTitle: doc('Split'), newContent: emptyDoc });
    assert.equal(rows.find(row => row.id === dashboard.focusTaskId.value).scheduledDate, dateKey);
    await dashboard.splitSubcontentToNewTask(source, {categoryId:category,title:doc('Outdent'),content:emptyDoc});
    assert.equal(rows.find(row => row.id === dashboard.focusTaskId.value).scheduledDate, dateKey);
  }

} finally {
  globalThis.fetch = originalFetch;
  globalThis.document = originalDocument;
  globalThis.requestAnimationFrame = originalFrame;
}
