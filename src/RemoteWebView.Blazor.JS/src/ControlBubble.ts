// The control bubble: a small floating dock shown on every participant surface (the machine owner's and the remote
// technician's). It is the one place session control lives — handing control back and forth, and (added separately)
// the voice call controls. State is driven entirely by the relay: "participants:" tells us who is present and who
// holds control, "primaryChanged:" flips this connection between controlling and read-only. Nothing here is
// optimistic except the brief "Switching…" state, which reverts on a timeout if the relay never confirms.

export interface Participant {
    id: string;
    owner: boolean;
    primary: boolean;
}

// The headless voice client (VoiceClient.js) exposes this once it is on the page; the dock renders its controls.
export interface VoiceState {
    state: string;
    statusText: string;
    actLabel: string; canAct: boolean;
    endLabel: string; canEnd: boolean;
    muteLabel: string; canMute: boolean;
}

export interface RemoteVoiceApi {
    subscribe(cb: (s: VoiceState) => void): void;
    act(): void;
    hangup(): void;
    toggleMute(): void;
}

declare global {
    interface Window {
        RemoteVoiceHeadless?: boolean;
        RemoteVoice?: RemoteVoiceApi;
    }
}

interface BubbleOptions {
    myClientId: string;
    isOwner: boolean;
    // Ask the relay to make targetClientId the controller. The relay enforces who is allowed to.
    requestPrimary: (targetClientId: string) => void;
}

// How long to show "Switching…" before giving up and re-rendering from the last known state.
const SWITCH_TIMEOUT_MS = 8000;

let opts: BubbleOptions | null = null;
let participants: Participant[] = [];
let myIsPrimary = false;
let switching = false;
let switchTimer: ReturnType<typeof setTimeout> | undefined;

let root: HTMLElement | null = null;
let statusEl: HTMLElement | null = null;
let giveBtn: HTMLButtonElement | null = null;
let takeBtn: HTMLButtonElement | null = null;
let voiceSlot: HTMLElement | null = null;
let voiceStatus: HTMLElement | null = null;
let voiceAct: HTMLButtonElement | null = null;
let voiceMuteBtn: HTMLButtonElement | null = null;
let voiceEnd: HTMLButtonElement | null = null;
let voiceBound = false;

function other(): Participant | undefined {
    // The other control party. From the owner, that is the non-owner (the technician); from the technician, the owner.
    if (!opts) return undefined;
    if (opts.isOwner) return participants.find(p => p.id !== opts!.myClientId && !p.owner);
    return participants.find(p => p.owner && p.id !== opts!.myClientId);
}

function controller(): Participant | undefined {
    return participants.find(p => p.primary);
}

function build() {
    if (root) return;
    root = document.createElement('div');
    root.id = '__remote-control-bubble';
    root.setAttribute('role', 'group');
    root.setAttribute('aria-label', 'Session controls');
    root.style.cssText =
        'position:fixed;bottom:16px;right:16px;z-index:2147483646;display:flex;align-items:center;gap:8px;' +
        'padding:8px 12px;background:#1f2430;color:#f3f4f6;border:1px solid #39404d;border-radius:10px;' +
        'box-shadow:0 4px 14px rgba(0,0,0,.35);font:13px system-ui,-apple-system,sans-serif;user-select:none';

    statusEl = document.createElement('span');
    statusEl.setAttribute('role', 'status');
    statusEl.style.cssText = 'white-space:nowrap';

    giveBtn = button('Give control', () => {
        const target = other();
        if (target) startSwitch(target.id);
    });
    takeBtn = button('Take control', () => {
        if (opts) startSwitch(opts.myClientId); // owner reclaim: target is self
    });

    voiceSlot = document.createElement('span');
    voiceSlot.id = '__remote-control-voice-slot';
    voiceSlot.style.cssText = 'display:flex;align-items:center;gap:8px;border-left:1px solid #39404d;padding-left:8px';

    // Voice controls, driven by window.RemoteVoice; hidden until voice is present and reports state.
    voiceStatus = document.createElement('span');
    voiceStatus.style.cssText = 'white-space:nowrap;opacity:.85';
    voiceStatus.hidden = true;
    voiceAct = button('Voice call', () => window.RemoteVoice?.act());
    voiceMuteBtn = button('Mute', () => window.RemoteVoice?.toggleMute());
    voiceEnd = button('Hang up', () => window.RemoteVoice?.hangup());
    voiceAct.hidden = voiceMuteBtn.hidden = voiceEnd.hidden = true;
    voiceSlot.hidden = true; // shown once voice binds
    voiceSlot.append(voiceStatus, voiceAct, voiceMuteBtn, voiceEnd);

    root.append(statusEl, giveBtn, takeBtn, voiceSlot);
    document.body.appendChild(root);
}

