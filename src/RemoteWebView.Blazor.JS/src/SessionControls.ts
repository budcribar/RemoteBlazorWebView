// Per-browser session controls (control hand-off + voice) for a RemoteBlazorWebView session.
//
// Why this is not a Blazor component: the Blazor app runs once, in the host process, and every browser (the owner's
// window, the remote user's) displays that same render, while the relay drops input from whoever does not hold control.
// A Blazor-rendered bar would therefore show the same text to everyone and be unclickable for the party without control.
// So each browser draws its own bar here, from its own state, and talks to the relay directly.
//
// The bar is opt-in. Nothing is drawn unless the app renders a placeholder - <div id="rbwv-session-controls"></div> -
// and the session has something to control (an owner, so control can be handed over, or a voice call). The bar is
// positioned exactly over the placeholder (which reserves the space), so it reads as part of the app's layout, e.g. in a
// header. The bar itself is attached to <body> so it is never trapped under the app's stacking contexts and always sits
// above the read-only overlay, keeping it clickable for the party without control. An app without the placeholder has no
// bar: it can leave it out, or draw its own from window.RemoteControl / window.RemoteVoice.
//
// Options on the placeholder (built-in bar):
//   data-share-link="true"            -> show a "Copy link" button (owner only) for the join link
//   data-restart="true"               -> show a "Restart app" button (whoever has control, and the owner) that restarts
//                                        the app; every page then waits for the new copy and comes back by itself
//   data-host-name / data-remote-name -> what to call the two parties in "... has control" (default "The host" / "Remote user")
//
// The app's own markup: put a <template> inside the placeholder and mark its elements with data-rbwv="<part>". Each
// browser gets a copy of the template with only the parts that apply to it right now; the status text is filled in and
// the buttons are wired up. Everything else in the template (layout, wording, classes, icons) is the app's.
//   <div id="rbwv-session-controls">
//     <template>
//       <span data-rbwv="dot"></span> <strong data-rbwv="status"></strong>
//       <button data-rbwv="give">Hand over</button> <button data-rbwv="take">Take back</button>
//       <button data-rbwv="copy-link">Invite</button>
//     </template>
//   </div>
// Parts: dot, status, give, take, copy-link, restart, restart-question, restart-confirm, restart-cancel, voice-status,
// voice-act, voice-mute, voice-end (SessionBarParts.ts). On the status element, data-text-you / -view-only / -none /
// -switching / -restarting replace the wording. A template with "restart" but no "restart-confirm" restarts at once.
// (A hidden element with data-rbwv-template works like <template>, for a renderer that cannot emit one.)
//
// Styling: rbwv-control-bar (container; plus rbwv-control-slotted, and rbwv-control-custom for an app's template),
// rbwv-control-dot (plus rbwv-control-dot-on while anyone is connected remotely), rbwv-control-status,
// rbwv-control-btn, rbwv-control-give / -take / -copy / -restart / -confirm / -cancel, rbwv-control-question, rbwv-voice-status, rbwv-voice-act / -mute / -end. Built-in
// defaults use :where() (zero specificity), so any app rule overrides them. The same classes are put on a template's parts.
//
// Parts are added and removed by state (never hidden with CSS), so an app's global button styles cannot reveal one.

import { ControlState, VoiceState } from './RemoteControl';
import { BarOptions, BarPart, BAR_PARTS, DEFAULT_STATUS_TEXTS, StatusTexts, hasSomethingToShow, otherParty, remoteConnections, statusText, visibleParts } from './SessionBarParts';

const SLOT_ID = 'rbwv-session-controls';
const SWITCH_TIMEOUT_MS = 8000;

let bar: HTMLDivElement | null = null;
let slot: HTMLElement | null = null;
let control: ControlState | null = null;
let voice: VoiceState | null = null;
let switching = false;
let switchTimer: ReturnType<typeof setTimeout> | undefined;
let copied = false;
let copiedTimer: ReturnType<typeof setTimeout> | undefined;
let placeQueued = false;
let confirmingRestart = false;
let confirmTimer: ReturnType<typeof setTimeout> | undefined;
const CONFIRM_TIMEOUT_MS = 6000;

const DEFAULT_STYLES = `
:where(.rbwv-control-bar){display:flex;align-items:center;gap:8px;white-space:nowrap;box-sizing:border-box}
:where(.rbwv-voice-status){opacity:.85}
:where(.rbwv-control-dot){width:10px;height:10px;border-radius:50%;flex:none;background:#8b929c;opacity:.6}
:where(.rbwv-control-dot-on){background:#e5484d;opacity:1;box-shadow:0 0 0 3px rgba(229,72,77,.25)}`;

