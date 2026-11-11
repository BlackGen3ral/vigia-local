import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const key = process.env.ELEVENLABS_API_KEY;
const voice = process.env.ELEVENLABS_VOICE_ID;
if (!key || !/^[a-zA-Z0-9]{10,40}$/.test(voice ?? '')) {
  throw new Error('Configure ELEVENLABS_API_KEY e ELEVENLABS_VOICE_ID antes de gerar as falas.');
}

const subscription = await fetch('https://api.elevenlabs.io/v1/user/subscription', {
  headers: { 'xi-api-key': key },
  signal: AbortSignal.timeout(10000)
});
if (!subscription.ok) throw new Error('Não foi possível verificar o plano do ElevenLabs.');
const { tier } = await subscription.json();
if (!tier || tier === 'free') throw new Error('Use um plano com licença comercial para gerar falas para o Vigia da loja.');

const clips = new URL('../voice/', import.meta.url);
await mkdir(clips, { recursive: true });
for (let people = 1; people <= 20; people++) {
  const endpoint = 'https://api.elevenlabs.io/v1/text-to-speech/' + voice + '?output_format=mp3_44100_64';
  const text = people === 1 ? 'Uma pessoa chegou.' : `${people} pessoas chegaram.`;
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: { 'xi-api-key': key, 'Content-Type': 'application/json', Accept: 'audio/mpeg' },
    body: JSON.stringify({ text, model_id: 'eleven_multilingual_v2' }),
    signal: AbortSignal.timeout(30000)
  });
  if (!response.ok || !response.headers.get('content-type')?.startsWith('audio/mpeg')) {
    const error = await response.json().catch(() => null);
    const status = error?.detail?.status;
    const reason = typeof status === 'string' && /^[a-z_]{1,60}$/.test(status) ? `, ${status}` : '';
    throw new Error(`ElevenLabs não gerou a fala para ${people} (HTTP ${response.status}${reason}).`);
  }
  await writeFile(new URL(`${people}.mp3`, clips), Buffer.from(await response.arrayBuffer()));
}
console.log(`Falas do ElevenLabs salvas em ${fileURLToPath(clips)}.`);
