const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const { webcrypto } = require('node:crypto');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/FrameControl.ts'), 'utf8'),
    { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;

// Which origins the control messages between a page and its remote component frames are taken from. A frame is
// sandboxed without allow-same-origin by default (its origin is "null"), but an app may set Sandbox to allow it, and
// the frame's page can be on another origin than the page around it.

const ID = '0123456789abcdef0123456789abcdef';

function run(window, document = { querySelectorAll: () => [], baseURI: window.location.href, referrer: '' }) {
    const module = { exports: {} };
    vm.runInNewContext(source, {
        module, exports: module.exports, window, document,
        crypto: webcrypto, TextEncoder, URL, URLSearchParams, Map, Promise,
    });
    return module.exports;
}

// A framed page. origin: what the browser calls the frame's origin ("null" when sandboxed); address: its location.
function frame({ origin, address, ancestors = [] }) {
    const asked = [];
    const parent = { postMessage() { } };
    const window = {
        origin,
        location: { origin: new URL(address).origin, href: address, hash: '', ancestorOrigins: ancestors },
        parent,
        RemoteControl: {
            getState: () => ({ iHaveControl: true, myClientId: 'me', participants: [{ id: ID }] }),
            subscribe() { },
            requestPrimary: id => asked.push(id),
        },
        addEventListener(type, handler) { if (type === 'message') window.onmessage = handler; },
    };
    run(window).initFrameControl();
    const send = fromOrigin => window.onmessage({ source: parent, origin: fromOrigin, data: { type: 'rbwv-parent-control', primaryId: ID } });
    return { asked, send };
}

test('a sandboxed frame takes control messages from a parent page on another origin', () => {
    // A sandboxed frame's location still shows its address; only window.origin is "null".
    const f = frame({ origin: 'null', address: 'https://relay.example/component/' });
    f.send('https://app.example');
    assert.deepEqual(f.asked, [ID]);
});

test('a frame allowed its own origin takes control messages from the parent page\'s origin', () => {
    const f = frame({ origin: 'https://relay.example', address: 'https://relay.example/component/', ancestors: ['https://app.example'] });
    f.send('https://app.example');
    assert.deepEqual(f.asked, [ID]);
});

test('a frame allowed its own origin ignores a message from any other origin', () => {
    const f = frame({ origin: 'https://relay.example', address: 'https://relay.example/component/', ancestors: ['https://app.example'] });
    f.send('https://elsewhere.example');
    assert.deepEqual(f.asked, []);
});

// The page around the frames: answers a frame's query with who should control the component.
async function page(frameSrc) {
    const contentWindow = { posted: [], postMessage(m) { this.posted.push(m); } };
    const frames = [{ src: frameSrc, contentWindow }];
    let subscriber;
    const window = {
        origin: 'https://app.example',
        location: { origin: 'https://app.example', href: 'https://app.example/', hash: '' },
        RemoteBlazor: { clientId: 'page-1', isOwner: false },
        RemoteControl: { subscribe: cb => { subscriber = cb; } },
        addEventListener(type, handler) { if (type === 'message') window.onmessage = handler; },
    };
    window.parent = window;
    const document = { querySelectorAll: () => frames, baseURI: 'https://app.example/', referrer: '' };
    const fc = run(window, document);
    await new Promise(resolve => fc.initParentFrameControl('page-key', resolve));
    subscriber({ participants: [{ id: 'tech', primary: true }] });
    await new Promise(resolve => setTimeout(resolve, 20)); // the frame id is a hash, worked out asynchronously
    contentWindow.posted.length = 0;
    const ask = fromOrigin => {
        window.onmessage({ source: contentWindow, origin: fromOrigin, data: { type: 'rbwv-control-query' } });
        return contentWindow.posted.length;
    };
    return { ask };
}

test('the page answers a sandboxed frame and a frame on its own address\'s origin', async () => {
    const p = await page('https://relay.example/component/');
    assert.equal(p.ask('null'), 1);
    assert.equal(p.ask('https://relay.example'), 2);
    assert.equal(p.ask('https://app.example'), 3);
});

test('the page does not answer a frame whose message comes from an origin other than its address', async () => {
    const p = await page('https://relay.example/component/');
    assert.equal(p.ask('https://elsewhere.example'), 0);
});
