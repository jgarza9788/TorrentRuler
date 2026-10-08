// Captures every main page at several widths and checks none of them scrolls sideways.
//
//   node shoot.mjs --out ../../docs/screenshots/after [--widths 375,768,1280] [--save 375,1280] [--no-fail]
//
// Saves <page>-<width>.png for the --save widths (default 375 and 1280); every width in
// --widths is checked for horizontal overflow. Exits 1 listing page@width on overflow
// unless --no-fail. Expects a seeded instance (see seed.mjs) at --base.
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import { baseUrl, login } from './seed.mjs';

const arg = (name, fallback) => {
  const i = process.argv.indexOf(name);
  return i > 0 ? process.argv[i + 1] : fallback;
};
const list = (s) => s.split(',').map(Number);

const out = path.resolve(arg('--out', 'out'));
const widths = list(arg('--widths', '375,768,1280'));
const save = new Set(list(arg('--save', '375,1280')));
const fail = !process.argv.includes('--no-fail');
const base = baseUrl();

const pages = [
  ['dashboard', '/'],
  ['rules', '/Rules'],
  ['rule-edit', '/Rules/Edit?id=1'],
  ['instances', '/Instances'],
  ['history', '/History'],
  ['sandbox', '/Sandbox'],
  ['settings', '/Settings'],
];

fs.mkdirSync(out, { recursive: true });
const browser = await chromium.launch();
const overflow = [];

for (const width of widths) {
  const context = await browser.newContext({ viewport: { width, height: 900 }, colorScheme: 'dark' });
  const page = await context.newPage();
  await login(page, base);
  for (const [name, url] of pages) {
    await page.goto(base + url);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(300); // CodeMirror / Alpine settle
    const { scrollWidth, innerWidth } = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    if (scrollWidth > innerWidth) {
      overflow.push(`${name}@${width}: scrollWidth ${scrollWidth} > ${innerWidth}`);
    }
    if (save.has(width)) {
      await page.screenshot({ path: path.join(out, `${name}-${width}.png`), fullPage: true });
    }
  }
  await context.close();
}
await browser.close();

const report = overflow.length ? overflow.join('\n') : 'no horizontal overflow';
fs.writeFileSync(path.join(out, 'overflow.txt'), report + '\n');
console.log(report);
if (fail && overflow.length) process.exit(1);
