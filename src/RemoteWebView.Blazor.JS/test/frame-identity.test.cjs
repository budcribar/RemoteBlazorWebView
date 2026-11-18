const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const { webcrypto } = require('node:crypto');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/FrameControl.ts'), 'utf8'),
    { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;

// A frame's identity in its component's session: the id is public (other browsers work it out to hand control over),
// the key is the secret that shows the relay a request is the frame's own.

// Loads FrameControl on a page. framed: the page is inside a parent page's iframe.
function load({ hash = '', framed = false, owner = false, clientId = 'page-1' } = {}) {
    const window = {
        location: { hash },
        RemoteBlazor: { clientId, isOwner: owner },
        addEventListener() { },
    };
    window.parent = framed ? {} : window;
    const module = { exports: {} };
    vm.runInNewContext(source, {
        module, exports: module.exports, window, document: { querySelectorAll: () => [] },
        crypto: webcrypto, TextEncoder, URLSearchParams, Map, Promise,
    });
    return { window, ...module.exports };
}

const ID = '0123456789abcdef0123456789abcdef';
const KEY = 'a'.repeat(64);

test('a page hands its frames an id and a key, and the key is not the page\'s own', async () => {
    const page = load();
    await new Promise(resolve => page.initParentFrameControl('page-key', resolve));

    const given = new URLSearchParams(page.window.RemoteBlazor.frameHash.slice(1));
    assert.match(given.get('rbwvFrameId'), /^[0-9a-f]{32}$/);
    assert.match(given.get('rbwvFrameKey'), /^[0-9a-f]{64}$/);
    assert.notEqual(given.get('rbwvFrameKey'), 'page-key');
    assert.equal(given.get('rbwvOwner'), null);
});

test('the same page gives the same frame identity every time, so a frame drawn again is the same connection', async () => {
    const first = load(), second = load();
    await new Promise(resolve => first.initParentFrameControl('page-key', resolve));
    await new Promise(resolve => second.initParentFrameControl('page-key', resolve));

    assert.equal(first.window.RemoteBlazor.frameHash, second.window.RemoteBlazor.frameHash);
});

test('the frame key cannot be worked out from what other browsers know', async () => {
    // Other browsers know this page's clientId (the roster) and so its frames' ids; the key follows the page's key only.
    const mine = load(), another = load();
    await new Promise(resolve => mine.initParentFrameControl('page-key', resolve));
    await new Promise(resolve => another.initParentFrameControl('another-key', resolve));

    const a = new URLSearchParams(mine.window.RemoteBlazor.frameHash.slice(1));
    const b = new URLSearchParams(another.window.RemoteBlazor.frameHash.slice(1));
    assert.equal(a.get('rbwvFrameId'), b.get('rbwvFrameId'));
    assert.notEqual(a.get('rbwvFrameKey'), b.get('rbwvFrameKey'));
});

test('a page without a key hands out none, and the owner mark still follows', async () => {
    const page = load({ owner: true });
    await new Promise(resolve => page.initParentFrameControl('', resolve));

    assert.match(page.window.RemoteBlazor.frameHash, /^#rbwvFrameId=[0-9a-f]{32}&rbwvOwner=1$/);
});

test('a frame reads the id and key its parent gave it', () => {
    const frame = load({ framed: true, hash: `#rbwvFrameId=${ID}&rbwvFrameKey=${KEY}&rbwvOwner=1` });

    assert.deepEqual({ ...frame.readFrameIdentity() }, { clientId: ID, clientKey: KEY, owner: true });
});

test('a frame whose parent gave no key, or a malformed one, has none', () => {
    assert.equal(load({ framed: true, hash: `#rbwvFrameId=${ID}` }).readFrameIdentity().clientKey, null);
    assert.equal(load({ framed: true, hash: `#rbwvFrameId=${ID}&rbwvFrameKey=xyz` }).readFrameIdentity().clientKey, null);
});

test('a page that is not framed takes nothing from its address', () => {
    const page = load({ hash: `#rbwvFrameId=${ID}&rbwvFrameKey=${KEY}&rbwvOwner=1` });

    assert.deepEqual({ ...page.readFrameIdentity() }, { clientId: null, clientKey: null, owner: false });
});
