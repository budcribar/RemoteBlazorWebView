import { receiveMessage } from './IPC';
import { grpc } from "@improbable-eng/grpc-web";
import { BrowserIPC } from "./generated/webview_pb_service";
import { SendSequenceMessageRequest, SendMessageResponse, StringRequest, IdMessageRequest } from "./generated/webview_pb";
import { internalFunctions as navigationManagerFunctions } from '../web.js/src/Services/NavigationManager';
import { showErrorNotification } from '../web.js/src/BootErrors';

var sequenceNum: number = 1;

export function sendMessage(message: string) {
    var req = new SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    req.setId(id);
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
                //console.log("sent:" + req.getSequence() + ":" + message);
            } else {
                console.log("grpc error", code, msg, trailers);
                showErrorNotification();
            }
        }
    });
}

export function initializeRemoteWebView() {
    var message = new IdMessageRequest();
    var id = window.location.pathname.split('/')[1];
    message.setId(id);

    grpc.invoke(BrowserIPC.ReceiveMessage,
        {
            request: message,
            host: window.location.origin,
            onMessage: (message: StringRequest) => {
                //console.info("Received: " + message.getRequest());
                receiveMessage(message.getRequest());
            },
            onEnd: (code: grpc.Code, msg: string | undefined, trailers: grpc.Metadata) => {
                if (code == grpc.Code.OK) {
                    //console.log("all ok")
                } else {
                    console.error("grpc error", code, msg, trailers);
                }
            }
        } );

    (window.external as any).sendMessage = sendMessage;

}