// Per-browser session controls (control hand-off + voice) for a RemoteBlazorWebView session.
//
// Why this is not a Blazor component: the Blazor app runs once, in the host process, and every browser (the owner's
// window, the technician's) displays that same render, while the relay drops input from whoever does not hold control.
// A Blazor-rendered bar would therefore show the same text to everyone and be unclickable for the party without control.
// So each browser draws its own bar here, from its own state, and talks to the relay directly.
//
// Placement: if the app renders an empty placeholder - <div id="rbwv-session-controls"></div> - the bar is positioned
// exactly over it (the placeholder reserves the space), so it reads as part of the app's layout, e.g. in a header. The
// bar itself is attached to <body> so it is never trapped under the app's stacking contexts and always sits above the
// read-only overlay, keeping it clickable for the party without control. Without a placeholder it floats bottom-right.
//
// Options on the placeholder:  data-share-link="true"  -> show a "Copy link" button for the technician's join URL.
// Styling: rbwv-control-bar (container; plus rbwv-control-slotted or rbwv-control-floating), rbwv-control-status,
// rbwv-control-btn, rbwv-control-give / -take / -copy, rbwv-voice-status, rbwv-voice-act / -mute / -end. Built-in
// defaults use :where() (zero specificity), so any app rule overrides them.
//
// Buttons are added and removed by state (never hidden with CSS), so an app's global button styles cannot reveal one.

import { ControlState, Participant, VoiceState } from './RemoteControl';

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

const DEFAULT_STYLES = `
:where(.rbwv-control-bar){display:flex;align-items:center;gap:8px;white-space:nowrap;box-sizing:border-box}
:where(.rbwv-control-floating){bottom:16px;right:16px;padding:8px 12px;background:#1f2430;color:#f3f4f6;
  border:1px solid #39404d;border-radius:10px;box-shadow:0 4px 14px rgba(0,0,0,.35);font:13px system-ui,-apple-system,sans-serif}
:where(.rbwv-control-floating .rbwv-control-btn){cursor:pointer;padding:5px 10px;border-radius:7px;
  border:1px solid #4b5563;background:#2b3240;color:#f3f4f6;font:inherit}
:where(.rbwv-voice-status){opacity:.85}`;

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

    // The placeholder is rendered by Blazor, possibly after this runs, and may be re-created by a re-render.
    new MutationObserver(() => { if (findSlot()) render(); else schedulePlace(); })
        .observe(document.body, { childList: true, subtree: true });
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
    slot = found;
    if (slot && typeof ResizeObserver !== 'undefined') new ResizeObserver(schedulePlace).observe(slot);
    return true;
}

function other(): Participant | undefined {
    if (!control) return undefined;
    const me = control.myClientId;
    return control.isOwner
        ? control.participants.find(p => !p.owner && p.id !== me)
        : control.participants.find(p => p.owner && p.id !== me);
}

function controller(): Participant | undefined {
    return control?.participants.find(p => p.primary);
}

function showShareLink(): boolean {
    // Only the owner shares the join link (the placeholder opts in; floating always offers it).
    return !!control?.isOwner && (!slot || slot.dataset.shareLink === 'true');
}

function button(text: string, cls: string, onClick: () => void): HTMLButtonElement {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = `rbwv-control-btn ${cls}`;
    b.textContent = text;
    b.addEventListener('click', onClick);
    return b;
}

function span(text: string, cls: string): HTMLSpanElement {
    const s = document.createElement('span');
    s.className = cls;
    s.textContent = text;
    return s;
}

function render() {
    if (!bar) return;
    bar.replaceChildren();
    if (!control) { bar.style.display = 'none'; return; }

    const iHaveControl = control.iHaveControl;
    const ctrl = controller();
    const target = other();

    if (switching) {
        bar.append(span('Switching control…', 'rbwv-control-status'));
    } else {
        bar.append(span(
            iHaveControl ? 'You have control'
                : ctrl ? (ctrl.owner ? 'The user has control' : 'The technician has control')
                    : 'No one has control',
            'rbwv-control-status'));

        // Give: only the current controller, and only when there is another party to hand off to.
        if (iHaveControl && target)
            bar.append(button(`Give control to ${control.isOwner ? 'the technician' : 'the user'}`,
                'rbwv-control-give', () => startSwitch(target.id)));

        // Take: only the owner may reclaim, and only when they do not already hold control.
        if (control.isOwner && !iHaveControl)
            bar.append(button('Take control', 'rbwv-control-take', () => startSwitch(control!.myClientId)));
    }

    if (showShareLink() && control.shareUrl)
        bar.append(button(copied ? 'Copied' : 'Copy link', 'rbwv-control-copy', copyShareLink));

    if (voice) {
        if (voice.statusText) bar.append(span(voice.statusText, 'rbwv-voice-status'));
        if (voice.canAct) bar.append(button(voice.actLabel, 'rbwv-voice-act', () => window.RemoteVoice?.act()));
        if (voice.canMute) bar.append(button(voice.muteLabel, 'rbwv-voice-mute', () => window.RemoteVoice?.toggleMute()));
        if (voice.canEnd) bar.append(button(voice.endLabel, 'rbwv-voice-end', () => window.RemoteVoice?.hangup()));
    }

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

// Sit exactly over the placeholder (which reserves the bar's size), or float when there is none.
function place() {
    if (!bar || !control) return;
    if (slot && !slot.isConnected) slot = null;

    if (slot) {
        if (slot.getClientRects().length === 0) { bar.style.display = 'none'; return; } // placeholder hidden by the app
        bar.style.display = '';
        bar.classList.add('rbwv-control-slotted');
        bar.classList.remove('rbwv-control-floating');
        const w = `${bar.offsetWidth}px`, h = `${bar.offsetHeight}px`;
        if (slot.style.width !== w) slot.style.width = w;
        if (slot.style.height !== h) slot.style.height = h;
        if (!slot.style.flexShrink) slot.style.flexShrink = '0';
        const r = slot.getBoundingClientRect();
        bar.style.top = `${r.top}px`;
        bar.style.left = `${r.left}px`;
        bar.style.right = 'auto';
        bar.style.bottom = 'auto';
    } else {
        bar.style.display = '';
        bar.classList.add('rbwv-control-floating');
        bar.classList.remove('rbwv-control-slotted');
        bar.style.top = bar.style.left = bar.style.right = bar.style.bottom = '';
    }
}
