import { initializeScrollSync, receiveScroll } from './ScrollSync';
import { watchMirrorFrames, repointMirrorFrames } from './RemoteComponentFrames';
import { initFrameControl, initParentFrameControl, isFramed, readFrameIdentity } from './FrameControl';
import { receiveMessage } from './IPC';
import { StatusCode } from "grpc-web";
import { BrowserIPCClient } from "./generated/WebviewServiceClientPb";
import { SendSequenceMessageRequest, SendMessageResponse, StringRequest, ReceiveMessageRequest, SetPrimaryRequest, RestartRequest } from "./generated/webview_pb";
import { initControl, setParticipants, setPrimary, setRestarting, Participant } from './RemoteControl';
import { initSessionControls } from './SessionControls';
import { Empty } from "google-protobuf/google/protobuf/empty_pb";
import { internalFunctions as navigationManagerFunctions } from '../Web.JS/src/Services/NavigationManager';
import { showErrorNotification } from '../Web.JS/src/BootErrors';
import { sendAttachPage } from '../Web.JS/src/Platform/WebView/WebViewIpcSender';

interface RemoteBlazor {
    sequenceNum: number;
    clientId: string;
    isPrimary: boolean;
    isOwner: boolean;
    /** This page was opened as a read-only /mirror page. Fixed for the page's life, unlike isPrimary, which follows control. */
    isMirrorPage: boolean;
    /** Appended to the address of remote component frames on this page: this page's identity in the component's session. */
    frameHash?: string;
    grpcHost?: string;
}

declare global {
    interface Window {
        RemoteBlazor: RemoteBlazor;
    }
}

// Set before the deferred voice client runs, so it exposes window.RemoteVoice instead of drawing its own panel.
// (The Window.RemoteVoiceHeadless/RemoteVoice globals are declared in RemoteControl.ts, imported below.)
window.RemoteVoiceHeadless = true;

if (!window.RemoteBlazor) {
    window.RemoteBlazor = {} as RemoteBlazor;
}

window.RemoteBlazor.sequenceNum = 1;
window.RemoteBlazor.clientId = '';
window.RemoteBlazor.isPrimary = true;
window.RemoteBlazor.isOwner = false;
window.RemoteBlazor.isMirrorPage = false;
// In a sandboxed iframe without allow-same-origin the page's origin is opaque and location.origin is the string "null".
// The page was still loaded from the relay, so its scheme and host are the relay's.
const locationOrigin = window.location.origin === 'null'
    ? `${window.location.protocol}//${window.location.host}`
    : window.location.origin;

const client = new BrowserIPCClient(window.RemoteBlazor.grpcHost || locationOrigin);

export function sendMessage(message: string) {
    // Only the controlling page talks to the app. A page without control applies render batches too, and Blazor
    // acknowledges each one, but the relay ignores those messages - and they must not use up message numbers: one
    // still on its way when this page is given control would arrive with a number from before the restart at 1, be
    // held by the relay, and later be forwarded in place of the real message with that number.
    if (!window.RemoteBlazor.isPrimary) return;

    const req = new SendSequenceMessageRequest();
    let id = window.location.pathname.split('/')[1];
    if (id == 'mirror') {
        id = window.location.pathname.split('/')[2];
    }
    req.setId(id);
    req.setClientid(window.RemoteBlazor.clientId);
    req.setMessage(message);
    req.setSequence(window.RemoteBlazor.sequenceNum++);
    req.setUrl(navigationManagerFunctions.getLocationHref());
    req.setIsprimary(window.RemoteBlazor.isPrimary);

    client.sendMessage(req, {}, (err: RpcError | null, response: SendMessageResponse) => {
        if (err) {
            console.log("grpc error", err.code, err.message);
            showErrorNotification();
            return;
        }

        if (!response.getSuccess()) {
            const error = `Client ${id} is unresponsive`;
            console.log(error);
            showErrorNotification();
        } else {
            //console.log("sent:" + req.getSequence() + ":" + message + " window.RemoteBlazor.clientId:" + window.RemoteBlazor.clientId);
        }
    });
}

