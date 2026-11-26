const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// The relay's voice client (served as /voice/client.js). On an app page it hides its own panel and leaves the controls
// to whoever listens to window.RemoteVoice; when nobody does, it has to say so.
const source = fs.readFileSync(path.join(__dirname, '../../RemoteWebViewService.Lib/VoiceClient.js'), 'utf8');

function element() {
    return { style: {}, dataset: {}, hidden: false, textContent: '', setAttribute() { }, removeAttribute() { }, append() { } };
}

// Runs the voice client on a page. headless: the app's script is on the page (RemoteWebView.ts sets the flag).
function load({ headless }) {
    const timers = [];
    const warnings = [];
    const window = { RemoteVoiceHeadless: headless, dispatchEvent() { } };
    const script = element();
    script.dataset = { session: 'session-1', key: 'key-1' };
    vm.runInNewContext(source, {
        window,
        document: { currentScript: script, createElement: element, body: element() },
        location: { host: 'relay.example' },
        WebSocket: class { static OPEN = 1; send() { } close() { } },
        Event: class { },
        addEventListener() { },
        setTimeout: (callback, ms) => { timers.push({ callback, ms }); return timers.length; },
        clearTimeout() { },
        console: { warn: text => warnings.push(text) },
    });
    return { window, warnings, runTimers: () => timers.splice(0).forEach(t => t.callback()) };
}

test('voice with nothing showing its controls is reported in the console', () => {
    const page = load({ headless: true });
    assert.deepEqual(page.warnings, [], 'the page gets time to draw its controls first');

    page.runTimers();

    assert.equal(page.warnings.length, 1);
    assert.match(page.warnings[0], /rbwv-session-controls/);
    assert.match(page.warnings[0], /window\.RemoteVoice/);
});

test('a page that listens for voice state is not reported', () => {
    const page = load({ headless: true });
    page.window.RemoteVoice.subscribe(() => { });

    page.runTimers();

    assert.deepEqual(page.warnings, []);
});

test('the bare voice page shows its own panel and is not reported', () => {
    const page = load({ headless: false });

    page.runTimers();

    assert.equal(page.window.RemoteVoice, undefined);
    assert.deepEqual(page.warnings, []);
});
