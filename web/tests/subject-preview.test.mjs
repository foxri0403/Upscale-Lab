import test from 'node:test';
import assert from 'node:assert/strict';
import { subjectPreview } from '../src/subject-preview.ts';

function fixture(statuses=['Completed'], foreground=true) {
  const calls=[], output=new Blob(['cutout'],{type:'image/png'});
  const api={
    async json(path,init={}) {
      calls.push({path,...init});
      if(path==='/api/projects') {
        assert.equal(init.method,'POST'); assert.equal(init.body.get('IsDraft'),'true');
        return {id:'preview-draft'};
      }
      if(path.endsWith('/process')||path.endsWith('/processing-status')) return {status:statuses.shift(),progress:50,errorMessage:'preview failed'};
      assert.equal(path,'/api/projects/preview-draft');
      return {layers:foreground?[{id:'background',layerType:'Background'},{id:'body',layerType:'Body'}]:[]};
    },
    async blob(path){calls.push({path});assert.equal(path,'/api/projects/preview-draft/content?layerId=body');return output;},
    async remove(path){calls.push({path,method:'DELETE'});},
  };
  return {api,calls,output};
}
test('automatic preview uses a private draft and cleans it after downloading the foreground',async()=>{
  const {api,calls,output}=fixture(['Queued','Processing','Completed']), changes=[];
  const result=await subjectPreview(api,new Blob(['original'],{type:'image/png'}),new AbortController().signal,j=>changes.push(j.status),async()=>{});
  assert.equal(result,output);assert.deepEqual(changes,['Queued','Processing','Completed']);
  assert.deepEqual(calls.at(-1),{path:'/api/projects/preview-draft',method:'DELETE'});
  assert.ok(calls.every(x=>x.path==='/api/projects'||x.path.startsWith('/api/projects/preview-draft')));
});
test('failed preview cleans its terminal draft and returns the failure',async()=>{
  const {api,calls}=fixture(['Failed']);
  await assert.rejects(subjectPreview(api,new Blob(['x']),new AbortController().signal,()=>{}),/preview failed/);
  assert.equal(calls.at(-1).method,'DELETE');
});
test('interruption preserves a running draft for the existing server cleanup policy',async()=>{
  const {api,calls}=fixture(['Queued']), controller=new AbortController();
  await assert.rejects(subjectPreview(api,new Blob(['x']),controller.signal,()=>{},async()=>{controller.abort();controller.signal.throwIfAborted();}),{name:'AbortError'});
  assert.ok(!calls.some(x=>x.method==='DELETE'));
});
test('a terminal preview without a foreground fails and cleans its draft',async()=>{
  const {api,calls}=fixture(['Completed'],false);
  await assert.rejects(subjectPreview(api,new Blob(['x']),new AbortController().signal,()=>{}),/인식 결과/);
  assert.equal(calls.at(-1).method,'DELETE');
});
test('a cancelled preview never creates a project',async()=>{
  const {api,calls}=fixture(), controller=new AbortController();controller.abort();
  await assert.rejects(subjectPreview(api,new Blob(['x']),controller.signal,()=>{}),{name:'AbortError'});
  assert.equal(calls.length,0);
});