// The class each part carries, in the built-in bar and on an app's template elements alike.
const PART_CLASS: Record<BarPart, string> = {
    'dot': 'rbwv-control-dot',
    'status': 'rbwv-control-status',
    'give': 'rbwv-control-btn rbwv-control-give',
    'take': 'rbwv-control-btn rbwv-control-take',
    'copy-link': 'rbwv-control-btn rbwv-control-copy',
    'restart': 'rbwv-control-btn rbwv-control-restart',
    'restart-question': 'rbwv-control-question',
    'restart-confirm': 'rbwv-control-btn rbwv-control-confirm',
    'restart-cancel': 'rbwv-control-btn rbwv-control-cancel',
    'voice-status': 'rbwv-voice-status',
    'voice-act': 'rbwv-control-btn rbwv-voice-act',
    'voice-mute': 'rbwv-control-btn rbwv-voice-mute',
    'voice-end': 'rbwv-control-btn rbwv-voice-end',
};

/** Called once by RemoteWebView.ts after window.RemoteControl is published. */
export function initSessionControls() {
    if (bar || !window.RemoteControl) return;

    if (!document.getElementById('rbwv-session-controls-style')) {
        const style = document.createElement('style');
        style.id = 'rbwv-session-controls-style';
        style.textContent = DEFAULT_STYLES;
        document.head.appendChild(style);
    }

    bar = document.createElement('div');
    bar.className = 'rbwv-control-bar';
    bar.setAttribute('role', 'group');
    bar.setAttribute('aria-label', 'Session controls');
    bar.style.position = 'fixed';
    // Above the read-only overlay (z-index 10000) so the party without control can still use it.
    bar.style.zIndex = '2147483646';
    bar.style.display = 'none';
    document.body.appendChild(bar);

    window.RemoteControl.subscribe(s => {
        control = s;
        endSwitching();
        render();
    });

    const bindVoice = () => window.RemoteVoice?.subscribe(s => { voice = s; render(); });
    if (window.RemoteVoice) bindVoice();
    else window.addEventListener('remotevoiceready', bindVoice, { once: true });

    // The placeholder is rendered by Blazor, possibly after this runs, and may be re-created (or its template or its
    // options changed) by a re-render. The bar is not inside the placeholder, so drawing it never counts as a change to it.
    new MutationObserver(records => {
        if (findSlot() || records.some(r => !!slot && slot.contains(r.target))) render();
        else schedulePlace();
    }).observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['id', 'data-share-link', 'data-restart', 'data-host-name', 'data-remote-name'] });
    window.addEventListener('resize', schedulePlace);
    window.addEventListener('scroll', schedulePlace, true);
    if (typeof ResizeObserver !== 'undefined') new ResizeObserver(schedulePlace).observe(bar);

    findSlot();
    render();
}

// Returns true when the placeholder changed (appeared, was replaced, or went away).
function findSlot(): boolean {
    const found = document.getElementById(SLOT_ID);
    if (found === slot) return false;
    if (slot) { slot.style.width = ''; slot.style.height = ''; } // the old one no longer reserves the bar's space
    slot = found;
    if (slot && typeof ResizeObserver !== 'undefined') new ResizeObserver(schedulePlace).observe(slot);
    return true;
}

// The app's own markup for the bar: the children of a <template> (or of an element marked data-rbwv-template) directly
// inside the placeholder. A <template> written in HTML keeps its children in .content; one rendered by Blazor has them
// as ordinary child nodes, so both are read.
function templateNodes(): Node[] | null {
    if (!slot) return null;
    for (const child of Array.from(slot.children)) {
        if (child instanceof HTMLTemplateElement)
            return Array.from(child.content.childNodes.length > 0 ? child.content.childNodes : child.childNodes);
        if (child.hasAttribute('data-rbwv-template')) return Array.from(child.childNodes);
    }
    return null;
}

function options(custom: boolean): BarOptions {
    return {
        switching,
        confirmingRestart,
        // The built-in bar asks on the placeholder; an app's template asks by having the element.
        shareLink: custom || slot?.dataset.shareLink === 'true',
        restart: custom || slot?.dataset.restart === 'true',
        hostName: slot?.dataset.hostName?.trim() || 'The host',
        remoteName: slot?.dataset.remoteName?.trim() || 'Remote user',
    };
}

