// Walks every Players page at 1440 and 390 px wide in both colour modes against a running app and
// prints what a reader would trip over, one line per page and mode, details below it.
//
// Usage:
//   node tools/players-walk.cjs <base url> <screenshot folder> [name filter]
//   node tools/players-walk.cjs http://127.0.0.1:5400/ D:/wt/ref/shots34c
//   node tools/players-walk.cjs http://127.0.0.1:5400/ D:/wt/ref/shots34c rankings   (only pages whose name contains it)
//
// The app must answer without a login (the Sample environment: ASPNETCORE_ENVIRONMENT=Sample, see
// src/FAForever.Vault.Viewer/AGENTS.md "Checking the pages"); add the Development server's address as a
// fourth argument to walk dev/charts too.
// Needs playwright-core and a chromium: set PLAYWRIGHT_CORE (the folder of the package) and
// CHROMIUM (the executable) when they are not at the places below (npx cache, chromium-1223).
//
// Per page and mode it prints: the scroll width against the viewport (OVERFLOW when they differ), the widest
// element past the viewport, console errors, role="alert" cards, visible text with "undefined", "NaN" or
// "[object", headings and values painted in the colour of the page background (the text-base trap), and
// touch targets under 44 px at 390 (links, buttons, inputs, selects of the page, not the shared header and footer). Screenshots: <folder>/<page>-<width>-<mode>.png.
// Exit code 1 when anything was flagged.
const fs = require('fs');
const path = require('path');

const core = process.env.PLAYWRIGHT_CORE || 'C:/Users/wbwij/AppData/Local/npm-cache/_npx/9833c18b2d85bc59/node_modules/playwright-core';
const executablePath = process.env.CHROMIUM || 'C:/Users/wbwij/AppData/Local/ms-playwright/chromium-1223/chrome-win64/chrome.exe';
const { chromium } = require(core);

const [base, out, only, devBase] = process.argv.slice(2);
if (!base || !out) {
  console.error('usage: node tools/players-walk.cjs <base url> <screenshot folder> [name filter] [development base url]');
  process.exit(2);
}
fs.mkdirSync(out, { recursive: true });

const root = base.endsWith('/') ? base : base + '/';
const pages = [
  ['landing', 'players'],
  ['search', 'players?q=rustic'],
  ['search-none', 'players?q=zzzznothing'],
  ['overview', 'players/2049311'],
  ['rating', 'players/2049311?tab=rating'],
  ['matches', 'players/2049311?tab=matches'],
  ['army', 'players/2049311?tab=army'],
  ['unknown', 'players/999'],
  ['rankings', 'players/rankings'],
  ['rankings-name', 'players/rankings?q=rustic'],
  ['rankings-page-past-end', 'players/rankings?page=999'],
  ['rankings-bad', 'players/rankings?board=zzz&active=zz&page=abc'],
  ['leagues', 'players/leagues'],
  ['leagues-sub', 'players/leagues?subdivision=3002'],
  ['leagues-bad', 'players/leagues?league=zzz&season=99999&page=999'],
  ['compare', 'players/compare'],
  ['compare-one', 'players/compare?a=2049311'],
  ['compare-two', 'players/compare?a=2049311&b=100285'],
];
if (devBase) pages.push(['charts', 'dev/charts', devBase.endsWith('/') ? devBase : devBase + '/']);

const jobs = [];
for (const [name, p, b] of pages) {
  if (only && !name.includes(only)) continue;
  for (const w of [1440, 390]) for (const mode of ['dark', 'light']) jobs.push({ name, path: p, base: b || root, w, mode });
}

