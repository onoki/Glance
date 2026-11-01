import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRenderer, getCurrentInstance, h, nextTick, ref } from 'vue';
import { compileScript, parse } from '@vue/compiler-sfc';
import { Schema } from 'prosemirror-model';
import { EditorState, TextSelection } from 'prosemirror-state';
import { useDashboardData } from '../src/composables/useDashboardData.js';
import { usePeopleTasks } from '../src/composables/usePeopleTasks.js';
import { splitAtSelection } from '../src/utils/editorListUtils.js';
import { saveCoordinator } from '../src/services/saveCoordinator.js';
const componentUrl = new URL('../src/components/TaskItem.vue', import.meta.url);
let script = compileScript(parse(await readFile(componentUrl, 'utf8')).descriptor, { id: 'task-safety' }).content;
script = script.replace(/import (\w+) from "[^"\n]+\.vue";/g, 'const $1 = {};')
  .replace(/from "([^"]+)"/g, (_, s) => `from ${JSON.stringify(s.startsWith('.') ? new URL(s, componentUrl).href : import.meta.resolve(s))}`);
const { default: TaskItem } = await import(`data:text/javascript;base64,${Buffer.from(script).toString('base64')}`);
const node = type => ({ type, children: [], parent: null });
const renderer = createRenderer({ createElement: node, createText: () => node('text'), createComment: () => node('comment'),
  setText() {}, setElementText() {}, patchProp() {}, parentNode: n => n.parent,
  nextSibling: n => n.parent?.children[n.parent.children.indexOf(n)+1] || null,
  insert(n,p,anchor) { if(n.parent) n.parent.children.splice(n.parent.children.indexOf(n),1); n.parent=p; const i=anchor?p.children.indexOf(anchor):-1; p.children.splice(i<0?p.children.length:i,0,n); },
  remove(n) { if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1);n.parent=null; }
});
const clone = value => JSON.parse(JSON.stringify(value));
const doc = text => ({type:'doc',content:[text?{type:'paragraph',content:[{type:'text',text}]}:{type:'paragraph'}]});
const list = (...texts) => ({type:'doc',content:[{type:'bulletList',content:texts.map(text=>({type:'listItem',content:doc(text).content}))}]});
let cases = 0;
const alerts = [];
const savedGlobals = Object.fromEntries(['window','document','ResizeObserver','requestAnimationFrame','cancelAnimationFrame','fetch'].map(k=>[k,globalThis[k]]));
globalThis.window={addEventListener(){},removeEventListener(){},alert(message){alerts.push(message);}};
globalThis.document={addEventListener(){},removeEventListener(){},querySelector:()=>null,querySelectorAll:()=>[]};
globalThis.ResizeObserver=class{observe(){}disconnect(){}};
globalThis.requestAnimationFrame=fn=>{fn();return 0;};globalThis.cancelAnimationFrame=()=>{};
const settle=async()=>{for(let i=0;i<15;i++){await nextTick();await new Promise(resolve=>setImmediate(resolve));}};

