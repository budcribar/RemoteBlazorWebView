const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/ConnectionLost.ts'), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 }
}).outputText;

// A fresh copy of the module per test (it remembers that it has been shown), a page with a body, and timers the test runs by hand.
function setup() {
    const elements = [];
    const document = {
        body: { appendChild(element) { elements.push(element); } },
        getElementById(id) { return elements.find(e => e.id === id) || null; },
        createElement() { return { id: '', style: {}, textContent: '', attributes: {}, setAttribute(k, v) { this.attributes[k] = v; } }; },
    };
    const timers = [];
    let now = 0;
    const exports = {};
    vm.runInNewContext(source, { exports, document, setTimeout(callback, ms) { timers.push({ callback, ms }); },
        Date: { now: () => now }, fetch: () => { throw new Error('the test supplies its own probe'); } });
    return { exports, elements, timers,
        text() { return elements[0] && elements[0].textContent; },
        advance(ms) { now += ms; },
        async runTimer() { const timer = timers.shift(); await timer.callback(); return timer.ms; } };
}

test('a page that cannot come back only says the connection is lost', () => {
    const page = setup();
    let reloads = 0;
    page.exports.connectionLost({ canReturn: false, address: '/x/', reload: () => reloads++, probe: async () => { throw new Error('must not probe'); } });
    assert.equal(page.text(), page.exports.TEXT_LOST);
    assert.equal(page.elements[0].id, page.exports.NOTICE_ID);
    assert.equal(page.elements[0].attributes.role, 'status');
    assert.equal(page.timers.length, 0);
    assert.equal(reloads, 0);
});

test('a mirror page waits until the session answers, then reloads once', async () => {
    const page = setup();
    const answers = [false, false, true];
    const probed = [];
    let reloads = 0;
    page.exports.connectionLost({ canReturn: true, address: '/mirror/abc', reload: () => reloads++,
        probe: async address => { probed.push(address); return answers.shift(); }, firstTryMs: 10, retryMs: 20 });
    assert.equal(page.text(), page.exports.TEXT_WAITING);
    assert.equal(await page.runTimer(), 10);
    assert.equal(await page.runTimer(), 20);
    assert.equal(reloads, 0);
    assert.equal(await page.runTimer(), 20);
    assert.equal(reloads, 1);
    assert.deepEqual(probed, ['/mirror/abc', '/mirror/abc', '/mirror/abc']);
    assert.equal(page.timers.length, 0); // nothing scheduled after the reload
});

test('a session that never answers ends the wait with "The session has ended."', async () => {
    const page = setup();
    let reloads = 0;
    page.exports.connectionLost({ canReturn: true, address: '/mirror/abc', reload: () => reloads++, probe: async () => false, giveUpMs: 1000 });
    await page.runTimer();
    assert.equal(page.text(), page.exports.TEXT_WAITING);
    page.advance(1001);
    await page.runTimer();
    assert.equal(page.text(), page.exports.TEXT_ENDED);
    assert.equal(page.timers.length, 0);
    assert.equal(reloads, 0);
    assert.equal(page.elements.length, 1); // the same notice, not a second one
});

test('a second report of the lost connection changes nothing', () => {
    const page = setup();
    page.exports.connectionLost({ canReturn: true, address: '/mirror/abc', reload() {}, probe: async () => false });
    page.exports.connectionLost({ canReturn: false, address: '/mirror/abc', reload() {} });
    assert.equal(page.text(), page.exports.TEXT_WAITING);
    assert.equal(page.timers.length, 1);
});