// Scroll sync must start once, when this connection is (or becomes) the controller — not twice if control
// returns to it later.
let scrollSyncStarted = false;
function ensureScrollSync() {
    if (!scrollSyncStarted) {
        initializeScrollSync(sendMessage);
        scrollSyncStarted = true;
    }
}

// Control transfer: ask the relay to hand control to targetClientId. The relay decides whether it is allowed.
export function requestPrimary(targetClientId: string) {
    const req = new SetPrimaryRequest();
    let id = window.location.pathname.split('/')[1];
    if (id == 'mirror') id = window.location.pathname.split('/')[2];
    req.setId(id);
    req.setClientid(window.RemoteBlazor.clientId);
    req.setTarget(targetClientId);
    client.setPrimary(req, {}, (err: RpcError | null, _response: Empty) => {
        if (err) console.log("SetPrimary grpc error", err.code, err.message);
    });
}

// Session id from this page's address: /<id>/... for the app's page, /mirror/<id> for a read-only page.
function sessionId(): string {
    const parts = window.location.pathname.split('/');
    return parts[1] == 'mirror' ? parts[2] : parts[1];
}

// Restart: ask the relay to restart the app. The relay decides whether this connection may.
export function requestRestart() {
    const req = new RestartRequest();
    req.setId(sessionId());
    req.setClientid(window.RemoteBlazor.clientId);
    client.restart(req, {}, (err: RpcError | null, _response: Empty) => {
        if (err) console.log("Restart grpc error", err.code, err.message);
    });
}

// The app is restarting. The old copy's session ends (this page's stream closes); the new copy starts under the same
// id a few seconds later. Wait for it, then load this page again: a /mirror page reloads itself, the app's page goes
// back through /app/<id> (loading /<id> again would itself count as a refresh and restart the new copy).
let restartPending = false;
let comingBack = false;
const COME_BACK_FIRST_TRY_MS = 2000;
const COME_BACK_RETRY_MS = 2000;
const COME_BACK_GIVE_UP_MS = 120000;
const RESTART_NO_END_MS = 45000;

function comeBackAfterRestart() {
    if (comingBack || isFramed()) return;
    // Inside the app's own window (WebView2) there is nothing to come back to: that window belongs to the copy that is
    // shutting down, and the new copy opens its own. Loading /app here could take the new copy's page from it.
    if ((window as unknown as { chrome?: { webview?: unknown } }).chrome?.webview) return;
    comingBack = true;
    const id = sessionId();
    const target = window.RemoteBlazor.isMirrorPage
        ? window.location.href
        : `${locationOrigin}/app/${id}${window.location.search}`;
    const started = Date.now();

    const attempt = async () => {
        if (!window.RemoteBlazor.isMirrorPage) { window.location.replace(target); return; } // /app waits for the new copy itself
        try {
            // A mirror page is only served once the new copy is up and its own page is open; until then the relay
            // answers with an error, which would leave this browser on a dead page.
            const response = await fetch(target, { cache: 'no-store', credentials: 'same-origin' });
            if (response.ok) { window.location.reload(); return; }
        } catch { /* relay not reachable right now; try again */ }
        if (Date.now() - started > COME_BACK_GIVE_UP_MS) { showErrorNotification(); return; }
        setTimeout(attempt, COME_BACK_RETRY_MS);
    };
    setTimeout(attempt, COME_BACK_FIRST_TRY_MS);
}

