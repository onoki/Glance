import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {compileScript,parse} from '@vue/compiler-sfc';
import {createRenderer,getCurrentInstance,h,nextTick,ref} from 'vue';
import {useKeyboardShortcuts} from '../src/composables/useKeyboardShortcuts.js';
const selections=[];
const shortcuts=useKeyboardShortcuts({activeTab:ref('Dashboard'),onSelectTab:tab=>selections.push(tab)});
for(const [index,name]of ['Dashboard','People','Search','History'].entries()){
 let prevented=false,stopped=false;
 shortcuts.handleGlobalShortcut({key:String(index+1),altKey:true,preventDefault(){prevented=true;},stopPropagation(){stopped=true;}});
 assert.equal(selections.at(-1),name);assert.ok(prevented&&stopped);
}
for(const extra of [{ctrlKey:true},{metaKey:true},{shiftKey:true},{isComposing:true}])shortcuts.handleGlobalShortcut({key:'3',altKey:true,...extra});
assert.equal(selections.length,4,'AltGr, modified shortcuts and composition are untouched');
const node=type=>({type,children:[],parent:null});
const renderer=createRenderer({createElement:node,createText:()=>node('text'),createComment:()=>node('comment'),setText(){},setElementText(){},patchProp(){},parentNode:n=>n.parent,nextSibling:()=>null,insert(n,p){n.parent=p;p.children.push(n);},remove(n){if(n.parent)n.parent.children.splice(n.parent.children.indexOf(n),1);}});
const component=async name=>{
 const url=new URL(`../src/components/views/${name}.vue`,import.meta.url);
 let script=compileScript(parse(await readFile(url,'utf8')).descriptor,{id:name}).content;
 script=script.replace(/import (\w+) from "[^"\n]+\.vue";/g,'const $1 = {};').replace(/from "([^"]+)"/g,(_,s)=>`from ${JSON.stringify(s.startsWith('.')?new URL(s,url).href:import.meta.resolve(s))}`);
 return (await import(`data:text/javascript;base64,${Buffer.from(script).toString('base64')}`)).default;
};
const noop=()=>{};
const Search=await component('SearchView');let focusCount=0;
Search.render=()=>{getCurrentInstance().setupState.searchRoot={querySelector:()=>({focus:()=>focusCount++})};return h('search');};
for(let i=0;i<2;i++){
 const app=renderer.createApp({render:()=>h(Search,{searchQuery:'retained',searchResults:[],hasSearched:false,isSearching:false,onSearchTasks:noop,noop,noopAsync:noop,onOpenSource:noop})});
 app.mount(node('root'));await nextTick();await nextTick();assert.equal(focusCount,i+1);app.unmount();
}
const store=new Map([['glance.selectedPerson','alice']]);
globalThis.sessionStorage={getItem:k=>store.get(k)||null,setItem:(k,v)=>store.set(k,v),removeItem:k=>store.delete(k)};
globalThis.window={setInterval:()=>1,clearInterval(){},alert:message=>{throw new Error(message);}};
globalThis.document={addEventListener(){},removeEventListener(){},querySelectorAll:()=>[],visibilityState:'visible'};
globalThis.requestAnimationFrame=fn=>fn();
const people=[{id:'alice',displayName:'Alice',position:0,tagIds:[]},{id:'bob',displayName:'Bob',position:1,tagIds:[]},{id:'anna',displayName:'Ännä',position:2,tagIds:[]},{id:'archived',displayName:'Old Alice',position:3,archivedAt:1,tagIds:[]}];
const doc=text=>({type:'doc',content:[{type:'paragraph',content:[{type:'text',text}]}]});
let taskReads=0;
globalThis.fetch=async path=>({ok:true,json:async()=>{
 if(path.startsWith('/api/people?'))return {people,tags:[]};
 if(path.endsWith('/tasks')){taskReads++;const id=path.split('/')[3];return {tasks:[{id:`note-${id}`,ownerPersonId:id,page:'people:main',title:doc('KEEP'),content:{type:'doc',content:[{type:'paragraph'}]},createdAt:123,updatedAt:1,position:0}]};}
 throw new Error(`Unexpected request ${path}`);
}});
const People=await component('PeopleView');let state;
People.render=()=>{state=getCurrentInstance().setupState;return h('people');};
const app=renderer.createApp({render:()=>h(People)});app.mount(node('root'));
const settle=async()=>{for(let i=0;i<12;i++){await nextTick();await new Promise(r=>setImmediate(r));}};
try{
 await settle();assert.equal(state.selectedId,'alice');const reads=taskReads;
 state.personFilter='BO';await nextTick();assert.deepEqual(state.filteredPeople.map(p=>p.id),['bob']);
 assert.equal(state.selectedId,'alice');assert.equal(state.tasks[0].id,'note-alice');assert.equal(taskReads,reads,'filtering must not switch notes or reload tasks');
 await state.selectFirstMatchingPerson();await settle();assert.equal(state.selectedId,'bob');assert.equal(state.tasks[0].id,'note-bob');
 state.personFilter=' äN ';await nextTick();assert.deepEqual(state.filteredPeople.map(p=>p.id),['anna']);
 state.personFilter='missing';await nextTick();assert.equal(state.filteredPeople.length,0);assert.equal(state.selectedPerson.id,'bob');
 await state.selectFirstMatchingPerson();assert.equal(state.selectedId,'bob');
 state.personFilter='';await nextTick();assert.equal(state.filteredPeople.length,3,'clearing restores active people only');
}finally{app.unmount();for(const key of ['sessionStorage','window','document','fetch','requestAnimationFrame'])delete globalThis[key];}
console.log('Alt navigation, search-entry focus and People filtering preserve current notes');
