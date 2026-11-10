import { initializeScrollSync, receiveScroll } from './ScrollSync';
import { watchMirrorFrames } from './RemoteComponentFrames';
import { receiveMessage } from './IPC';
import { StatusCode } from "grpc-web";
import { BrowserIPCClient } from "./generated/WebviewServiceClientPb";
import { SendSequenceMessageRequest, SendMessageResponse, StringRequest, ReceiveMessageRequest, SetPrimaryRequest } from "./generated/webview_pb";
import { initControl, setParticipants, setPrimary, Participant } from './RemoteControl';
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
// In a sandboxed iframe without allow-same-origin the page's origin is opaque and location.origin is the string "null".
// The page was still loaded from the relay, so its scheme and host are the relay's.
const locationOrigin = window.location.origin === 'null'
    ? `${window.location.protocol}//${window.location.host}`
    : window.location.origin;

const client = new BrowserIPCClient(window.RemoteBlazor.grpcHost || locationOrigin);

export function sendMessage(message: string) {
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
        window.RemoteBlazor.isPrimary = false;
        makePageReadOnly();
        // Before any render batch arrives: remote component frames on a mirror load the relay's mirror view, never the component's page.
        watchMirrorFrames();
    }

    window.RemoteBlazor.clientId = crypto.randomUUID();
    // The machine owner's local control surface is launched with ?role=owner so the relay lets it reclaim control.
    window.RemoteBlazor.isOwner = new URLSearchParams(window.location.search).get('role') === 'owner';

    // "Copy link": an owner surface is itself the controlling /app page, so technicians join at the read-only /mirror
    // page and are handed control from there; anyone else shares the controller page.
    const shareUrl = window.RemoteBlazor.isOwner ? `${locationOrigin}/mirror/${id}` : `${locationOrigin}/app/${id}`;

    initControl({
        myClientId: window.RemoteBlazor.clientId,
        isOwner: window.RemoteBlazor.isOwner,
        shareUrl,
        requestPrimary,
    });

    // Each browser draws its own give/take/voice bar (into #rbwv-session-controls if the app provides one).
    if (document.body) initSessionControls();
    else document.addEventListener('DOMContentLoaded', () => initSessionControls(), { once: true });

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
    });

    stream.on('end', () => {
        console.log("BrowserIPC.ReceiveMessage: stream ended");
    });
}

interface RpcError {
    code: StatusCode;
    message: string;
}