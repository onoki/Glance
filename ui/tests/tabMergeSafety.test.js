import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { compileScript, parse } from "@vue/compiler-sfc";
import { createRenderer, getCurrentInstance, h, nextTick, ref } from "vue";
import { saveCoordinator } from "../src/services/saveCoordinator.js";
import { useDashboardData } from "../src/composables/useDashboardData.js";
import { indentTaskDocuments } from "../src/utils/taskJoin.js";
import { usePeopleTasks } from "../src/composables/usePeopleTasks.js";
const url = new URL("../src/components/TaskItem.vue",import.meta.url);
let script=compileScript(parse(await readFile(url,"utf8")).descriptor,{id:"tab-merge-safety"}).content;
script=script.replace(/import (\w+) from "[^"\n]+\.vue";/g,"const $1 = {};").replace(/from "([^"]+)"/g,(_,s)=>`from ${JSON.stringify(s.startsWith('.')?new URL(s,url).href:import.meta.resolve(s))}`);
const {default:TaskItem}=await import(`data:text/javascript;base64,${Buffer.from(script).toString('base64')}`);
const node=type=>({type,children:[],parent:null});
const renderer=createRenderer({createElement:node,createText:()=>node('text'),createComment:()=>node('comment'),setText(){},setElementText(){},patchProp(){},parentNode:n=>n.parent,nextSibling:n=>n.parent?.children[n.parent.children.indexOf(n)+1]||null,
  insert(n,p,anchor){if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1);n.parent=p;const index=anchor?p.children.indexOf(anchor):-1;p.children.splice(index<0?p.children.length:index,0,n);},remove(n){if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1);n.parent=null;}});
