import { readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { polyphonic, segment } from '../../../node_modules/pinyin-pro/dist/esm/index.mjs';
import dict2 from '../../../node_modules/pinyin-pro/dist/esm/data/dict2.mjs';
import dict3 from '../../../node_modules/pinyin-pro/dist/esm/data/dict3.mjs';
import dict4 from '../../../node_modules/pinyin-pro/dist/esm/data/dict4.mjs';
import dict5 from '../../../node_modules/pinyin-pro/dist/esm/data/dict5.mjs';

const out = fileURLToPath(new URL('.', import.meta.url));
const han = /^\p{Script=Han}+$/u;
const words = new Set([
  ...Object.keys(dict2), ...Object.keys(dict3), ...Object.keys(dict4), ...Object.keys(dict5),
  '拼音输入法', '中文分词', '文本对象', '代码编辑器', '原生语义', '首字母搜索',
  '拼音首字母', '操作符待命', '视觉模式', '寄存器', '宏录制', '点重复',
  '中文', '编辑', '测试', '输入', '分词', '首字母', '搜索', '词典', '定位', '边界', '常用', '句子',
]);
// jieba's MIT-licensed vocabulary supplies ordinary words; pinyin-pro's
// pronunciation dictionary alone is not a general segmentation dictionary.
const jieba = await readFile(new URL('../../vendor/jieba-dict.txt', import.meta.url), 'utf8');
for (const row of jieba.split(/\r?\n/)) {
  const [word, frequency] = row.split(' ');
  if (word && Number(frequency) >= 10 && [...word].length >= 2 && [...word].length <= 8 && han.test(word)) words.add(word);
}
const seedText = [
  '我喜欢学习汉语中文编辑测试拼音输入法',
  '代码编辑器支持操作符待命视觉模式寄存器宏录制点重复',
  '原生语义保持中文分词使用真实词典并且支持多个汉字词语',
  '首字母搜索拼音首字母定位文本对象连续移动',
  '这是一个用于测试中文词语边界和编辑动作的常用句子',
].join('');
for (const part of segment(seedText)) words.add(part.origin);
const orderedWords = [...words].filter((word) => han.test(word) && [...word].length >= 2)
  .sort((a, b) => [...b].length - [...a].length || a.localeCompare(b, 'zh-CN'));
await writeFile(resolve(out, 'chinese-words.txt'), `${orderedWords.join('\n')}\n`, 'utf8');

const initials = [];
for (let code = 0x3400; code <= 0x9fff; code++) {
  const character = String.fromCodePoint(code);
  const result = polyphonic(character, { type: 'array', toneType: 'none' })[0] ?? [];
  const letters = [...new Set(result.map((pinyin) => pinyin[0]).filter((letter) => /^[a-z]$/.test(letter)))];
  if (letters.length) initials.push(`${character}\t${letters.join('')}`);
}
await writeFile(resolve(out, 'chinese-initials.txt'), `${initials.join('\n')}\n`, 'utf8');
console.log(`wrote ${orderedWords.length} words and ${initials.length} Han initials`);
