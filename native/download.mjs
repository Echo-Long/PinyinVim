import { createWriteStream } from 'node:fs';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
const [url, destination] = process.argv.slice(2);
if (!url || !destination || !url.startsWith('https://')) throw new Error('Expected an HTTPS URL and destination');
let response;
for (let attempt = 0; attempt < 3; attempt++) {
  response = await fetch(url, { headers: { 'User-Agent': 'PinyinVim runtime setup' }, signal: AbortSignal.timeout(120000) });
  if (response.ok || response.status < 500 || attempt === 2) break;
  await new Promise(resolve => setTimeout(resolve, 2000 * (attempt + 1)));
}
if (!response.ok) throw new Error(`Download failed: HTTP ${response.status}`);
await pipeline(Readable.fromWeb(response.body), createWriteStream(destination));
