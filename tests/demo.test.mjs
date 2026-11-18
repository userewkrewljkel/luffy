import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
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

test('hero illustration keeps real desktop proportions and the tiny 40px meter', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  const illustration = html.split('<figure class="hero-figure peak-demo"')[1]?.split('</figure>')[0];
  assert.ok(illustration, 'Animated hero figure is present');
  assert.ok(illustration.includes('class="demo-desktop" x="35" y="33" width="690" height="388.125" viewBox="0 0 1920 1080"'));
  assert.ok(illustration.includes('id="demo-luffy-meter" transform="translate(1598 994)"'));
  assert.ok(illustration.includes('width="310" height="40"'));
  assert.ok(illustration.includes('id="demo-screen-warning" class="demo-peak" x="8" y="8" width="1904" height="1064"'));
  assert.ok(illustration.includes('class="demo-normal" x="140"'));
  assert.ok(illustration.includes('class="demo-peak" x="140"'));
  assert.ok(illustration.includes('id="demo-toggle"'));
});

test('uploaded Ableton example appears directly under the original animated hero', () => {
  const html = readFileSync(new URL('../index.html', import.meta.url), 'utf8');
  const hero = html.split('<section class="hero"')[1]?.split('</section>')[0];
  assert.ok(hero, 'Hero exists');
  const originalFigureEnd = hero.indexOf('</figure>');
  const exampleStart = hero.indexOf('<figure class="hero-example">');
  const exampleEnd = hero.indexOf('</figure>', exampleStart);
  assert.ok(exampleStart > originalFigureEnd, 'Example is below the animated illustration');
  assert.ok(exampleEnd > exampleStart, 'Example has a separate figure');
  assert.match(hero.slice(exampleStart, exampleEnd), /src="assets\/luffy-ableton-example\.png"/);
  assert.match(hero.slice(exampleStart, exampleEnd), /width="325" height="85"/);
  assert.match(hero.slice(exampleStart, exampleEnd), /<figcaption>Place LUFFY anywhere you like — here, above Ableton Live’s Control Bar.<\/figcaption>/);
  assert.match(hero.slice(0, originalFigureEnd), /id="demo-screen-warning"/);
  assert.match(hero.slice(0, originalFigureEnd), /id="demo-toggle"/);
  const asset = new URL('../assets/luffy-ableton-example.png', import.meta.url);
  assert.ok(existsSync(asset), 'Original screenshot asset exists');
  const png = readFileSync(asset);
  assert.equal(png.subarray(1,4).toString('ascii'), 'PNG');
  assert.equal(png.readUInt32BE(16), 325);
  assert.equal(png.readUInt32BE(20), 85);
});