const empty={type:'doc',content:[{type:'paragraph'}]};
const title=text=>({type:'doc',content:[{type:'paragraph',content:[{type:'text',text}]}]});
const item=text=>({type:'listItem',content:[text?title(text).content[0]:{type:'paragraph'}]});
const content={type:'doc',content:[{type:'bulletList',content:[item(''),item('KEEP'),item('')]}]};
const clone=value=>JSON.parse(JSON.stringify(value));
// Blank rows, nested children and rich inline nodes survive on both sides.
const nested={type:'listItem',content:[title('Nested parent').content[0],{type:'bulletList',content:[item(''),item('Nested KEEP'),item('')]}]};
const rich={type:'listItem',content:[{type:'paragraph',content:[{type:'text',text:'Linked',marks:[{type:'link',attrs:{href:'https://example.com'}}]},{type:'hardBreak'},{type:'image',attrs:{src:'/fixture.png'}}]}]};
const variants=[empty, content,{type:'doc',content:[{type:'bulletList',content:[item(''),item('')]}]}, {type:'doc',content:[{type:'bulletList',content:[item(''),nested,rich,item('')]}]}];
for(const upper of variants) for(const lower of variants) {
  const before=JSON.stringify([upper,lower]);
  const result=indentTaskDocuments({content:upper},{title:empty,content:lower});
  const upperItems=upper.content[0].type==='bulletList'?upper.content[0].content:[];
  const lowerItems=lower.content[0].type==='bulletList'?lower.content[0].content:[];
  assert.deepEqual(result.content.content[0].content,[...upperItems,{type:'listItem',content:[{type:'paragraph',content:[]}]},...lowerItems]);
  assert.equal(result.listIndex,upperItems.length);
  assert.equal(JSON.stringify([upper,lower]),before,'transformation cannot mutate saved snapshots');
}
const previousGlobals=Object.fromEntries(['window','document','ResizeObserver','requestAnimationFrame','cancelAnimationFrame','fetch'].map(k=>[k,globalThis[k]]));
globalThis.window={addEventListener(){},removeEventListener(){}};
globalThis.document={addEventListener(){},removeEventListener(){},querySelector:()=>null,querySelectorAll:()=>[]};
globalThis.ResizeObserver=class {observe(){}disconnect(){}};
globalThis.requestAnimationFrame=callback=>{callback();return 0;};globalThis.cancelAnimationFrame=()=>{};
const noop=()=>{};
const bindings=Object.fromEntries(['onComplete','onCreateBelow','onTabToPrevious','onSplitToNewTask','onFocusPrevTaskFromTitle','onFocusNextTaskFromContent','onDelete'].map(k=>[k,noop]));
try {
  for(const view of ['Dashboard','People']) {
    const page=view==='People'?'people:main':'dashboard:new';
    const rows=[{id:'upper',page,title:title('Parent'),content:clone(content),position:0,updatedAt:1,completedAt:null,ownerPersonId:'person'},
      ...Array.from({length:10},(_,i)=>({id:`empty-${i}`,page,title:clone(empty),content:clone(empty),position:i+1,updatedAt:1,completedAt:null,ownerPersonId:'person'}))];
    let releaseSave, stallSave=false;
    const update=async(id,payload)=>{
      if (stallSave) { stallSave=false; await new Promise(resolve=>{releaseSave=resolve;}); }
      const row=rows.find(r=>r.id===id);assert.ok(row);
      if(payload.baseUpdatedAt!==row.updatedAt) throw Object.assign(new Error('Conflict'),{status:409,payload:{currentUpdatedAt:row.updatedAt}});
      Object.assign(row,clone(payload),{updatedAt:row.updatedAt+1});return {updatedAt:row.updatedAt};
    };
    const removed=new Map();
    const remove=async id=>{const index=rows.findIndex(r=>r.id===id);assert.ok(index>=0);removed.set(id,clone(rows[index]));rows.splice(index,1);return {ok:true};};
    const restore=async id=>{const row=removed.get(id);assert.ok(row);row.updatedAt++;rows.push(row);removed.delete(id);return {updatedAt:row.updatedAt};};
    const fetchTasks=async()=>({tasks:clone(rows.filter(r=>r.page===page))});
    globalThis.fetch=async(path,request)=>{try {
      const result=path==='/api/dashboard'?{newTasks:clone(rows.filter(r=>r.page==='dashboard:new')),mainTasks:[]}:path.endsWith('/restore')?await restore(path.split('/')[3]):request.method==='DELETE'?await remove(path.split('/')[3].split('?')[0]):await update(path.split('/')[3],JSON.parse(request.body));
      return {ok:true,json:async()=>result};
    }catch(e){return {ok:false,status:e.status,json:async()=>({message:e.message,...e.payload})};}};
    const model=view==='People'?usePeopleTasks({selectedPersonId:ref('person'),api:{fetchPersonTasks:fetchTasks,updateTask:update,deleteTask:remove,restoreTask:restore}}):useDashboardData();
    const tasks=view==='People'?model.tasks:model.newTasks;
    await (view==='People'?model.loadTasks():model.loadDashboard());
    const states=new Map();
    TaskItem.render=function(){states.set(getCurrentInstance().props.task.id,getCurrentInstance().setupState);return h('task');};
    const app=renderer.createApp({render:()=>h('list',tasks.value.map(task=>h(TaskItem,{...bindings,key:task.id,task,onSave:model.saveTask,onDirty:model.handleDirtyChange})))});
    app.mount(node('root'));
    try {
      // A drag can happen before title autosave finishes. The metadata write
      // must use the acknowledged revision and must not lose that pending edit.
      states.get('upper').title=title('Pending drag edit');
      states.get('upper').scheduleSave();
      stallSave=true;
      const pendingDragSave=saveCoordinator.flushAll();
      await nextTick();
      let moving;
      if(view==='People') {
        model.startDrag(tasks.value.find(task=>task.id==='upper'));
        model.setDragOver('empty-0','after');
        moving=model.dropOnTask(tasks.value.find(task=>task.id==='empty-0'),'people');
        model.endDrag(); // Native dragend occurs before a slow save completes.
      } else moving=model.applyTaskMove(tasks.value.find(task=>task.id==='upper'),'new',-1);
      assert.ok(releaseSave);
      releaseSave();
      await pendingDragSave; await moving; await nextTick();
      assert.equal(rows.find(row=>row.id==='upper').title.content[0].content[0].text,'Pending drag edit');
      assert.equal((await saveCoordinator.flushAll()).ok,true,'dragging after an in-flight save cannot create a self-conflict');
      if(view==='People') {
        assert.ok(rows.find(row=>row.id==='upper').position > rows.find(row=>row.id==='empty-0').position,'the indicated after position survives dragend while saving');
        model.startDrag(tasks.value.find(task=>task.id==='upper'));
        model.setDragOver('empty-0','before');
        await model.dropOnTask(tasks.value.find(task=>task.id==='empty-0'),'people');
        await nextTick();
      }
      for(let i=0;i<8;i++) {
        // A pending autosave belongs to this same window and must settle before
        // the structural write takes its content/revision snapshot.
        const upper=states.get('upper');
        upper.title=title(`Parent edit ${i}`);upper.scheduleSave();
        let inFlight=null;
        if (i===0) { stallSave=true; inFlight=saveCoordinator.flushAll(); await nextTick(); }
        const mutation=model.moveTaskToPrevious(tasks.value.find(t=>t.id===`empty-${i}`));
        if (inFlight) { assert.ok(releaseSave); releaseSave(); await inFlight; }
        assert.equal(await mutation,true,`${view} iteration ${i}: ${JSON.stringify(saveCoordinator.getSummary().failures)}`);
        assert.equal((await saveCoordinator.flushAll()).ok,true,`${view}: no self-conflict after Tab merge ${i}`);
        const saved=rows.find(r=>r.id==='upper');
        assert.equal(saved.title.content[0].content[0].text,`Parent edit ${i}`);
        assert.deepEqual(saved.content.content[0].content.slice(0,3),content.content[0].content,`${view}: retain leading and trailing blank lines`);
        assert.equal(saved.content.content[0].content.length,4+i);
        assert.equal(model.focusContentTarget.value.listIndex,3+i);
        await nextTick();
      }
      // Adjacent source rows can be reached before the earlier merge finishes.
      const concurrentSources=tasks.value.filter(task=>task.id!=='upper');
      const rapidHistory=[...concurrentSources.map(task=>model.moveTaskToPrevious(task)),model.undo()];
      assert.deepEqual(await Promise.all(rapidHistory),[true,true,true]);
      assert.equal(tasks.value.length,2,'immediate Undo runs after the pending Tab merges');
      assert.equal(rows.find(row=>row.id==='upper').content.content[0].content.length,12);
      assert.equal(tasks.value.some(task=>task.id==='empty-9'),true,'Undo restores original source identity');
      assert.equal(await model.redo(),true);
      assert.equal(rows.find(row=>row.id==='upper').content.content[0].content.length,13);
      assert.equal(tasks.value.length,1);
      assert.deepEqual(saveCoordinator.getSummary().failures,[]);
    }finally{app.unmount();await saveCoordinator.flushAll();await nextTick();}
  }
}finally{for(const [k,v] of Object.entries(previousGlobals))globalThis[k]=v;}
console.log('Repeated Tab merges preserve blank lines and pending edits without self-conflicts in Dashboard/People');
