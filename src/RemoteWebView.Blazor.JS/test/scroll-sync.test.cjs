const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/ScrollSync.ts'), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 }
}).outputText;

function setup(primary = true, withFrame = false) {
    class Element {
        constructor(tagName, id = '') { Object.assign(this, { tagName, id, children: [], parentElement: null,
            nodeType: 1, isConnected: true, scrollTop: 0, scrollLeft: 0 }); }
        append(child) { child.parentElement = this; this.children.push(child); return child; }
    }
    const root = new Element('HTML');
    const body = root.append(new Element('BODY'));
    const pane = body.append(new Element('DIV', 'workflow'));
    const timers = [];
    const listeners = new Map();
    function makeDocument(documentRoot) {
        const doc = { documentElement: documentRoot, scrollingElement: documentRoot,
            querySelectorAll() { return []; },
            addEventListener(name, callback, capture) {
                assert.equal(name, 'scroll'); assert.equal(capture, true); listeners.set(doc, callback);
            } };
        return doc;
    }
    const document = makeDocument(root);
    const iframeRoot = new Element('HTML');
    const iframeDocument = makeDocument(iframeRoot);
    if (withFrame) {
        const frame = body.append(new Element('IFRAME'));
        frame.contentDocument = iframeDocument;
        frame.addEventListener = () => {};
        document.querySelectorAll = () => [frame];
    }
    const exports = {};
    vm.runInNewContext(source, { exports, Element, document, MutationObserver: class { observe() {} },
        window: { RemoteBlazor: { isPrimary: primary }, setTimeout(callback) { timers.push(callback); } } });
    const sent = [];
    exports.initializeScrollSync(message => sent.push(message));
    return { root, pane, iframeRoot, sent, exports,
        scrollFrame() { listeners.get(iframeDocument)({ target: iframeDocument }); },
        scroll(target) { listeners.get(document)({ target: target || document }); },
        flush() { timers.splice(0).forEach(callback => callback()); } };
}

test('nested pane scrolls are captured and coalesced with absolute horizontal and vertical offsets', () => {
    const app = setup();
    app.pane.scrollTop = 100; app.scroll(app.pane);
    app.pane.scrollTop = 350; app.pane.scrollLeft = 40; app.scroll(app.pane);
    app.flush();
    assert.equal(app.sent.length, 1);
    const mirror = setup(false);
    assert.equal(mirror.exports.receiveScroll(app.sent[0]), true);
    assert.equal(mirror.pane.scrollTop, 350);
    assert.equal(mirror.pane.scrollLeft, 40);
});

test('document scrolls and returning to zero synchronize', () => {
    const app = setup(); const mirror = setup(false);
    app.root.scrollTop = 700; app.scroll(); app.flush();
    mirror.exports.receiveScroll(app.sent.at(-1)); assert.equal(mirror.root.scrollTop, 700);
    app.root.scrollTop = 0; app.scroll(); app.flush();
    mirror.exports.receiveScroll(app.sent.at(-1)); assert.equal(mirror.root.scrollTop, 0);
});

test('primary ignores relayed offsets and unrelated IPC passes through', () => {
    const app = setup();
    assert.equal(app.exports.receiveScroll('mirror-scroll:[{"frames":[],"path":[],"tag":"HTML","id":"","top":500,"left":0}]'), true);
    assert.equal(app.root.scrollTop, 0);
    assert.equal(app.exports.receiveScroll('__bwv:["RenderBatch"]'), false);
});

test('mirror tolerates scroll payloads without frames', () => {
    const mirror = setup(false);
    assert.equal(mirror.exports.receiveScroll('mirror-scroll:[{"path":[],"tag":"HTML","id":"","top":500,"left":25}]'), true);
    assert.equal(mirror.root.scrollTop, 500);
    assert.equal(mirror.root.scrollLeft, 25);
});

test('detached targets are removed and mismatched mirror elements are untouched', () => {
    const app = setup(); const mirror = setup(false);
    app.pane.scrollTop = 200; app.scroll(app.pane); app.flush();
    mirror.pane.id = 'different-page'; mirror.exports.receiveScroll(app.sent[0]);
    assert.equal(mirror.pane.scrollTop, 0);
    app.pane.isConnected = false; app.scroll(); app.flush();
    assert.equal(JSON.parse(app.sent.at(-1).slice('mirror-scroll:'.length)).length, 1);
});

test('same-origin report iframe scrolls synchronize independently of the parent page', () => {
    const app = setup(true, true); const mirror = setup(false, true);
    app.iframeRoot.scrollTop = 920; app.scrollFrame(); app.flush();
    mirror.exports.receiveScroll(app.sent[0]);
    assert.equal(mirror.iframeRoot.scrollTop, 920);
    assert.equal(mirror.root.scrollTop, 0);
    app.root.scrollTop = 300; app.scroll(); app.flush();
    mirror.exports.receiveScroll(app.sent[1]);
    assert.equal(mirror.iframeRoot.scrollTop, 920);
    assert.equal(mirror.root.scrollTop, 300);
});
