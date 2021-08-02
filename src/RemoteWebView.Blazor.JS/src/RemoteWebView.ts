import { receiveMessage, send } from './IPC';
import { grpc } from "@improbable-eng/grpc-web";
import { BrowserIPC } from "./generated/webview_pb_service";
import { SendSequenceMessageRequest, StringRequest, IdMessageRequest } from "./generated/webview_pb";
import { internalFunctions as navigationManagerFunctions } from '../upstream/aspnetcore/web.js/src/Services/NavigationManager';

var sequenceNum: number = 1;

export function sendMessage(message: string) {
    var req = new SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    req.setId(id);
    req.setMessage(message);
    req.setSequence(sequenceNum++);
    req.setUrl(navigationManagerFunctions.getLocationHref())
    grpc.invoke(BrowserIPC.SendMessage, {
        request: req, host: window.location.origin, onEnd: (code, msg, trailers) => {
            if (code == grpc.Code.OK) {
                //console.log("sent:" + req.getSequence() + ":" + message)
            } else {
                console.log("hit an error", code, msg, trailers);
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
                    console.log("hit an error", code, msg, trailers);
                }
            }
        });

    (<any>window).RemoteWebWindow = {

        showMessage: function (message) {

            window.alert(message);
        }
    };

    (window.external as any).sendMessage = sendMessage;

}