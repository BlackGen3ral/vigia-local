import { mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

// Called after the signed APK is published. GitHub is used only by the build,
// never by the installed Android app or the public download request.
const headers={Accept:'application/vnd.github+json','User-Agent':'VigiaLocal-Publish'};
if(process.env.GH_TOKEN) headers.Authorization='Bearer '+process.env.GH_TOKEN;
const releasesUrl = 'https://api.github.com/repos/BlackGen3ral/vigia-local/releases';
async function readReleases(path) {
  const response = await fetch(releasesUrl + path, { headers });
  if (!response.ok) throw new Error('Cannot read the Android release');
  return response.json();
}
const androidAsset = release => release.assets?.find(asset => asset.name === 'VigiaLocal-Android.apk');
let release = await readReleases('/latest');
if (!androidAsset(release)) {
  const releases = await readReleases('?per_page=30');
  release = releases.find(item => !item.draft && !item.prerelease && androidAsset(item));
}
if (!release) throw new Error('No stable Android release found');
if(release.draft || release.prerelease || !/^v[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$/.test(release.tag_name??'')) throw new Error('Invalid release');
const asset=androidAsset(release);
if(!asset || !Number.isSafeInteger(asset.size) || asset.size<=0 || asset.size>24*1024*1024 ||
 !/^sha256:[a-f0-9]{64}$/.test(asset.digest??'') ||
 asset.browser_download_url!==`https://github.com/BlackGen3ral/vigia-local/releases/download/${release.tag_name}/VigiaLocal-Android.apk`)
 throw new Error('Invalid Android asset');
const response=await fetch(asset.browser_download_url,{headers:{'User-Agent':'VigiaLocal-Publish'}});
if(!response.ok) throw new Error('Cannot download the signed APK');
const chunks=[];let length=0;
for await(const chunk of response.body) {length+=chunk.length;if(length>asset.size)throw new Error('APK too large');chunks.push(chunk);}
const bytes=Buffer.concat(chunks);
const sha256=createHash('sha256').update(bytes).digest('hex');
if(bytes.length!==asset.size || 'sha256:'+sha256!==asset.digest)throw new Error('APK checksum mismatch');
await mkdir('site/android-update',{recursive:true});
await writeFile('site/android-update/VigiaLocal-Android.apk',bytes);
await writeFile('site/android-update.json',JSON.stringify({version:release.tag_name.slice(1),size:asset.size,sha256}));
console.log(`Prepared Android ${release.tag_name}: verified APK hosted by Vigia.`);
