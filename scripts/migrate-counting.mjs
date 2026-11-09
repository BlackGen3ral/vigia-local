import { spawnSync } from 'node:child_process';
const run = args => {
  const result=spawnSync(process.execPath,['node_modules/wrangler/bin/wrangler.js','d1','execute','vigia-local','--remote',...args],{encoding:'utf8',env:process.env});
  if(result.status!==0) throw new Error(result.stderr || result.stdout || 'D1 migration failed');
  return result.stdout;
};
const columns=JSON.parse(run(['--command','PRAGMA table_info(events)','--json']))[0].results;
if(columns.some(c=>c.name==='notified')) {
  const stores=JSON.parse(run(['--command','PRAGMA table_info(stores)','--json']))[0].results;
  if(!['total_entries','counting_since','last_arrival_notice'].every(name=>stores.some(c=>c.name===name)))
    throw new Error('Partial counting migration; inspect before deployment.');
  console.log('Counting schema already installed.');
} else {
  run(['--file','migrations/005-directional-counting.sql','--yes']);
  console.log('Counting schema installed; previous events and pairings preserved.');
}
