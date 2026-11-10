const { test } = require('node:test');
const assert = require('node:assert/strict');
const ts = require('typescript');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/UnattendedControl.ts'), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 }
}).outputText;

const OWNER = 'owner';
// The owner's page as the relay's roster describes the session: who is there, and who has control.
function roster(inControl, ...remoteUsers) {
    return { myClientId: OWNER, isOwner: true, iHaveControl: inControl === OWNER, shareUrl: null, restarting: false,
        participants: [{ id: OWNER, owner: true, primary: inControl === OWNER },
            ...remoteUsers.map(id => ({ id, owner: false, primary: inControl === id }))] };
}

function setup(options = {}) {
    const timers = [];
    const requests = [];
    let push;
    const exports = {};
    vm.runInNewContext(source, { exports, setTimeout(callback) { timers.push(callback); }, Map, Set });
    exports.giveControlToWhoeverJoins({ subscribe: callback => { push = callback; }, requestPrimary: id => requests.push(id), ...options });
    return { requests, push, timers, tick() { timers.splice(0).forEach(callback => callback()); } };
}

test('control goes to the remote user who joins, and is not given again after the owner takes it back', () => {
    const page = setup();
    page.push(roster(OWNER));
    assert.deepEqual(page.requests, []);
    page.push(roster(OWNER, 'a'));
    assert.deepEqual(page.requests, ['a']);
    page.push(roster('a', 'a'));      // it took
    page.push(roster(OWNER, 'a'));    // the owner takes control back
    page.tick(); page.tick();
    assert.deepEqual(page.requests, ['a']);
});

test('a request that does not take is repeated until the roster shows the remote user in control', () => {
    const page = setup();
    page.push(roster(OWNER, 'a'));
    page.tick();                      // still the owner: ask again
    page.tick();
    assert.deepEqual(page.requests, ['a', 'a', 'a']);
    page.push(roster('a', 'a'));
    page.tick();
    page.push(roster(OWNER, 'a'));
    page.tick();
    assert.deepEqual(page.requests, ['a', 'a', 'a']);
});

test('it gives up after the configured number of requests', () => {
    const page = setup({ maxAttempts: 3 });
    page.push(roster(OWNER, 'a'));
    for (let i = 0; i < 10; i++) page.tick();
    assert.deepEqual(page.requests, ['a', 'a', 'a']);
    assert.equal(page.timers.length, 0);
});

test('roster updates between retries do not send extra requests', () => {
    const page = setup();
    page.push(roster(OWNER, 'a'));
    page.push(roster(OWNER, 'a'));
    page.push(roster(OWNER, 'a', 'viewer'));
    assert.deepEqual(page.requests, ['a']);
    assert.equal(page.timers.length, 1);
});

test('a remote user who leaves before the retry is not asked for again, and the next one to join is served', () => {
    const page = setup();
    page.push(roster(OWNER, 'a'));
    page.push(roster(OWNER));         // a left
    page.tick();
    assert.deepEqual(page.requests, ['a']);
    page.push(roster(OWNER, 'b'));
    assert.deepEqual(page.requests, ['a', 'b']);
});

test('a remote user who was given control by hand counts as served', () => {
    const page = setup();
    page.push(roster('a', 'a'));      // first seen already in control
    page.push(roster(OWNER, 'a'));
    page.tick();
    assert.deepEqual(page.requests, []);
});
