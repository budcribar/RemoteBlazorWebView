import { receiveMessage } from './IPC';
import { grpc } from "@improbable-eng/grpc-web";
import { BrowserIPC } from "./generated/webview_pb_service";
import { SendSequenceMessageRequest, SendMessageResponse, StringRequest, ReceiveMessageRequest } from "./generated/webview_pb";
import { internalFunctions as navigationManagerFunctions } from '../web.js/src/Services/NavigationManager';
import { showErrorNotification } from '../web.js/src/BootErrors';
import { sendAttachPage} from '../web.js/src/Platform/WebView/WebViewIpcSender';

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

export function sendMessage(message: string) {
    var req = new SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    if (id == 'mirror') {
        id = window.location.pathname.split('/')[2];
       
    }
    req.setId(id);
    req.setClientid(window.RemoteBlazor.clientId);
    req.setMessage(message);
    req.setSequence(window.RemoteBlazor.sequenceNum++);
    req.setUrl(navigationManagerFunctions.getLocationHref())
    req.setIsprimary(window.RemoteBlazor.isPrimary);

    grpc.invoke(BrowserIPC.SendMessage, {
        request: req,
        host: window.RemoteBlazor && window.RemoteBlazor.grpcHost ? window.RemoteBlazor.grpcHost : locationOrigin,
        onMessage: (message: SendMessageResponse) => {
            if (!message.getSuccess()) {
                var error = `Client ${id} is unresponsive`
                console.log(error);
                showErrorNotification();
            }
        },
        onEnd: (code, msg, trailers) => {
            if (code == grpc.Code.OK) {
                //console.log("sent:" + req.getSequence() + ":" + message + " window.RemoteBlazor.clientId:" + clientId);
            } else {
                console.log("grpc error", code, msg, trailers);
                showErrorNotification();
            }
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

    function preventInteraction(event) {
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

    window.RemoteBlazor.clientId = (window.crypto as any).randomUUID();

    sendMessage("connected:");
    sendAttachPage(navigationManagerFunctions.getBaseURI(), navigationManagerFunctions.getLocationHref());

    var message = new ReceiveMessageRequest();
    message.setId(id);
    message.setClientid(window.RemoteBlazor.clientId);
    message.setIsprimary(window.RemoteBlazor.isPrimary);

    grpc.invoke(BrowserIPC.ReceiveMessage,
        {
            request: message,
            host: window.RemoteBlazor && window.RemoteBlazor.grpcHost ? window.RemoteBlazor.grpcHost : locationOrigin,
            onMessage: (message: StringRequest) => {
                console.info("BrowserIPC.ReceiveMessage: " + message.getRequest());
                receiveMessage(message.getRequest());
                if (!window.RemoteBlazor.isPrimary)
                    makePageReadOnly();
            },
            onEnd: (code: grpc.Code, msg: string | undefined, trailers: grpc.Metadata) => {
                if (code == grpc.Code.OK) {
                    console.log("BrowserIPC.ReceiveMessage:onEnd:ok")
                } else {
                    console.error("grpc error", code, msg, trailers);
                }
            }
        } );

}