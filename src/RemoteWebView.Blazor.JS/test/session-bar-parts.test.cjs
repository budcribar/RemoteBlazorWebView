const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/SessionBarParts.ts'), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 }
}).outputText;
const bar = {};
vm.runInNewContext(source, { exports: bar });

const OWNER = 'owner', REMOTE = 'remote', VIEWER = 'viewer';
// A page's view of the session: who it is, who is there (in join order) and who has control.
function state(me, inControl, ...others) {
    const ids = [OWNER, ...others];
    return { myClientId: me, isOwner: me === OWNER, iHaveControl: inControl === me, shareUrl: 'https://relay/mirror/1', restarting: false,
        participants: ids.map(id => ({ id, owner: id === OWNER, primary: id === inControl })) };
}
const options = (extra = {}) => ({ switching: false, confirmingRestart: false, shareLink: false, restart: false, hostName: 'The host', remoteName: 'Remote user', ...extra });
const parts = (control, opts = options(), voice = null) => Array.from(bar.visibleParts(control, voice, opts));

test('a bar has something to show only with an owner in the session or a voice call', () => {
    const classic = { myClientId: 'a', isOwner: false, iHaveControl: true, shareUrl: null, restarting: false, participants: [{ id: 'a', owner: false, primary: true }] };
    assert.equal(bar.hasSomethingToShow(null, null), false);
    assert.equal(bar.hasSomethingToShow(classic, null), false);
    assert.equal(bar.hasSomethingToShow(classic, { statusText: '' }), true);
    assert.equal(bar.hasSomethingToShow(state(OWNER, OWNER), null), true);
});

test('the owner alone has nothing to give; with a remote user they can give, and take back afterwards', () => {
    assert.deepEqual(parts(state(OWNER, OWNER)), ['dot', 'status']);
    assert.deepEqual(parts(state(OWNER, OWNER, REMOTE)), ['dot', 'status', 'give']);
    assert.deepEqual(parts(state(OWNER, REMOTE, REMOTE)), ['dot', 'status', 'take']);
});

test('the remote user can only give control back, and a viewer has no buttons', () => {
    assert.deepEqual(parts(state(REMOTE, OWNER, REMOTE)), ['dot', 'status']);
    assert.deepEqual(parts(state(REMOTE, REMOTE, REMOTE)), ['dot', 'status', 'give']);
    assert.deepEqual(parts(state(VIEWER, OWNER, REMOTE, VIEWER)), ['dot', 'status']);
    assert.equal(bar.isViewer(state(VIEWER, OWNER, REMOTE, VIEWER)), true);
    assert.equal(bar.isViewer(state(REMOTE, OWNER, REMOTE, VIEWER)), false);
});

test('copy link and restart are shown only when the app asks, and only to those allowed', () => {
    const asked = options({ shareLink: true, restart: true });
    assert.deepEqual(parts(state(OWNER, REMOTE, REMOTE), asked), ['dot', 'status', 'take', 'copy-link', 'restart']); // the owner, without control
    assert.deepEqual(parts(state(REMOTE, REMOTE, REMOTE), asked), ['dot', 'status', 'give', 'restart']);             // the controller
    assert.deepEqual(parts(state(REMOTE, OWNER, REMOTE), asked), ['dot', 'status']);                                 // neither
    assert.deepEqual(parts(state(OWNER, OWNER, REMOTE), options({ restart: true, confirmingRestart: true })),
        ['dot', 'status', 'give', 'restart-question', 'restart-confirm', 'restart-cancel']);
});

test('while switching or restarting the buttons are gone', () => {
    assert.deepEqual(parts(state(OWNER, OWNER, REMOTE), options({ switching: true, shareLink: true })), ['dot', 'status', 'copy-link']);
    assert.deepEqual(parts({ ...state(OWNER, OWNER, REMOTE), restarting: true }, options({ shareLink: true, restart: true }), { statusText: 'In a call', canEnd: true }),
        ['dot', 'status']);
});

test('voice parts follow the call', () => {
    assert.deepEqual(parts(state(OWNER, OWNER), options(), { statusText: '', canAct: true, canMute: false, canEnd: false }), ['dot', 'status', 'voice-act']);
    assert.deepEqual(parts(state(OWNER, OWNER), options(), { statusText: 'In a call', canAct: false, canMute: true, canEnd: true }),
        ['dot', 'status', 'voice-status', 'voice-mute', 'voice-end']);
});

test('status says who has control, in the app\'s own words when it gives them', () => {
    const o = options();
    assert.equal(bar.statusText(state(OWNER, OWNER, REMOTE), o), 'You have control');
    assert.equal(bar.statusText(state(OWNER, REMOTE, REMOTE), o), 'Remote user has control');
    assert.equal(bar.statusText(state(REMOTE, OWNER, REMOTE), o), 'The host has control');
    assert.equal(bar.statusText(state(VIEWER, OWNER, REMOTE, VIEWER), o), 'View only');
    assert.equal(bar.statusText(state(OWNER, 'nobody', REMOTE), o), 'No one has control');
    assert.equal(bar.statusText(state(OWNER, OWNER, REMOTE), options({ switching: true })), 'Switching control…');
    assert.equal(bar.statusText({ ...state(OWNER, OWNER), restarting: true }, o), 'Restarting…');
    assert.equal(bar.statusText(state(REMOTE, OWNER, REMOTE), options({ hostName: 'The customer' })), 'The customer has control');
    const texts = { you: 'You are driving', viewOnly: 'Watching', none: 'Idle', switching: '…', restarting: 'Back soon' };
    assert.equal(bar.statusText(state(OWNER, OWNER, REMOTE), o, texts), 'You are driving');
    assert.equal(bar.statusText(state(VIEWER, OWNER, REMOTE, VIEWER), o, texts), 'Watching');
});

test('the dot counts everyone connected remotely', () => {
    assert.deepEqual({ ...bar.remoteConnections(state(OWNER, OWNER)) }, { count: 0, text: 'No one is connected remotely' });
    assert.deepEqual({ ...bar.remoteConnections(state(OWNER, OWNER, REMOTE)) }, { count: 1, text: 'Someone is connected remotely' });
    assert.deepEqual({ ...bar.remoteConnections(state(OWNER, OWNER, REMOTE, VIEWER)) }, { count: 2, text: '2 people are connected remotely' });
    assert.deepEqual({ ...bar.remoteConnections(state(REMOTE, OWNER, REMOTE)) }, { count: 1, text: 'You are connected remotely' });
});