// Apply a controller change to this page: take local input back when we gain control, lock it when we lose it,
// and publish control state for the app's UI. Driven by both 'primaryChanged:' and the 'participants:' roster.
function applyPrimary(nowPrimary: boolean) {
    if (nowPrimary && !window.RemoteBlazor.isPrimary) {
        // Gained control: realign the outgoing sequence with the relay's reset, then take local input back.
        window.RemoteBlazor.sequenceNum = 1;
        window.RemoteBlazor.isPrimary = true;
        makePageEditable();
        ensureScrollSync();
    } else if (!nowPrimary && window.RemoteBlazor.isPrimary) {
        window.RemoteBlazor.isPrimary = false;
        makePageReadOnly();
    }
    setPrimary(window.RemoteBlazor.isPrimary);
}

// Intercept the relay's control-channel messages before they reach the Blazor IPC parser.
function receiveControl(message: string): boolean {
    if (message.startsWith('primaryChanged:')) {
        const primaryId = message.substring('primaryChanged:'.length);
        applyPrimary(primaryId === window.RemoteBlazor.clientId);
        return true;
    }
    if (message === 'restarting:') {
        restartPending = true;
        setRestarting();
        makePageReadOnly();
        // Normally the stream closing starts the wait; this covers a stream that never reports its end.
        setTimeout(comeBackAfterRestart, RESTART_NO_END_MS);
        return true;
    }
    if (message.startsWith('participants:')) {
        try {
            const roster = JSON.parse(message.substring('participants:'.length)) as Participant[];
            setParticipants(roster);
            // The roster is authoritative for who holds control: reconcile this page with it, so a just-joined
            // connection that still defaults to primary (the /app page) is locked while the owner holds control.
            const me = roster.find(p => p.id === window.RemoteBlazor.clientId);
            if (me) applyPrimary(me.primary);
        } catch { /* ignore a malformed roster */ }
        return true;
    }
    return false;
}

// Module scope so makePageEditable can remove exactly these listeners.
function preventInteraction(event: Event) {
    event.preventDefault();
    event.stopPropagation();
}

function makePageReadOnly() {
    if (document.getElementById('__remote-mirror-overlay')) return;
    // Create the overlay element
    const overlay = document.createElement('div');
    overlay.id = '__remote-mirror-overlay';
    overlay.style.position = 'fixed';
    overlay.style.top = '0px';
    overlay.style.left = '0px';
    overlay.style.width = '100%';
    overlay.style.height = '100%';
    // Below the control bubble (which stays clickable) but above page content.
    overlay.style.zIndex = '10000';
    overlay.style.background = 'transparent';
    overlay.style.cursor = 'not-allowed';
    // Append the overlay to the body
    document.body.appendChild(overlay);

    // Block local wheel/touch input while allowing synchronized document scrolling.
    overlay.addEventListener('wheel', event => event.preventDefault(), { passive: false });
    overlay.addEventListener('touchmove', event => event.preventDefault(), { passive: false });

    // Bind events for keyboard
    window.addEventListener('keydown', preventInteraction, true);
    window.addEventListener('keyup', preventInteraction, true);

    document.body.style.userSelect = 'none';
    document.body.setAttribute('oncontextmenu', 'return false;');
}

// Inverse of makePageReadOnly: restore local input when this connection becomes the controller.
function makePageEditable() {
    document.getElementById('__remote-mirror-overlay')?.remove();
    window.removeEventListener('keydown', preventInteraction, true);
    window.removeEventListener('keyup', preventInteraction, true);
    document.body.style.userSelect = '';
    document.body.removeAttribute('oncontextmenu');
}

