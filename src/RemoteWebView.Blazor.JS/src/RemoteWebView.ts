import { receiveMessage } from './IPC';
import { grpc } from "@improbable-eng/grpc-web";
import { BrowserIPC } from "./generated/webview_pb_service";
import { SendSequenceMessageRequest, SendMessageResponse, StringRequest, IdMessageRequest, ClientIdMessageRequest } from "./generated/webview_pb";
import { internalFunctions as navigationManagerFunctions } from '../web.js/src/Services/NavigationManager';
import { showErrorNotification } from '../web.js/src/BootErrors';
import { sendAttachPage} from '../web.js/src/Platform/WebView/WebViewIpcSender';

var sequenceNum: number = 1;
var clientId: string 
var isPrimary: boolean = true;

export function sendMessage(message: string) {
    var req = new SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    if (id == 'mirror') id = window.location.pathname.split('/')[2];
    req.setId(id);
    req.setClientid(clientId);
    req.setMessage(message);
    req.setSequence(sequenceNum++);
    req.setUrl(navigationManagerFunctions.getLocationHref())
    grpc.invoke(BrowserIPC.SendMessage, {
        request: req,
        host: window.location.origin,
        onMessage: (message: SendMessageResponse) => {
            if (!message.getSuccess()) {
                var error = `Client ${id} is unresponsive`
                console.log(error);
                showErrorNotification();
            }
        },
        onEnd: (code, msg, trailers) => {
            if (code == grpc.Code.OK) {
                console.log("sent:" + req.getSequence() + ":" + message + " clientId:" + clientId);
            } else {
                console.log("grpc error", code, msg, trailers);
                showErrorNotification();
            }
        }
    });
}

function setNotAllowedCursor(isPrimary:boolean): void {
    if (!isPrimary) {
        document.body.style.cursor = 'not-allowed';
      
        var a = document.getElementsByTagName('a');

        for (var idx = 0; idx < a.length; ++idx) {
            a[idx].style.cursor = 'not-allowed';
        }

        var b = document.getElementsByTagName('button');

        for (var idx = 0; idx < b.length; ++idx) {
            b[idx].style.cursor = 'not-allowed';
        }

        var s = document.getElementsByTagName('span');

        for (var idx = 0; idx < s.length; ++idx) {
            s[idx].style.cursor = 'not-allowed';
        }

        var d = document.getElementsByTagName('div');

        for (var idx = 0; idx < d.length; ++idx) {
            d[idx].style.cursor = 'not-allowed';
        }

        var i = document.getElementsByTagName('input');

        for (var idx = 0; idx < i.length; ++idx) {
            i[idx].style.cursor = 'not-allowed';
        }

        var t = document.getElementsByTagName('textarea');

        for (var idx = 0; idx < t.length; ++idx) {
            t[idx].style.cursor = 'not-allowed';
        }

        var l = document.getElementsByTagName('li');

        for (var idx = 0; idx < l.length; ++idx) {
            l[idx].style.cursor = 'not-allowed';
        }

        var sel = document.getElementsByTagName('select');

        for (var idx = 0; idx < sel.length; ++idx) {
            sel[idx].style.cursor = 'not-allowed';
        }
    }
}

export function initializeRemoteWebView() {
    (window.external as any).sendMessage = sendMessage;

    var message = new IdMessageRequest();
    var id = window.location.pathname.split('/')[1];
    if (id == 'mirror') id = window.location.pathname.split('/')[2];
    message.setId(id);

    grpc.invoke(BrowserIPC.GetClientId,
        {
            request: message,
            host: window.location.origin,
            onMessage: (message: ClientIdMessageRequest) => {
                //console.info("ClientId: " + message.getId());
                clientId = message.getClientid();
                isPrimary = message.getIsprimary();

                sendMessage("connected:");
                sendAttachPage(navigationManagerFunctions.getBaseURI(), navigationManagerFunctions.getLocationHref());

                setNotAllowedCursor(isPrimary);

            },
            onEnd: (code: grpc.Code, msg: string | undefined, trailers: grpc.Metadata) => {
                if (code == grpc.Code.OK) {
                    console.log("all ok:" + clientId)
                } else {
                    console.error("grpc error", code, msg, trailers);
                }
            }

        });

    grpc.invoke(BrowserIPC.ReceiveMessage,
        {
            request: message,
            host: window.location.origin,
            onMessage: (message: StringRequest) => {
                //console.info("Received: " + message.getRequest());
                receiveMessage(message.getRequest());
                setNotAllowedCursor(isPrimary);
            },
            onEnd: (code: grpc.Code, msg: string | undefined, trailers: grpc.Metadata) => {
                if (code == grpc.Code.OK) {
                    //console.log("all ok")
                } else {
                    console.error("grpc error", code, msg, trailers);
                }
            }
        } );

}