function askRestart() {
    confirmingRestart = true;
    clearTimeout(confirmTimer);
    confirmTimer = setTimeout(() => { confirmingRestart = false; render(); }, CONFIRM_TIMEOUT_MS);
    render();
}

function endConfirm() {
    confirmingRestart = false;
    clearTimeout(confirmTimer);
}

function restartNow() {
    endConfirm();
    render();
    window.RemoteControl?.requestRestart();
}

// What a click on a part does. Parts that are only text (dot, status, the question) have none.
function action(part: BarPart, confirmRestart: boolean): (() => void) | undefined {
    switch (part) {
        case 'give': return () => { const target = control && otherParty(control); if (target) startSwitch(target.id); };
        case 'take': return () => { if (control) startSwitch(control.myClientId); };
        case 'copy-link': return copyShareLink;
        case 'restart': return confirmRestart ? askRestart : restartNow;
        case 'restart-confirm': return restartNow;
        case 'restart-cancel': return () => { endConfirm(); render(); };
        case 'voice-act': return () => window.RemoteVoice?.act();
        case 'voice-mute': return () => window.RemoteVoice?.toggleMute();
        case 'voice-end': return () => window.RemoteVoice?.hangup();
        default: return undefined;
    }
}

// The built-in wording of a part. null: the part has no text of its own (the dot).
function defaultText(part: BarPart, status: string): string | null {
    switch (part) {
        case 'dot': return null;
        case 'status': return status;
        case 'give': return control?.isOwner ? 'Give control' : 'Give control back';
        case 'take': return 'Take control';
        case 'copy-link': return copied ? 'Copied' : 'Copy link';
        case 'restart': return 'Restart app';
        case 'restart-question': return 'Restart the app?';
        case 'restart-confirm': return 'Restart';
        case 'restart-cancel': return 'Cancel';
        case 'voice-status': return voice?.statusText ?? '';
        case 'voice-act': return voice?.actLabel ?? '';
        case 'voice-mute': return voice?.muteLabel ?? '';
        case 'voice-end': return voice?.endLabel ?? '';
    }
}

function setDot(dot: HTMLElement) {
    const remote = remoteConnections(control);
    dot.classList.toggle('rbwv-control-dot-on', remote.count > 0);
    dot.title = remote.text;
    dot.setAttribute('role', 'img');
    dot.setAttribute('aria-label', remote.text);
}

// The built-in bar: one element per visible part, in order.
function renderBuiltIn(parts: BarPart[], status: string) {
    for (const part of parts) {
        const act = action(part, true);
        const el = document.createElement(act ? 'button' : 'span');
        if (el instanceof HTMLButtonElement) el.type = 'button';
        el.className = PART_CLASS[part];
        const text = defaultText(part, status);
        if (text !== null) el.textContent = text;
        if (part === 'dot') setDot(el);
        if (act) el.addEventListener('click', act);
        bar!.append(el);
    }
}

function statusTexts(el: HTMLElement): StatusTexts {
    const d = el.dataset;
    return {
        you: d.textYou ?? DEFAULT_STATUS_TEXTS.you,
        viewOnly: d.textViewOnly ?? DEFAULT_STATUS_TEXTS.viewOnly,
        none: d.textNone ?? DEFAULT_STATUS_TEXTS.none,
        switching: d.textSwitching ?? DEFAULT_STATUS_TEXTS.switching,
        restarting: d.textRestarting ?? DEFAULT_STATUS_TEXTS.restarting,
    };
}

