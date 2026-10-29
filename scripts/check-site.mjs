import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const read = path => readFileSync(resolve(root, path), 'utf8');
const html = read('index.html');
const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(m => m[1]);
assert.equal(new Set(ids).size, ids.length, 'Duplicate HTML IDs');
for (const [, target] of html.matchAll(/\b(?:href|src)="([^"]+)"/g)) {
  if (target.startsWith('#')) {
    assert.ok(ids.includes(target.slice(1)), `Missing anchor: ${target}`);
  } else if (!/^[a-z]+:/i.test(target)) {
    assert.ok(existsSync(resolve(root, target.split(/[?#]/)[0])), `Missing resource: ${target}`);
  }
}
for (const id of ['version-pill', 'panel-version', 'download-button', 'download-button-secondary', 'download-message', 'donation-button', 'donation-message']) {
  assert.ok(ids.includes(id), `Missing application element: ${id}`);
}
new vm.Script(read('app.js'), { filename: 'app.js' });
const context = { window: {} };
vm.runInNewContext(read('config.js'), context);
const config = context.window.LUFFY_CONFIG;
assert.match(config.version, /^\d+(\.\d+)*$/);
assert.equal(typeof config.beta, 'boolean');
for (const key of ['downloadUrl', 'donationUrl']) {
  assert.equal(typeof config[key], 'string');
  if (config[key]) assert.equal(new URL(config[key]).protocol, 'https:', `${key} must use HTTPS`);
}
assert.ok(config.downloadUrl, 'The public download must be configured');
for (const [, version] of html.matchAll(/v(\d+(?:\.\d+)+)/g)) {
  assert.equal(version, config.version, 'Static version label differs from config.js');
}

// Query release metadata rather than downloading the Windows executable.
const download = new URL(config.downloadUrl);
const match = download.pathname.match(/^\/([^/]+)\/([^/]+)\/releases\/download\/([^/]+)\/([^/]+)$/);
assert.equal(download.hostname, 'github.com', 'Expected a GitHub Release download');
assert.ok(match, 'Expected a direct release asset URL');
const [, owner, repo, tag, filename] = match;
assert.equal(decodeURIComponent(tag), config.version, 'Release tag and version differ');
const headers = { Accept: 'application/vnd.github+json', 'User-Agent': 'LUFFY-site-check' };
if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
const response = await fetch(`https://api.github.com/repos/${owner}/${repo}/releases/tags/${tag}`, {
  headers, signal: AbortSignal.timeout(20000)
});
assert.ok(response.ok, `Release metadata returned HTTP ${response.status}`);
const release = await response.json();
assert.equal(release.draft, false, 'Download points to a draft release');
assert.ok(release.assets.some(asset => asset.name === decodeURIComponent(filename) && asset.size > 0 && asset.browser_download_url === config.downloadUrl), 'Download asset is missing or empty');
console.log('Website resources, anchors, scripts, configuration and release asset passed.');
