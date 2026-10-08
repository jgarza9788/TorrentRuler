// Seeds a throwaway TorrentRuler instance so every page has something to show:
// completes first-run setup, logs in, imports the bundled example rules, and adds one
// instance of each source type plus a storage path. Run against an app started with
// TORRENTRULER_DATA_DIR pointing at an empty temp directory -- never a real install.
//
//   node seed.mjs [--base http://localhost:5199]
//
// Exports login() for shoot.mjs and ad-hoc verification scripts.
import { chromium } from 'playwright';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

export const USER = 'admin';
export const PASSWORD = process.env.TR_SEED_PASSWORD ?? 'TorrentRuler!2026';

const here = path.dirname(fileURLToPath(import.meta.url));
const examples = path.resolve(here, '../../examples/example-rules.json');

export function baseUrl() {
  const i = process.argv.indexOf('--base');
  return i > 0 ? process.argv[i + 1] : 'http://localhost:5199';
}

/** Logs in on `page` (completing setup first if the instance is fresh). */
export async function login(page, base = baseUrl()) {
  await page.goto(`${base}/`);
  if (page.url().includes('/Setup')) {
    await page.fill('#Input_Username', USER);
    await page.fill('#Input_Password', PASSWORD);
    await page.fill('#Input_ConfirmPassword', PASSWORD);
    await page.click('button[type=submit]');
    await page.waitForLoadState('networkidle');
  }
  if (page.url().includes('/Login')) {
    await page.fill('#Input_Username', USER);
    await page.fill('#Input_Password', PASSWORD);
    await page.click('button[type=submit]');
    await page.waitForLoadState('networkidle');
  }
  if (page.url().includes('/Login') || page.url().includes('/Setup')) {
    throw new Error(`Could not log in (still at ${page.url()})`);
  }
}

async function addInstance(page, base, { type, name, url }) {
  await page.goto(`${base}/Instances/Edit`);
  await page.selectOption('#Input_SourceType', type); // option values are SourceType enum names
  await page.fill('#Input_Name', name);
  await page.fill('#Input_BaseUrl', url);
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.waitForLoadState('networkidle');
}

async function seed() {
  const base = baseUrl();
  const browser = await chromium.launch();
  const page = await browser.newPage();
  await login(page, base);

  await page.goto(`${base}/Settings`);
  await page.selectOption('select[name=importKind]', 'rules');
  await page.selectOption('select[name=importFormat]', 'Json');
  await page.setInputFiles('input[name=importFile]', examples);
  await page.getByRole('button', { name: 'Import', exact: true }).click();
  await page.waitForLoadState('networkidle');

  // Dark is the app's main theme; "system" renders light in this Bootstrap build.
  await page.goto(`${base}/Settings`);
  await page.selectOption('#Input_Theme', 'dark');
  await page.getByRole('button', { name: 'Save settings' }).click();
  await page.waitForLoadState('networkidle');

  if (process.argv.includes('--instances')) {
    for (const i of [
      { type: 'Qbittorrent', name: 'qbt1', url: 'http://qbittorrent.invalid:8080' },
      { type: 'Jellyfin', name: 'jf1', url: 'http://jellyfin.invalid:8096' },
      { type: 'Jellystat', name: 'js1', url: 'http://jellystat.invalid:3000' },
    ]) {
      await addInstance(page, base, i);
    }

    // A storage path with real contents (this repo's docs folder) so usage and folder size show.
    await page.goto(`${base}/Instances/EditStoragePath`);
    await page.fill('#Input_Name', 'docs');
    await page.fill('#Input_Path', path.resolve(here, '../../docs'));
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await page.waitForLoadState('networkidle');
  }

  // A couple of runs so History and the dashboard have rows (they fail: the instances are fake).
  if (process.argv.includes('--runs')) {
    for (let n = 0; n < 2; n++) {
      await page.goto(`${base}/Rules`);
      await page.locator('form[action*="handler=RunNow"] button:visible').nth(n).click();
      await page.waitForLoadState('networkidle');
    }
  }

  await browser.close();
  console.log('seeded', base);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1])) {
  seed().catch((e) => { console.error(e); process.exit(1); });
}