function transport(page, titles=['Source']) {
  const rows=titles.map((title,index)=>({id:`row-${index}`,page,ownerPersonId:'person',title:doc(title),content:doc(''),position:index,updatedAt:1,completedAt:null}));
  const removed=new Map();let sequence=0, releaseCreate, delayCreate=false, failCreate=false, failUpdates=false, afterUpdate;
  const update=async(id,payload)=>{
    if(failUpdates)throw Object.assign(new Error('Simulated save failure'),{status:503});
    const row=rows.find(row=>row.id===id);assert.ok(row);assert.equal(payload.baseUpdatedAt,row.updatedAt);
    Object.assign(row,clone(payload),{updatedAt:row.updatedAt+1});afterUpdate?.(id);
    return {updatedAt:row.updatedAt};
  };
  const create=async payload=>{if(delayCreate)await new Promise(resolve=>{releaseCreate=resolve;});if(failCreate)throw new Error('Simulated create failure');const taskId=`created-${++sequence}`;rows.push({...clone(payload),id:taskId,updatedAt:1,completedAt:null});return{taskId,updatedAt:1};};
  const remove=async(id,revision)=>{
    const i=rows.findIndex(row=>row.id===id);assert.ok(i>=0);
    if(revision!==undefined)assert.equal(revision,rows[i].updatedAt,'guarded delete rejects a newer row');
    removed.set(id,clone(rows[i]));rows.splice(i,1);return {ok:true};
  };
  const restore=async id=>{const row=removed.get(id);assert.ok(row);row.updatedAt++;rows.push(row);removed.delete(id);return{updatedAt:row.updatedAt};};
  const fetchTasks=async()=>({tasks:clone(rows)});
  globalThis.fetch=async(path,request)=>{try{
    let result;
    if(path==='/api/dashboard')result={newTasks:clone(rows.filter(r=>r.page==='dashboard:new')),mainTasks:clone(rows.filter(r=>r.page==='dashboard:main'))};
    else if(path==='/api/tasks')result=await create(JSON.parse(request.body));
    else if(path.endsWith('/restore'))result=await restore(path.split('/')[3]);
    else if(request.method==='DELETE'){const url=new URL(path,'http://fixture');const revision=url.searchParams.get('baseUpdatedAt');result=await remove(url.pathname.split('/')[3],revision===null?undefined:Number(revision));}
    else result=await update(path.split('/')[3],JSON.parse(request.body));
    return{ok:true,json:async()=>result};
  }catch(error){return{ok:false,status:error.status||500,json:async()=>({message:error.message})};}};
  return {rows,removed,update,create,remove,restore,fetchTasks,setDelay:()=>{delayCreate=true;},release:()=>releaseCreate(),setFail:value=>{failUpdates=value;},setCreateFail:value=>{failCreate=value;},onUpdate:fn=>{afterUpdate=fn;}};
}
const makeModel=(view,t)=>view==='People'?usePeopleTasks({selectedPersonId:ref('person'),api:{fetchPersonTasks:t.fetchTasks,updateTask:t.update,createTask:t.create,deleteTask:t.remove,restoreTask:t.restore}}):useDashboardData();
const load=(view,m)=>view==='People'?m.loadTasks():m.loadDashboard();
const tasks=(view,m)=>view==='People'?m.tasks:m.newTasks;
const mount=(view,m,onDelete=()=>{})=>{
  const states=new Map();TaskItem.render=function(){states.set(getCurrentInstance().props.task.id,getCurrentInstance().setupState);return h('task');};
  const noop=()=>{};
  const app=renderer.createApp({render:()=>h('list',tasks(view,m).value.map(task=>h(TaskItem,{key:task.id,task,onSave:m.saveTask,onDirty:m.handleDirtyChange,
    onComplete:noop,onCreateBelow:m.createTaskBelow,onTabToPrevious:noop,onSplitToNewTask:m.splitSubcontentToNewTask,onFocusPrevTaskFromTitle:noop,onFocusNextTaskFromContent:noop,onDelete})))});
  app.mount(node('root'));return{app,states};
};

