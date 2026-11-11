const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const compilerOptions = { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } };
const frameControlSource = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/FrameControl.ts'), 'utf8'), compilerOptions).outputText;
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/RemoteComponentFrames.ts'), 'utf8'), compilerOptions).outputText;

// Just enough DOM: elements with attributes and children, querySelectorAll for iframe[data-rbwv-src], and a MutationObserver the test drives.
class Element {
    constructor(tagName, attrs = {}) { this.tagName = tagName; this.nodeType = 1; this.attrs = new Map(Object.entries(attrs)); this.children = []; }
    getAttribute(n) { return this.attrs.has(n) ? this.attrs.get(n) : null; }
    hasAttribute(n) { return this.attrs.has(n); }
    setAttribute(n, v) { this.attrs.set(n, String(v)); (this.sets ??= []).push([n, String(v)]); }
    removeAttribute(n) { this.attrs.delete(n); }
    append(c) { this.children.push(c); return c; }
    all() { return this.children.flatMap(c => [c, ...c.all()]); }
    querySelectorAll(sel) { assert.equal(sel, 'iframe[data-rbwv-src]'); return this.all().filter(e => e.tagName === 'IFRAME' && e.hasAttribute('data-rbwv-src')); }
}

function load() {
    const observers = [];
    class MutationObserver { constructor(cb) { this.cb = cb; observers.push(this); } observe(root, opts) { this.root = root; this.opts = opts; } }
    const root = new Element('HTML');
    const document = {
        documentElement: root,
        querySelectorAll(sel) { return root.querySelectorAll(sel); },
    };
    const storage = new Map();
    const window = {
        location: { href: 'https://r/mirror/', hash: '', pathname: '/mirror/', search: '' },
        sessionStorage: {
            getItem(k) { return storage.has(k) ? storage.get(k) : null; },
            setItem(k, v) { storage.set(k, String(v)); },
            removeItem(k) { storage.delete(k); },
        },
        history: { replaceState() {} },
        addEventListener() {},
        postMessage() {},
        RemoteBlazor: {},
    };
    window.parent = window;

    const globals = { MutationObserver, Node: { ELEMENT_NODE: 1 }, document, window, URLSearchParams, crypto, TextEncoder };
    const frameControlModule = { exports: {} };
    vm.runInNewContext(frameControlSource, { ...globals, module: frameControlModule, exports: frameControlModule.exports });

    const module = { exports: {} };
    const requireFn = specifier => {
        if (specifier === './FrameControl') return frameControlModule.exports;
        throw new Error(`Unexpected module import: ${specifier}`);
    };
    vm.runInNewContext(source, { ...globals, module, exports: module.exports, require: requireFn });
    return { api: module.exports, root, observers, window, document };
}

const id = '9bfd9d43-0289-4a80-92d8-6e617729da12';
const frame = (mirror = true) => new Element('IFRAME', mirror
    ? { 'data-rbwv-src': `https://r/${id}`, 'data-rbwv-mirror-src': `https://r/mirror/${id}` }
    : { 'data-rbwv-src': `https://r/${id}` });

test('frames already on the mirror page get the mirror address, never the component page', () => {
    const { api, root } = load();
    const f = root.append(new Element('DIV')).append(frame());
    api.watchMirrorFrames(root);
    assert.equal(f.getAttribute('src'), `https://r/mirror/${id}`);
    assert.ok(!(f.sets ?? []).some(([, v]) => v === `https://r/${id}`));
});

test('frames rendered later are pointed at the mirror address', () => {
    const { api, root, observers } = load();
    api.watchMirrorFrames(root);
    const div = new Element('DIV');
    const f = div.append(frame());
    root.append(div);
    observers[0].cb([{ type: 'childList', addedNodes: [div] }]);
    assert.equal(f.getAttribute('src'), `https://r/mirror/${id}`);
});

test('a frame without a mirror address stays empty, even if src shows up', () => {
    const { api, root, observers } = load();
    const f = root.append(frame(false));
    api.watchMirrorFrames(root);
    assert.equal(f.getAttribute('src'), null);
    f.attrs.set('src', `https://r/${id}`);
    observers[0].cb([{ type: 'attributes', target: f }]);
    assert.equal(f.hasAttribute('src'), false);
});

test('other iframes are left alone', () => {
    const { api, root } = load();
    const other = root.append(new Element('IFRAME', { src: 'https://elsewhere/' }));
    api.watchMirrorFrames(root);
    assert.equal(other.getAttribute('src'), 'https://elsewhere/');
    assert.equal(other.sets, undefined);
});

test('repointMirrorFrames appends frameHash once frame identity is known', () => {
    const { api, root, window } = load();
    const f = root.append(frame());
    api.watchMirrorFrames(root);
    assert.equal(f.getAttribute('src'), `https://r/mirror/${id}`);
    window.RemoteBlazor.frameHash = '#rbwvFrameId=0123456789abcdef0123456789abcdef';
    api.repointMirrorFrames();
    assert.equal(f.getAttribute('src'), `https://r/mirror/${id}#rbwvFrameId=0123456789abcdef0123456789abcdef`);
});
