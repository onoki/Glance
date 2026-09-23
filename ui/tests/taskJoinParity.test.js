import assert from 'node:assert/strict';
import { ref } from 'vue';
import { useDashboardData } from '../src/composables/useDashboardData.js';
import { usePeopleTasks } from '../src/composables/usePeopleTasks.js';

const clone = value => JSON.parse(JSON.stringify(value));
const paragraph = text => (text ? {type:'paragraph',content:[{type:'text',text}]} : {type:'paragraph'});
const doc = text => ({type:'doc',content:[paragraph(text)]});
const item = text => ({type:'listItem',content:[paragraph(text)]});
const list = items => ({type:'bulletList',content:items});
const content = items => ({type:'doc',content:[list(items)]});

async function exercise(view, upperContent, lowerContent, forward) {
  const page = view === 'People' ? 'people:main' : 'dashboard:new';
  const rows = ['Upper','Lower'].map((text,index)=>({id:String(index),page,ownerPersonId:'person',title:doc(text),content:clone(index ? lowerContent : upperContent),position:index,updatedAt:1,completedAt:null}));
  const original = clone(rows);
  const deleted = new Map();
  const failures = [];
  const updateTask = async (id,payload) => {
    const row=rows.find(row=>row.id===id);
    assert.ok(row);
    assert.equal(payload.baseUpdatedAt,row.updatedAt,'current revision is used');
    Object.assign(row,clone(payload),{updatedAt:row.updatedAt+1});
    return {updatedAt:row.updatedAt};
  };
  const deleteTask = async id => {
    const index=rows.findIndex(row=>row.id===id);
    assert.ok(index>=0);
    deleted.set(id,clone(rows[index])); rows.splice(index,1);
    return {ok:true};
  };
  const restoreTask = async id => {
    const row=deleted.get(id); assert.ok(row);
    row.updatedAt++; rows.push(row); deleted.delete(id);
    return {updatedAt:row.updatedAt};
  };
  const priorFetch=globalThis.fetch;
  globalThis.fetch=async (path,request)=>{
    try {
      let result;
      if(path==='/api/dashboard') result={newTasks:clone(rows),mainTasks:[]};
      else {
        const id=path.split('/')[3].split('?')[0];
        if(path.endsWith('/restore')) result=await restoreTask(id);
        else if(request.method==='DELETE') result=await deleteTask(id);
        else if(request.method==='PUT') result=await updateTask(id,JSON.parse(request.body));
        else throw new Error(`Unexpected request ${request.method} ${path}`);
      }
      return {ok:true,json:async()=>result};
    } catch(error) { failures.push(error); throw error; }
  };
  try {
    const model=view==='People'
      ? usePeopleTasks({selectedPersonId:ref('person'),api:{fetchPersonTasks:async()=>({tasks:clone(rows)}),updateTask,deleteTask,restoreTask}})
      : useDashboardData();
    const load=view==='People' ? model.loadTasks : model.loadDashboard;
    const tasks=view==='People' ? model.tasks : model.newTasks;
    await load();
    assert.equal(await model.mergeTaskToPrevious(tasks.value[0]),false,'first task never merges backward');
    assert.equal(await model.mergeTaskWithNext(tasks.value[1]),false,'last task never merges forward');
    await (forward ? model.mergeTaskWithNext(tasks.value[0]) : model.mergeTaskToPrevious(tasks.value[1]));
    assert.equal(rows.length,1);
    assert.equal(rows[0].id,'0','upper task identity survives');
    const result={title:clone(rows[0].title),content:clone(rows[0].content),titleFocus:clone(model.focusTaskId.value),contentFocus:clone(model.focusContentTarget.value)};
    await model.undo();
    assert.deepEqual(rows.slice().sort((a,b)=>a.position-b.position).map(({id,title,content})=>({id,title,content})),original.map(({id,title,content})=>({id,title,content})),'Undo restores both original task IDs and documents');
    await model.redo();
    assert.equal(rows.length,1);
    assert.deepEqual(rows[0].title,result.title);
    assert.deepEqual(rows[0].content,result.content);
    assert.deepEqual(failures,[],'no swallowed API failures');
    return result;
  } finally {globalThis.fetch=priorFetch;}
}

const nested=item('Parent');
nested.content.push(list([item('Nested tail')]));
const cases=[
  [doc(''),doc('')],
  [doc(''),content([item('Lower child')])],
  [content([item('Upper tail')]),content([item('Lower child')])],
  [content([nested]),content([item('Lower child'),item('')])]
];
for(const [upper,lower] of cases) {
  for(const forward of [false,true]) {
    assert.deepEqual(await exercise('People',upper,lower,forward),await exercise('Dashboard',upper,lower,forward),`People and Dashboard agree for ${forward?'Delete':'Backspace'}`);
  }
}
console.log('Dashboard/People parity: boundary joins, nested content, caret, task identity, Undo and Redo');