// The app's template: a copy per browser, with the parts that do not apply removed, the state filled in and the
// buttons wired. Elements that are not parts are the app's and are copied as they are.
function renderTemplate(nodes: Node[], parts: BarPart[], opts: BarOptions) {
    const copy = document.createDocumentFragment();
    for (const node of nodes) copy.append(node.cloneNode(true));
    const confirmRestart = !!copy.querySelector('[data-rbwv="restart-confirm"]');

    for (const el of Array.from(copy.querySelectorAll<HTMLElement>('[data-rbwv]'))) {
        const part = el.dataset.rbwv as BarPart;
        if (!BAR_PARTS.includes(part)) continue; // not one of ours: leave it alone
        // Without a confirm step in the template, the question never applies and Restart stays while it would be asked.
        const shown = parts.includes(part) || (!confirmRestart && part === 'restart' && parts.includes('restart-confirm'));
        if (!shown) { el.remove(); continue; }

        el.classList.add(...PART_CLASS[part].split(' '));
        if (part === 'dot') setDot(el);
        else if (part === 'status') el.textContent = statusText(control!, opts, statusTexts(el));
        else if (part === 'copy-link') {
            // "Copied" for a moment: the app's own wording (data-copied-text), or ours when the button is plain text.
            el.classList.toggle('rbwv-control-copied', copied);
            if (copied && (el.dataset.copiedText !== undefined || el.childElementCount === 0)) el.textContent = el.dataset.copiedText ?? 'Copied';
        }
        // The voice wording changes with the call (Call / Answer, Mute / Unmute), so it is always ours.
        else if (part === 'voice-status' || part === 'voice-act' || part === 'voice-mute' || part === 'voice-end') el.textContent = defaultText(part, '') ?? '';
        // Anything else keeps the app's wording; an element left empty gets ours.
        else if (el.childNodes.length === 0) el.textContent = defaultText(part, '') ?? '';

        const act = action(part, confirmRestart);
        if (act) el.addEventListener('click', e => { e.preventDefault(); act(); });
    }
    bar!.append(copy);
}

function render() {
    if (!bar) return;
    bar.replaceChildren();
    findSlot();
    // No placeholder, no bar; and nothing to show in a session with no owner and no voice call.
    if (!control || !slot || !hasSomethingToShow(control, voice)) { hide(); return; }

    const template = templateNodes();
    const opts = options(template !== null);
    const parts = visibleParts(control, voice, opts);
    if (confirmingRestart && !parts.includes('restart-confirm')) endConfirm(); // lost control while the question was showing

    bar.classList.toggle('rbwv-control-custom', template !== null);
    if (template) renderTemplate(template, parts, opts);
    else renderBuiltIn(parts, statusText(control, opts));

    schedulePlace();
}

function startSwitch(targetClientId: string) {
    if (switching || !window.RemoteControl) return;
    switching = true;
    clearTimeout(switchTimer);
    // The relay never confirmed: drop back to the last known state rather than spin forever.
    switchTimer = setTimeout(() => { switching = false; render(); }, SWITCH_TIMEOUT_MS);
    render();
    window.RemoteControl.requestPrimary(targetClientId);
}

function endSwitching() {
    if (!switching) return;
    switching = false;
    clearTimeout(switchTimer);
}

async function copyShareLink() {
    const url = control?.shareUrl;
    if (!url) return;
    let ok = false;
    try {
        if (navigator.clipboard) { await navigator.clipboard.writeText(url); ok = true; }
    } catch { /* fall back below */ }
    if (!ok) {
        const t = document.createElement('textarea');
        t.value = url;
        t.style.position = 'fixed';
        t.style.opacity = '0';
        document.body.appendChild(t);
        t.select();
        try { ok = document.execCommand('copy'); } catch { /* nothing more to try */ }
        t.remove();
    }
    if (!ok) return;
    copied = true;
    clearTimeout(copiedTimer);
    copiedTimer = setTimeout(() => { copied = false; render(); }, 2000);
    render();
}

function schedulePlace() {
    if (placeQueued) return;
    placeQueued = true;
    requestAnimationFrame(() => { placeQueued = false; place(); });
}

// Nothing to show: no bar, and the placeholder takes no space.
function hide() {
    if (!bar) return;
    bar.style.display = 'none';
    if (slot) { slot.style.width = ''; slot.style.height = ''; }
}

// Sit exactly over the placeholder, which reserves the bar's size.
function place() {
    if (!bar || !control) return;
    if (findSlot()) { render(); return; } // the placeholder appeared, went away or was replaced: draw for the new one
    if (!slot || !hasSomethingToShow(control, voice)) { hide(); return; }
    if (slot.getClientRects().length === 0) { bar.style.display = 'none'; return; } // placeholder hidden by the app

    bar.style.display = '';
    bar.classList.add('rbwv-control-slotted');
    const w = `${bar.offsetWidth}px`, h = `${bar.offsetHeight}px`;
    if (slot.style.width !== w) slot.style.width = w;
    if (slot.style.height !== h) slot.style.height = h;
    if (!slot.style.flexShrink) slot.style.flexShrink = '0';
    const r = slot.getBoundingClientRect();
    bar.style.top = `${r.top}px`;
    bar.style.left = `${r.left}px`;
    bar.style.right = 'auto';
    bar.style.bottom = 'auto';
}
