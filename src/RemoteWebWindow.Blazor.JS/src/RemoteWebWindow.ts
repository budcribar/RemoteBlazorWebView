import { receiveMessage } from './IPC';
import { grpc } from "@improbable-eng/grpc-web";
import { BrowserIPC } from "./generated/webwindow_pb_service";
import { StringRequest, IdMessageRequest } from "./generated/webwindow_pb";

export async function sendMessage(message: string) {
    var req = new StringRequest();
    var id = window.location.pathname.split('/')[1];
    req.setId(id);
    req.setRequest(message);
    await grpc.invoke(BrowserIPC.SendMessage, {
        request: req, host: window.location.origin, onEnd: (code, msg, trailers) => {
            if (code == grpc.Code.OK) {
                //console.log("sent:" + message)
            } else {
                console.log("hit an error", code, msg, trailers);
            }
        }
    });
}

export function initializeRemoteWebWindow() {
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

        showMessage: function (title,body) {

            window.alert(body);
        }
    };

}