// Runs in the page: everything measured is returned as plain data.
function measure() {
  const vw = document.documentElement.clientWidth;
  let widest = null;
  for (const el of document.querySelectorAll('body *')) {
    const r = el.getBoundingClientRect();
    if (r.width > 0 && r.right > vw + 1 && (!widest || r.right > widest.right)) {
      widest = { right: Math.round(r.right), tag: el.tagName, cls: (el.className || '').toString().slice(0, 70), text: (el.textContent || '').trim().slice(0, 40) };
    }
  }
  const visible = el => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none' && !el.closest('.sr-only, [hidden]');
  };
  const text = document.body.innerText || '';
  const bad = [];
  for (const needle of ['undefined', 'NaN', '[object']) {
    const i = text.indexOf(needle);
    if (i >= 0) bad.push(`${needle}: ...${text.slice(Math.max(0, i - 25), i + 30).replace(/\s+/g, ' ')}...`);
  }
  const background = getComputedStyle(document.body).backgroundColor;
  const invisible = [];
  for (const el of document.querySelectorAll('h1, h2, h3, h4, [class*="font-semibold"], [class*="font-bold"], [class*="text-[1rem]"]')) {
    if (visible(el) && el.textContent.trim() && getComputedStyle(el).color === background) invisible.push(`${el.tagName} "${el.textContent.trim().slice(0, 40)}"`);
  }
  const small = [];
  // Touch targets matter on the phone; the shared header and footer are not the page's own.
  const checkTargets = vw <= 600;
  for (const el of checkTargets ? document.querySelectorAll('a[href], button, input, select, textarea, summary, [role="button"], [role="tab"]') : []) {
    if (el.closest('header, footer')) continue;
    if (!visible(el)) continue;
    if (el.matches('input[type="checkbox"], input[type="radio"]') && el.closest('label')) continue; // the label is the target
    const r = el.getBoundingClientRect();
    if (r.height < 43.5 || (!el.matches('a') && r.width < 43.5)) {
      small.push(`${el.tagName.toLowerCase()} ${Math.round(r.width)}x${Math.round(r.height)} "${(el.getAttribute('aria-label') || el.textContent || el.getAttribute('placeholder') || '').trim().replace(/\s+/g, ' ').slice(0, 36)}"`);
    }
  }
  return {
    sw: document.documentElement.scrollWidth, vw, widest,
    alerts: [...document.querySelectorAll('[role="alert"]')].map(e => e.textContent.trim().replace(/\s+/g, ' ').slice(0, 80)),
    bad, invisible, small,
  };
}

(async () => {
  const browser = await chromium.launch({ executablePath });
  let flagged = 0;
  for (const job of jobs) {
    const ctx = await browser.newContext({ viewport: { width: job.w, height: 900 } });
    await ctx.addInitScript(mode => { try { localStorage.setItem('faf-mode', mode); } catch (e) { } }, job.mode);
    const page = await ctx.newPage();
    const errors = [];
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text().slice(0, 140)); });
    page.on('pageerror', e => errors.push('pageerror: ' + e.message.slice(0, 140)));
    await page.goto(job.base + job.path, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    const info = await page.evaluate(measure);
    await page.screenshot({ path: path.join(out, `${job.name}-${job.w}-${job.mode}.png`), fullPage: true });

    const flags = [];
    if (info.sw !== info.vw) flags.push('OVERFLOW');
    if (info.alerts.length) flags.push('ALERT');
    if (errors.length) flags.push('ERRORS');
    if (info.bad.length) flags.push('BADTEXT');
    if (info.invisible.length) flags.push('INVISIBLE');
    if (info.small.length) flags.push(`SMALL(${info.small.length})`);
    if (flags.length) flagged++;
    console.log(`${job.name}-${job.w}-${job.mode}`.padEnd(36), `sw ${info.sw}/${info.vw}`.padEnd(14), flags.length ? flags.join(' ') : 'ok');
    if (info.sw !== info.vw && info.widest) console.log('    widest:', JSON.stringify(info.widest));
    for (const a of info.alerts) console.log('    alert:', a);
    for (const e of errors) console.log('    console:', e);
    for (const b of info.bad) console.log('    text:', b);
    for (const i of info.invisible) console.log('    invisible:', i);
    for (const s of info.small.slice(0, 12)) console.log('    small:', s);
    if (info.small.length > 12) console.log(`    small: ... and ${info.small.length - 12} more`);
    await ctx.close();
  }
  await browser.close();
  console.log(`\n${jobs.length} runs, ${flagged} with findings`);
  process.exit(flagged ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
