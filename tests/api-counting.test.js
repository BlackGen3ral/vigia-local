import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import worker from '../worker.js';

// Execute the real Worker SQL against SQLite; never connect to the live database.
test('entry counts, UUID deduplication and notification slots are independent', async () => {
  const db = new DatabaseSync(':memory:');
  for (const file of ['schema.sql','migrations/002-invite-codes.sql','migrations/003-event-thumbnails.sql','migrations/004-android-thumbnails.sql','migrations/005-directional-counting.sql','migrations/006-arrival-group-size.sql'])
    db.exec(readFileSync(new URL('../'+file,import.meta.url),'utf8'));
  const pending = [];
  const env = { ENROLL_KEY: 'b'.repeat(64), DB: { prepare(sql) {
    const stmt = db.prepare(sql);
    return { bind(...args) { return {
      async run() { const result=stmt.run(...args); return { meta:{changes:Number(result.changes)} }; },
      async all() { return {results:stmt.all(...args)}; },
      async first() { return stmt.get(...args) ?? null; }
    }; } };
  } } };
  const ctx = { waitUntil(promise) { pending.push(promise); } };
  const writer='a'.repeat(64);
  const reader=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',new TextEncoder().encode('read:'+writer))),v=>v.toString(16).padStart(2,'0')).join('');
  let now=Date.now();const realNow=Date.now; Date.now=()=>now;
  async function request(path,method='GET',data,token=reader,extra={}) {
    const response = await worker.fetch(new Request('https://example.test/api/'+path,{
      method,headers:{Authorization:'Bearer '+token,'Content-Type':'application/json',...extra},
      ...(data ? {body:JSON.stringify(data)} : {})
    }),env,ctx);
    assert.equal(response.status,200,await response.clone().text());
    const result=await response.json();await Promise.all(pending.splice(0));return result;
  }
  try {
    await request('activate','POST',null,writer,{'X-Vigia-Enrollment':env.ENROLL_KEY});
    const id=crypto.randomUUID();
    const first=await request('events','POST',{id,kind:'arrival'},writer);
    assert.equal(first.accepted,true);assert.equal(first.notified,true);
    const duplicate=await request('events','POST',{id,kind:'arrival'},writer);
    assert.equal(duplicate.accepted,false);
    now+=200;
    const second=await request('events','POST',{id:crypto.randomUUID(),kind:'arrival'},writer);
    assert.equal(second.accepted,true);assert.equal(second.notified,false);
    const group=await Promise.all(Array.from({length:4},()=>request('events','POST',{id:crypto.randomUUID(),kind:'arrival'},writer)));
    assert.ok(group.every(e=>e.accepted&&!e.notified));
    let history=await request('events');
    assert.equal(history.totalEntries,6);assert.equal(history.events.length,6);
    assert.equal(history.events.filter(e=>e.notified).length,1);
    now+=20000;
    assert.equal((await request('events','POST',{id:crypto.randomUUID(),kind:'arrival'},writer)).notified,true);
    await request('events','POST',{id:crypto.randomUUID(),kind:'test'},writer);
    assert.equal((await request('events')).totalEntries,7,'tests never count as entries');
    for(let i=0;i<205;i++) {now++;await request('events','POST',{id:crypto.randomUUID(),kind:'arrival'},writer);}
    history=await request('events');
    assert.equal(history.totalEntries,212,'totals survive event retention cleanup');
    assert.equal(history.events.length,60);
    assert.equal(db.prepare('SELECT COUNT(*) AS n FROM events').get().n,200);
    assert.ok(history.countingSince>0);
    now+=20000;
    const groupId=crypto.randomUUID();
    const groupEvent={id:groupId,kind:'arrival',people:3};
    assert.equal((await request('events','POST',groupEvent,writer)).notified,true);
    assert.equal((await request('events','POST',groupEvent,writer)).accepted,false);
    history=await request('events');
    assert.equal(history.totalEntries,215);
    assert.equal(history.events.find(event=>event.id===groupId).people,3);
    const invalid=await worker.fetch(new Request('https://example.test/api/events',{method:'POST',headers:{Authorization:'Bearer '+writer,'Content-Type':'application/json'},body:JSON.stringify({id:'bad',kind:'arrival'})}),env,ctx);
    assert.equal(invalid.status,400);
  } finally {Date.now=realNow;db.close();}
});