export function initializeRemoteWebView() {
    (window.external as any).sendMessage = sendMessage;

    const pathParts = window.location.pathname.split('/');
    let id = pathParts[1];
    const startedAsMirror = id === 'mirror';
    if (startedAsMirror) {
        id = pathParts[2];
        window.RemoteBlazor.isMirrorPage = true;
        window.RemoteBlazor.isPrimary = false;
        makePageReadOnly();
        // Before any render batch arrives: remote component frames on a mirror load the relay's mirror view, never the component's page.
        watchMirrorFrames();
    }

    // A remote component's frame takes the identity its parent page gave it, so control of the component can follow
    // control of the app (FrameControl.ts); any other page picks its own.
    const frameIdentity = readFrameIdentity();
    window.RemoteBlazor.clientId = frameIdentity.clientId ?? crypto.randomUUID();
    // The machine owner's local control surface is launched with ?role=owner so the relay lets it reclaim control
    // (and a frame on that surface is the owner of its component's session).
    window.RemoteBlazor.isOwner = frameIdentity.owner || new URLSearchParams(window.location.search).get('role') === 'owner';

    // "Copy link": an owner surface is itself the controlling /app page, so remote users join at the read-only /mirror
    // page and are handed control from there; anyone else shares the controller page.
    const shareUrl = window.RemoteBlazor.isOwner ? `${locationOrigin}/mirror/${id}` : `${locationOrigin}/app/${id}`;

    initControl({
        myClientId: window.RemoteBlazor.clientId,
        isOwner: window.RemoteBlazor.isOwner,
        shareUrl,
        requestPrimary,
        requestRestart,
    });

    // Each browser draws its own give/take/voice bar (into #rbwv-session-controls if the app provides one). Not inside
    // a remote component's frame: its control simply follows the page it is on.
    if (!isFramed()) {
        if (document.body) initSessionControls();
        else document.addEventListener('DOMContentLoaded', () => initSessionControls(), { once: true });
    }
    // Unattended sessions (?control=remote on the owner's page): the remote user gets control on joining, with
    // nobody at the machine handing it over. Once per remote user, so the owner can take control back and keep it.
    if (window.RemoteBlazor.isOwner && !isFramed() && new URLSearchParams(window.location.search).get('control') === 'remote') {
        const given = new Set<string>();
        window.RemoteControl?.subscribe(state => {
            if (!state.iHaveControl) return;
            const remoteUser = state.participants.find(p => !p.owner && p.id !== state.myClientId);
            if (!remoteUser || given.has(remoteUser.id)) return;
            given.add(remoteUser.id);
            requestPrimary(remoteUser.id);
        });
    }
    initParentFrameControl(() => { if (window.RemoteBlazor.isMirrorPage) repointMirrorFrames(); });
    initFrameControl();

    // The connect handshake belongs to the controller (/app) page, which attaches the shared page context. A /mirror page
    // must not send it: once a mirror can hold control (the owner's local view starts in control, and promotion resets
    // the sequence) these would reach the host, where AttachPage rebuilds the page for everyone - and with the
    // /mirror/<id> address, outside the app's base, NavigationManager throws and the host crashes - and "connected:"
    // would raise Connected again.
    if (!startedAsMirror) {
        sendMessage("connected:");
        sendAttachPage(navigationManagerFunctions.getBaseURI(), navigationManagerFunctions.getLocationHref());
    }

    const message = new ReceiveMessageRequest();
    message.setId(id);
    message.setClientid(window.RemoteBlazor.clientId);
    message.setIsprimary(window.RemoteBlazor.isPrimary);
    message.setIsowner(window.RemoteBlazor.isOwner);

    if (window.RemoteBlazor.isPrimary) ensureScrollSync();

    const stream = client.receiveMessage(message, {});

    stream.on('data', (response: StringRequest) => {
        console.info("BrowserIPC.ReceiveMessage: " + response.getRequest());
        if (receiveControl(response.getRequest())) return;
        if (receiveScroll(response.getRequest())) return;
        receiveMessage(response.getRequest());
        if (!window.RemoteBlazor.isPrimary)
            makePageReadOnly();
    });

    stream.on('error', (err: RpcError) => {
        console.error("grpc error", err.code, err.message);
        if (restartPending) comeBackAfterRestart();
    });

    stream.on('end', () => {
        console.log("BrowserIPC.ReceiveMessage: stream ended");
        if (restartPending) comeBackAfterRestart();
    });
}

interface RpcError {
    code: StatusCode;
    message: string;
}