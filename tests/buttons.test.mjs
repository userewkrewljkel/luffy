import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const script = readFileSync(new URL('../app.js', import.meta.url), 'utf8');
function render(config) {
  const elements = new Map();
  for (const id of ['version-pill', 'panel-version', 'download-button', 'download-button-secondary', 'download-message', 'donation-button', 'donation-message']) {
    const attrs = new Map([['aria-disabled', 'true']]);
    const classes = new Set(['is-unavailable']);
    elements.set(id, {
      textContent: '', attrs, classes, listeners: {},
      set href(value) { attrs.set('href', value); },
      setAttribute(key, value) { attrs.set(key, value); },
      removeAttribute(key) { attrs.delete(key); },
      classList: { add: value => classes.add(value), remove: value => classes.delete(value) },
      addEventListener(event, handler) { this.listeners[event] = handler; }
    });
  }
  vm.runInNewContext(script, { window: { LUFFY_CONFIG: config }, URL, document: { getElementById: id => elements.get(id) } });
  return elements;
}

test('valid download enables both buttons and updates version labels', () => {
  const url = 'https://example.com/LUFFY.exe';
  const elements = render({ version: '3.8.1', beta: true, downloadUrl: url });
  for (const id of ['download-button', 'download-button-secondary']) {
    assert.equal(elements.get(id).attrs.get('href'), url);
    assert.equal(elements.get(id).attrs.has('aria-disabled'), false);
    assert.equal(elements.get(id).classes.has('is-unavailable'), false);
  }
  assert.equal(elements.get('version-pill').textContent, 'v3.8.1 beta');
  assert.match(elements.get('download-message').textContent, /Direct download/);
});

test('missing and unsafe destinations remain disabled and prevent clicks', () => {
  for (const url of [undefined, '', 'http://example.com/file', 'javascript:alert(1)', '/file.exe', 'not a URL']) {
    const elements = render({ downloadUrl: url, donationUrl: url });
    for (const id of ['download-button', 'download-button-secondary', 'donation-button']) {
      const element = elements.get(id);
      assert.equal(element.attrs.has('href'), false);
      assert.equal(element.attrs.get('aria-disabled'), 'true');
      assert.equal(element.classes.has('is-unavailable'), true);
      let prevented = false;
      element.listeners.click({ preventDefault() { prevented = true; } });
      assert.equal(prevented, true);
    }
  }
});

test('donation opens safely in a new tab while an empty donation remains unavailable', () => {
  const elements = render({ version: '3.8.1', donationUrl: 'https://ko-fi.com/example' });
  assert.equal(elements.get('donation-button').target, '_blank');
  assert.equal(elements.get('donation-button').rel, 'noopener noreferrer');
  assert.equal(elements.get('donation-message').textContent, 'Optional. LUFFY stays free.');
  assert.equal(render({ donationUrl: '' }).get('donation-button').attrs.get('aria-disabled'), 'true');
});

test('invalid version falls back and stable releases omit beta', () => {
  assert.equal(render({ version: '<script>' }).get('version-pill').textContent, 'v3.8.1');
  assert.equal(render({ version: '4.0', beta: false }).get('panel-version').textContent, 'LUFFY v4.0 · Windows x64');
});
