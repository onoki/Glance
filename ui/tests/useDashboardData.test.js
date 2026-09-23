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
const removedRows=new Map();
globalThis.fetch = async (path,request) => {
  if(path==='/api/dashboard') return {ok:true,json:async()=>({newTasks:structuredClone(rows.filter(row=>row.page==='dashboard:new')),mainTasks:structuredClone(rows.filter(row=>row.page==='dashboard:main'))})};
  if(path.endsWith('/restore')) {
    const id=path.split('/')[3];const restored=removedRows.get(id);
    assert.ok(restored); restored.updatedAt++; rows.push(restored); removedRows.delete(id);
    return {ok:true,json:async()=>({updatedAt:restored.updatedAt})};
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
} finally {globalThis.fetch=originalFetch;}
