import { test } from 'node:test';
import assert from 'node:assert/strict';
import worker from '../worker.js';

test('Android downloads stay on Vigia and validate the release manifest', async () => {
  const original = globalThis.fetch;
  const bytes=new Uint8Array([1,2,3,4]);
  const asset={name:'VigiaLocal-Android.apk',size:bytes.length,digest:'sha256:'+'a'.repeat(64),
    browser_download_url:'https://github.com/BlackGen3ral/vigia-local/releases/download/v1.3.1/VigiaLocal-Android.apk'};
  const release={tag_name:'v1.3.1',draft:false,prerelease:false,assets:[asset]};
  const urls=[];
  globalThis.fetch=async (url,options)=>{
    urls.push(String(url));
    if(String(url).startsWith('https://api.github.com/')) return Response.json(release);
    assert.equal(url,asset.browser_download_url);assert.equal(options.redirect,'follow');
    return new Response(bytes);
  };
  const ctx={waitUntil(promise){return promise;}};
  try {
    const manifestResponse=await worker.fetch(new Request('https://vigia-local.pages.dev/api/android/update'),{},ctx);
    assert.equal(manifestResponse.status,200);
    const manifest=await manifestResponse.json();
    assert.equal(manifest.url,'https://vigia-local.pages.dev/api/android/apk/v1.3.1');
    assert.equal(manifest.version,'1.3.1');assert.equal(manifest.sha256,'a'.repeat(64));
    for(const version of ['v1.3.1','latest']) {
      const response=await worker.fetch(new Request('https://vigia-local.pages.dev/api/android/apk/'+version),{},ctx);
      assert.equal(response.status,200);assert.equal(response.headers.get('Location'),null);
      assert.equal(response.headers.get('Content-Type'),'application/vnd.android.package-archive');
      assert.deepEqual(new Uint8Array(await response.arrayBuffer()),bytes);
    }
    asset.digest='invalid';
    assert.equal((await worker.fetch(new Request('https://vigia-local.pages.dev/api/android/update'),{},ctx)).status,503);
    asset.digest='sha256:'+'a'.repeat(64);asset.browser_download_url='https://untrusted.example/app.apk';
    assert.equal((await worker.fetch(new Request('https://vigia-local.pages.dev/api/android/update'),{},ctx)).status,503);
    assert.equal((await worker.fetch(new Request('https://vigia-local.pages.dev/api/android/apk/not-a-version'),{},ctx)).status,401);
    assert.ok(urls.every(url=>url.startsWith('https://github.com/BlackGen3ral/vigia-local/')||url.startsWith('https://api.github.com/repos/BlackGen3ral/vigia-local/')));
  } finally {globalThis.fetch=original;}
});
