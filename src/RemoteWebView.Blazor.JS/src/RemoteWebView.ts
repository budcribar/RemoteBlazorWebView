import { receiveMessage } from './IPC';
import { StatusCode } from "grpc-web";
import { BrowserIPCClient } from "./generated/WebviewServiceClientPb";
import { SendSequenceMessageRequest, SendMessageResponse, StringRequest, ReceiveMessageRequest } from "./generated/webview_pb";
import { internalFunctions as navigationManagerFunctions } from '../web.js/src/Services/NavigationManager';
import { showErrorNotification } from '../web.js/src/BootErrors';
import { sendAttachPage } from '../web.js/src/Platform/WebView/WebViewIpcSender';

interface RemoteBlazor {
    sequenceNum: number;
    clientId: string;
    isPrimary: boolean;
    grpcHost?: string;
}

declare global {
    interface Window {
        RemoteBlazor: RemoteBlazor;
    }
}

if (!window.RemoteBlazor) {
    window.RemoteBlazor = {} as RemoteBlazor;
}

window.RemoteBlazor.sequenceNum = 1;
window.RemoteBlazor.clientId = '';
window.RemoteBlazor.isPrimary = true;
const locationOrigin = window.location.origin;

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

function makePageReadOnly() {
    // Create the overlay element
    var overlay = document.createElement('div');
    overlay.style.position = 'fixed';
    overlay.style.top = '0px';
    overlay.style.left = '0px';
    overlay.style.width = '100%';
    overlay.style.height = '100%';
    overlay.style.zIndex = '10000';
    overlay.style.background = 'transparent';
    overlay.style.cursor = 'not-allowed';
    // Append the overlay to the body
    document.body.appendChild(overlay);

    // Disable scrolling
    document.body.style.overflow = 'hidden';

    // Bind events for keyboard
    window.addEventListener('keydown', preventInteraction, true);
    window.addEventListener('keyup', preventInteraction, true);

    function preventInteraction(event: Event) {
        event.preventDefault();
        event.stopPropagation();
    }

    document.body.style.userSelect = 'none';
    document.body.setAttribute('oncontextmenu', 'return false;');
}

export function initializeRemoteWebView() {
    (window.external as any).sendMessage = sendMessage;

    const pathParts = window.location.pathname.split('/');
    let id = pathParts[1];
    if (id === 'mirror') {
        id = pathParts[2];
        window.RemoteBlazor.isPrimary = false;
        makePageReadOnly();
    }

    window.RemoteBlazor.clientId = crypto.randomUUID();

    sendMessage("connected:");
    sendAttachPage(navigationManagerFunctions.getBaseURI(), navigationManagerFunctions.getLocationHref());

    const message = new ReceiveMessageRequest();
    message.setId(id);
    message.setClientid(window.RemoteBlazor.clientId);
    message.setIsprimary(window.RemoteBlazor.isPrimary);

    const stream = client.receiveMessage(message, {});

    stream.on('data', (response: StringRequest) => {
        console.info("BrowserIPC.ReceiveMessage: " + response.getRequest());
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