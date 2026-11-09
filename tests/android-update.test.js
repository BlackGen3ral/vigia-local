import { test } from 'node:test';
import assert from 'node:assert/strict';
import worker from '../worker.js';

test('Android downloads use hosted Vigia files with no GitHub request or redirect', async () => {
  const original=globalThis.fetch;
  globalThis.fetch=async ()=>{throw new Error('External request forbidden');};
  const bytes=new Uint8Array([1,2,3,4]);
  const release={version:'1.3.1',size:bytes.length,sha256:'a'.repeat(64)};
  const env={ASSETS:{async fetch(request){
    const url=new URL(request.url);
    assert.equal(url.origin,'https://vigia-local.pages.dev');
    if(url.pathname==='/android-update.json')return Response.json(release);
    assert.equal(url.pathname,'/android-update/VigiaLocal-Android.apk');
    return new Response(bytes);
  }}};
  const ctx={waitUntil(promise){return promise;}};
  const request=path=>worker.fetch(new Request('https://vigia-local.pages.dev/api/android/'+path),env,ctx);
  try {
    const response=await request('update');assert.equal(response.status,200);
    const manifest=await response.json();
    assert.equal(manifest.url,'https://vigia-local.pages.dev/api/android/apk/v1.3.1');
    assert.equal(manifest.sha256,'a'.repeat(64));
    for(const version of ['v1.3.1','latest']) {
      const apk=await request('apk/'+version);
      assert.equal(apk.status,200);assert.equal(apk.headers.get('Location'),null);
      assert.equal(apk.headers.get('Content-Type'),'application/vnd.android.package-archive');
      assert.deepEqual(new Uint8Array(await apk.arrayBuffer()),bytes);
    }
    assert.equal((await request('apk/v1.3.0')).status,409);
    release.sha256='invalid';assert.equal((await request('update')).status,503);
    release.sha256='a'.repeat(64);release.size=999999999;
    assert.equal((await request('update')).status,503);
    assert.equal((await request('apk/not-a-version')).status,401);
  } finally {globalThis.fetch=original;}
});
