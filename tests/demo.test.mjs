import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const source = readFileSync(new URL('../demo.js', import.meta.url), 'utf8');
function mount(reduced = false) {
  const demo = { dataset: {} };
  const button = { hidden: true, attributes: {}, setAttribute(key, value) { this.attributes[key] = value; }, addEventListener(_, handler) { this.click = handler; } };
  const motion = { matches: reduced, addEventListener(_, handler) { this.change = handler; } };
  vm.runInNewContext(source, { document: { getElementById: id => id === 'peak-demo' ? demo : button }, window: { matchMedia: () => motion } });
  return { demo, button, motion };
}

test('animation can be paused and resumed with matching accessible button state', () => {
  const { demo, button } = mount();
  assert.equal(demo.dataset.animated, 'true');
  assert.equal(button.hidden, false);
  button.click();
  assert.equal(demo.dataset.paused, 'true');
  assert.equal(button.attributes['aria-pressed'], 'true');
  assert.equal(button.textContent, 'Play animation');
  button.click();
  assert.equal(demo.dataset.paused, 'false');
  assert.equal(button.attributes['aria-pressed'], 'false');
  assert.equal(button.textContent, 'Pause animation');
});

test('reduced-motion preference disables animation initially and when changed', () => {
  const { demo, button, motion } = mount(true);
  assert.equal(demo.dataset.animated, 'false');
  assert.equal(button.hidden, true);
  motion.matches = false;
  motion.change();
  assert.equal(demo.dataset.animated, 'true');
  assert.equal(button.hidden, false);
  motion.matches = true;
  motion.change();
  assert.equal(demo.dataset.animated, 'false');
  assert.equal(button.hidden, true);
});

test('pages without the illustration are unaffected', () => {
  assert.doesNotThrow(() => vm.runInNewContext(source, { document: { getElementById: () => null } }));
});
