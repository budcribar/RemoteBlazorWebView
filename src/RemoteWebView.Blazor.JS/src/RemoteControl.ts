// Headless control-transfer state for RemoteBlazorWebView. The relay's control channel ("primaryChanged:" /
// "participants:") is decoded in RemoteWebView.ts and pushed here. This module keeps the current roster and whether this
// connection holds control, and publishes it to the host app as window.RemoteControl, so the built-in bar
// (SessionControls.ts) or an app's own UI can render give/take and name the other party. No DOM is drawn here.

export interface Participant {
    id: string;
    owner: boolean;
    primary: boolean;
}

/**
 * This page's stream from the session: connecting until the first message arrives, connected while messages can arrive,
 * reconnecting while a lost or restarting session is waited for, disconnected once nothing will come back (ConnectionLost.ts).
 * A page whose control marker still says in control is not in control while this is anything but connected.
 */
export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export interface ControlState {
    participants: Participant[];
    myClientId: string;
    isOwner: boolean;
    iHaveControl: boolean;
    shareUrl: string | null;
    /** The app is restarting (someone pressed Restart); this page waits for the new copy and comes back by itself. */
    restarting: boolean;
    connectionState: ConnectionState;
}

export interface RemoteControlApi {
    getState(): ControlState;
    // Subscribe to state changes; the callback fires immediately with the current state. Returns an unsubscribe function.
    subscribe(cb: (state: ControlState) => void): () => void;
    // Ask the relay to make targetClientId the controller. The relay enforces who is allowed to.
    requestPrimary(targetClientId: string): void;
    // Ask the relay to restart the app. The relay enforces who is allowed to (the controller, and the owner).
    requestRestart(): void;
}

// The headless voice client (VoiceClient.js) publishes this once it is on the page; the app's UI renders its controls.
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
        RemoteControl?: RemoteControlApi;
    }
}

interface InitOptions {
    myClientId: string;
    isOwner: boolean;
    shareUrl: string | null;
    requestPrimary: (targetClientId: string) => void;
    requestRestart: () => void;
}

let participants: Participant[] = [];
let myClientId = '';
let isOwner = false;
let iHaveControl = false;
let shareUrl: string | null = null;
let restarting = false;
let connectionState: ConnectionState = 'connecting';
let requestPrimaryImpl: (t: string) => void = () => { /* set in initControl */ };
let requestRestartImpl: () => void = () => { /* set in initControl */ };
const subscribers = new Set<(s: ControlState) => void>();

function snapshot(): ControlState {
    return { participants, myClientId, isOwner, iHaveControl, shareUrl, restarting, connectionState };
}

function notify() {
    const s = snapshot();
    subscribers.forEach(cb => { try { cb(s); } catch { /* a subscriber threw; keep the rest */ } });
}

/** Called once by RemoteWebView.ts after the connection is set up. Publishes window.RemoteControl. */
export function initControl(opts: InitOptions) {
    myClientId = opts.myClientId;
    isOwner = opts.isOwner;
    shareUrl = opts.shareUrl;
    requestPrimaryImpl = opts.requestPrimary;
    requestRestartImpl = opts.requestRestart;

    window.RemoteControl = {
        getState: snapshot,
        subscribe(cb) {
            subscribers.add(cb);
            try { cb(snapshot()); } catch { /* first push threw */ }
            return () => { subscribers.delete(cb); };
        },
        requestPrimary(target) { requestPrimaryImpl(target); },
        requestRestart() { requestRestartImpl(); },
    };
    window.dispatchEvent(new Event('remotecontrolready'));
    notify();
}

/** Relay roster update ("participants:"). */
export function setParticipants(next: Participant[]) {
    participants = next;
    notify();
}

/** This connection gained or lost control ("primaryChanged:" / roster reconciliation). */
export function setPrimary(value: boolean) {
    iHaveControl = value;
    notify();
}

/** The stream's state changed (RemoteWebView.ts). Subscribers hear only a change, since every message reports connected. */
export function setConnectionState(next: ConnectionState) {
    if (connectionState === next) return;
    connectionState = next;
    notify();
}

/** The relay announced that the app is restarting ("restarting:"). */
export function setRestarting() {
    restarting = true;
    notify();
}
