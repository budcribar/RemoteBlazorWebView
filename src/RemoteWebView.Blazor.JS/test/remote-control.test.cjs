const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/RemoteControl.ts'), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 }
}).outputText;

// A fresh copy of the module on a page of its own: window.RemoteControl is what the app (RPCD's dialogs.js) reads.
function setup() {
    const window = { dispatchEvent() {} };
    const exports = {};
    vm.runInNewContext(source, { exports, window, Event: class { constructor(type) { this.type = type; } }, Set });
    exports.initControl({ myClientId: 'me', isOwner: false, shareUrl: null, requestPrimary() {}, requestRestart() {} });
    return { exports, control: window.RemoteControl };
}

test('the stream state starts as connecting and is published with control', () => {
    const page = setup();
    assert.equal(page.control.getState().connectionState, 'connecting');
    page.exports.setPrimary(true);
    page.exports.setConnectionState('connected');
    const state = page.control.getState();
    assert.equal(state.connectionState, 'connected');
    assert.equal(state.iHaveControl, true);
});

test('a lost stream reads as reconnecting, then disconnected, while control keeps its last value', () => {
    const page = setup();
    const heard = [];
    page.control.subscribe(state => heard.push(state.connectionState));
    page.exports.setPrimary(true);
    page.exports.setConnectionState('connected');
    page.exports.setConnectionState('reconnecting');
    page.exports.setConnectionState('disconnected');
    assert.deepEqual(heard, ['connecting', 'connecting', 'connected', 'reconnecting', 'disconnected']);
    // The page does not know it lost control: the connection state is what says it cannot act.
    assert.equal(page.control.getState().iHaveControl, true);
});

test('every message reports connected, but subscribers hear only a change', () => {
    const page = setup();
    let calls = 0;
    page.control.subscribe(() => calls++);
    page.exports.setConnectionState('connected');
    page.exports.setConnectionState('connected');
    page.exports.setConnectionState('connected');
    assert.equal(calls, 2); // the first push on subscribing, and the change to connected
});