try {
  for (const view of ['Dashboard', 'People']) {
    const page = view === 'People' ? 'people:main' : 'dashboard:new';
    // Tab nests a whole rich subtree; promotion reverses it in both list models.
    {
      const t=transport(page,['Parent','Child task']);
      const schema=new Schema({nodes:{doc:{content:'block+'},paragraph:{group:'block',content:'inline*'},bulletList:{group:'block',content:'listItem+'},listItem:{content:'paragraph block*'},text:{group:'inline'},hardBreak:{group:'inline',inline:true},image:{group:'inline',inline:true,atom:true,attrs:{src:{}}}},marks:{bold:{},link:{attrs:{href:{}}}}});
      t.rows[0].content=list('', 'Keep parent', '');
      t.rows[0].isPrivate = false;
      t.rows[1].isPrivate = true;
      t.rows[1].title=doc('Child task');
      t.rows[1].title.content[0].content.push({type:'hardBreak'},{type:'image',attrs:{src:'/child.png'}});
      t.rows[1].title.content[0].content[0].marks=[{type:'bold'}];
      t.rows[1].content=list('', 'Child content', '');
      t.rows[1].content.content[0].content[1].content.push({type:'bulletList',content:list('', 'Grandchild', '').content[0].content});
      const parentBefore=clone(t.rows[0].content), childBefore=clone(t.rows[1]);
      const m=makeModel(view,t);await load(view,m);
      assert.equal(await m.moveTaskToPrevious(tasks(view,m).value[1]),true);
      assert.equal(t.rows[0].isPrivate, true, 'merging private content protects the destination');
      const merged=clone(t.rows[0].content);
      assert.equal(merged.content[0].content.length,4);
      assert.deepEqual(merged.content[0].content[3].content[1],childBefore.content.content[0]);
      assert.equal(m.focusContentTarget.value.listIndex,3);
      await m.undo();
      assert.equal(t.rows.find(row=>row.id==='row-0').isPrivate, false);
      assert.equal(t.rows.find(row=>row.id==='row-1').isPrivate, true);
      assert.deepEqual(t.rows.find(row=>row.id==='row-0').content,parentBefore);
      assert.deepEqual(t.rows.find(row=>row.id==='row-1').content,childBefore.content);
      await m.redo();
      assert.deepEqual(t.rows[0].content,merged);
      await load(view,m);
      const {app,states}=mount(view,m);
      try {
        const docNode=schema.nodeFromJSON(merged);
        const beforeSize=docNode.child(0).content.size-docNode.child(0).lastChild.nodeSize;
        const state=EditorState.create({schema,doc:docNode,selection:TextSelection.create(docNode,beforeSize+3)});
        const split=splitAtSelection({state,getJSON:()=>docNode.toJSON()});
        await states.get('row-0').handleSplitToNewTask({...split,content:split.newTaskContent});
        await settle();
        const promoted=t.rows.find(row=>row.id!=='row-0');
        assert.ok(promoted);
        assert.equal(promoted.isPrivate, true, 'promoting private subcontent retains privacy');
        assert.deepEqual(schema.nodeFromJSON(promoted.title).toJSON(),schema.nodeFromJSON(childBefore.title).toJSON());
        assert.deepEqual(schema.nodeFromJSON(promoted.content).toJSON(),schema.nodeFromJSON(childBefore.content).toJSON());
        assert.deepEqual(schema.nodeFromJSON(t.rows.find(row=>row.id==='row-0').content).toJSON(),schema.nodeFromJSON(parentBefore).toJSON());
        await m.undo();assert.equal(t.rows.length,1);
        assert.deepEqual(t.rows[0].content,merged);
        await m.redo();assert.equal(t.rows.length,2);
        assert.deepEqual(schema.nodeFromJSON(t.rows.find(row=>row.id===promoted.id).content).toJSON(),schema.nodeFromJSON(childBefore.content).toJSON());
        cases++;
      } finally {app.unmount();await saveCoordinator.flushAll();}
    }
    // Delayed/failed creation, a failed shortening save, repeated promotion and
    // typing during creation. Rich/nested/image/blank siblings stay recoverable.
    for (const mode of ['success', 'create-failure', 'save-failure', 'typing']) {
      const t = transport(page);
      const original = list('', 'Keep above', 'Promote this', 'Keep below', '');
      original.content[0].content[2].content.push({type:'bulletList',content:[{type:'listItem',content:doc('KEEP nested').content}]});
      original.content[0].content[3].content[0].content.push({type:'image',attrs:{src:'/keep.png'}});
      original.content[0].content[2].content[0].content[0].marks=[{type:'bold'}];
      t.rows[0].content=clone(original);
      const m=makeModel(view,t);await load(view,m);
      const {app,states}=mount(view,m);
      try {
        t.setDelay();
        const state=states.get('row-0');
        const schema=new Schema({nodes:{doc:{content:'block+'},paragraph:{group:'block',content:'inline*'},bulletList:{group:'block',content:'listItem+'},listItem:{content:'paragraph block*'},text:{group:'inline'},image:{group:'inline',inline:true,atom:true,attrs:{src:{}}}},marks:{bold:{}}});
        const document=schema.nodeFromJSON(original);
        let position;
        document.descendants((node,pos)=>{if(node.type.name==='paragraph' && node.textContent==='Promote this')position=pos+8;});
        const editor={state:EditorState.create({schema,doc:document,selection:TextSelection.create(document,position)}),getJSON:()=>document.toJSON()};
        const split=splitAtSelection(editor);
        const payload=clone({remainingContent:split.remainingContent,title:split.title,content:split.newTaskContent,titleSelection:split.titleSelection});
        const pending=state.handleSplitToNewTask(payload);
        await settle();
        assert.deepEqual(t.rows[0].content,original,'source stays durable during delayed creation');
        assert.deepEqual(state.content,original,'visible source stays intact during delayed creation');
        await state.handleSplitToNewTask(payload); // blocked duplicate structural action
        assert.equal(t.rows.length,1);
        if(mode==='typing') { state.content=clone(original);state.content.content[0].content.push({type:'listItem',content:doc('New typing').content});state.markDirty(); }
        if(mode==='save-failure')t.setFail(true);
        if(mode==='create-failure') t.setCreateFail(true);
        t.release();await pending;await settle();
        if(mode==='success') {
          assert.deepEqual(t.rows[0].content,payload.remainingContent);
          assert.deepEqual(t.rows[1].title,payload.title);assert.deepEqual(t.rows[1].content,payload.content);
          assert.deepEqual(m.focusTaskId.value,{taskId:'created-1',selection:{from:8,to:8}});
          await m.undo();assert.deepEqual(t.rows.find(r=>r.id==='row-0').content,original);
          await m.redo();assert.deepEqual(t.rows.find(r=>r.id==='row-0').content,payload.remainingContent);
        } else {
          assert.deepEqual(t.rows[0].content,original,'a failed source commit preserves durable original');
          assert.match(alerts.at(-1),/kept|updated|changed|preserved|failure/i);
          if(mode==='save-failure') {
            assert.deepEqual(state.content,original,'failed save restores complete local source');
            t.setFail(false);await state.saveNow(true,{suppressUndo:true});
            await m.undo();assert.deepEqual(t.rows[0].content,original,'partial Undo removes only the copy after retry');
          }
          if(mode==='typing') {
            assert.match(JSON.stringify(state.content),/New typing/);
            await state.saveNow(true,{suppressUndo:true});await m.undo();
            assert.match(JSON.stringify(t.rows[0].content),/New typing/);
          }
        }
        cases++;
      } finally {t.setFail(false);app.unmount();await saveCoordinator.flushAll();}
    }
    {
      const t=transport(page);const m=makeModel(view,t);await load(view,m);
      t.setFail(true);
      await assert.rejects(m.saveTask({id:'row-0',title:doc('Failed edit'),content:doc(''),baseUpdatedAt:1}),/Simulated save failure/);
      t.setFail(false);assert.equal(await m.undo(),false,'a failed edit must not create successful history');
      assert.deepEqual(t.rows[0].title,doc('Source'));cases++;
    }
    {
      const t=transport(page);const m=makeModel(view,t);await load(view,m);
      const source=tasks(view,m).value[0];
      m.handleDirtyChange(source.id,true,{...source,title:doc('Unflushed title'),content:list('', 'Unflushed rich content', '')});
      await (view==='People'?m.removeTask(source):m.deleteTask(source));
      await m.undo();
      const restored=t.rows.find(row=>row.id===source.id);
      assert.deepEqual(restored.title,doc('Unflushed title'));
      assert.deepEqual(restored.content,list('', 'Unflushed rich content', ''));cases++;
    }
    // Ordinary deletion rejects a newer source, including after local edits.
    {
      const t=transport(page);const m=makeModel(view,t);await load(view,m);
      const stale=tasks(view,m).value[0];t.rows[0].title=doc('NEW external text');t.rows[0].updatedAt++;
      await assert.rejects(view==='People'?m.removeTask(stale):m.deleteTask(stale),/guarded delete/);
      assert.equal(t.rows.length,1);assert.deepEqual(t.rows[0].title,doc('NEW external text'));cases++;
    }
    // A concurrent lower-row write between copying and deleting must survive.
    {
      const t=transport(page,['Upper ','Lower']);const m=makeModel(view,t);await load(view,m);
      t.onUpdate(id=>{if(id==='row-0'){const lower=t.rows.find(r=>r.id==='row-1');lower.title=doc('NEW lower text');lower.updatedAt++;}});
      await assert.rejects(m.mergeTaskToPrevious(tasks(view,m).value[1]),/guarded delete/);
      assert.equal(t.rows.length,2);assert.deepEqual(t.rows[1].title,doc('NEW lower text'));
      await assert.rejects(m.undo(),/changed elsewhere/);assert.deepEqual(t.rows[1].title,doc('NEW lower text'));cases++;
    }
    // Undo of a split cannot delete the only suffix copy before restoring source.
    {
      const t=transport(page,['Full original title']);const m=makeModel(view,t);await load(view,m);
      await m.splitTitleToNewTask(tasks(view,m).value[0],'new',{beforeTitle:doc('Full original title'),beforeContent:doc(''),afterTitle:doc('Full '),afterContent:doc(''),newTitle:doc('original title'),newContent:doc(''),
        commitSource:async()=>{await m.saveTask({id:'row-0',title:doc('Full '),content:doc(''),baseUpdatedAt:t.rows[0].updatedAt,suppressUndo:true});return true;}});
      t.setFail(true);await assert.rejects(m.undo(),/Simulated save failure/);
      assert.equal(t.rows.length,2,'suffix copy remains visible after failed restoration');
      t.setFail(false);await m.undo();assert.deepEqual(t.rows[0].title,doc('Full original title'));assert.equal(t.rows.length,1);
      await m.redo();assert.equal(t.rows.length,2);cases++;
    }
    // Redo of a merge must save the combined destination before deleting source.
    {
      const t=transport(page,['Upper ','Lower']);const m=makeModel(view,t);await load(view,m);
      await m.mergeTaskToPrevious(tasks(view,m).value[1]);await m.undo();t.setFail(true);
      await assert.rejects(m.redo(),/Simulated save failure/);assert.equal(t.rows.length,2);
      t.setFail(false);await m.redo();assert.equal(t.rows.length,1);assert.equal(t.rows[0].title.content[0].content.map(node=>node.text).join(''),'Upper Lower');cases++;
    }
    // Refreshed revisions do not authorize overwriting external text in history.
    {
      const t=transport(page);const m=makeModel(view,t);await load(view,m);
      await m.saveTask({id:'row-0',title:doc('Local edit'),content:doc(''),baseUpdatedAt:1});
      t.rows[0].title=doc('NEW external text');t.rows[0].updatedAt++;
      await load(view,m);await assert.rejects(m.undo(),/changed elsewhere/);
      assert.deepEqual(t.rows[0].title,doc('NEW external text'));cases++;
    }
    // Image atoms are populated titles, even when textContent is empty.
    {
      const t=transport(page);t.rows[0].title={type:'doc',content:[{type:'paragraph',content:[{type:'image',attrs:{src:'/keep.png'}}]}]};
      const m=makeModel(view,t);await load(view,m);let deletes=0;const {app,states}=mount(view,m,()=>{deletes++;});
      try {
        const schema=new Schema({nodes:{doc:{content:'paragraph+'},paragraph:{content:'inline*'},text:{group:'inline'},image:{group:'inline',inline:true,atom:true,attrs:{src:{}}}}});
        const docNode=schema.nodeFromJSON(t.rows[0].title);const editor={state:EditorState.create({schema,doc:docNode,selection:TextSelection.create(docNode,1)})};
        states.get('row-0').handleTitleKeydown({key:'Backspace',preventDefault(){}},editor);
        await settle();assert.equal(deletes,0);assert.equal(t.rows.length,1);cases++;
      }finally {app.unmount();await saveCoordinator.flushAll();}
    }
  }
  console.log(`Task safety: ${cases} Dashboard/People cases, delayed promotion, failed history, concurrent deletion and image titles`);
} finally {for(const[k,v]of Object.entries(savedGlobals))globalThis[k]=v;}