function button(label: string, onClick: () => void): HTMLButtonElement {
    const b = document.createElement('button');
    b.type = 'button';
    b.textContent = label;
    b.style.cssText =
        'cursor:pointer;padding:5px 10px;border-radius:7px;border:1px solid #4b5563;background:#2b3240;' +
        'color:#f3f4f6;font:inherit';
    b.onclick = onClick;
    return b;
}

function startSwitch(targetClientId: string) {
    if (!opts || switching) return;
    switching = true;
    clearTimeout(switchTimer);
    switchTimer = setTimeout(() => {
        // The relay never confirmed — drop back to the last known state rather than hang on the spinner.
        switching = false;
        render();
    }, SWITCH_TIMEOUT_MS);
    render();
    opts.requestPrimary(targetClientId);
}

// The voice client loads separately (relay-injected on the controller, WPF-injected on the owner) and may appear
// before or after the dock, so bind on its ready event as well as checking for it now.
function bindVoice() {
    if (voiceBound || !window.RemoteVoice) return;
    voiceBound = true;
    if (voiceSlot) voiceSlot.hidden = false;
    window.RemoteVoice.subscribe(renderVoice);
}

function renderVoice(s: VoiceState) {
    if (!voiceStatus || !voiceAct || !voiceMuteBtn || !voiceEnd) return;
    voiceStatus.textContent = s.statusText || '';
    voiceStatus.hidden = !s.statusText;
    voiceAct.textContent = s.actLabel; voiceAct.hidden = !s.canAct;
    voiceMuteBtn.textContent = s.muteLabel; voiceMuteBtn.hidden = !s.canMute;
    voiceEnd.textContent = s.endLabel; voiceEnd.hidden = !s.canEnd;
}

export function initControlBubble(options: BubbleOptions) {
    opts = options;
    build();
    render();
    if (window.RemoteVoice) bindVoice();
    else window.addEventListener('remotevoiceready', bindVoice);
}

/** Relay roster update ("participants:"). */
export function setParticipants(next: Participant[]) {
    participants = next;
    // Any confirmed roster change clears a pending switch — the hand-off either happened or the roster moved on.
    if (switching) {
        switching = false;
        clearTimeout(switchTimer);
    }
    render();
}

/** This connection gained or lost control ("primaryChanged:"). */
export function setPrimary(isPrimary: boolean) {
    myIsPrimary = isPrimary;
    if (switching) {
        switching = false;
        clearTimeout(switchTimer);
    }
    render();
}

function render() {
    if (!root || !opts || !statusEl || !giveBtn || !takeBtn) return;

    if (switching) {
        statusEl.textContent = 'Switching control…';
        giveBtn.hidden = true;
        takeBtn.hidden = true;
        return;
    }

    const ctrl = controller();
    const iHaveControl = myIsPrimary;
    const theirName = opts.isOwner ? 'the technician' : 'the user';

    if (iHaveControl) {
        statusEl.textContent = 'You have control';
    } else if (ctrl) {
        statusEl.textContent = ctrl.owner ? 'The user has control' : 'The technician has control';
    } else {
        statusEl.textContent = 'No one has control';
    }

    // Give: only the current controller, and only when there is another party to hand off to.
    giveBtn.hidden = !(iHaveControl && other() !== undefined);
    giveBtn.textContent = `Give control to ${theirName}`;

    // Take: only the owner may unilaterally reclaim, and only when they do not already hold it.
    takeBtn.hidden = !(opts.isOwner && !iHaveControl);
}
