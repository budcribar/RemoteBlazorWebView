import { receiveMessage } from './IPC';
import { grpc } from "@improbable-eng/grpc-web";
import { BrowserIPC } from "./generated/webwindow_pb_service";
import { SendSequenceMessageRequest, StringRequest, IdMessageRequest } from "./generated/webwindow_pb";
var sequenceNum: number = 1;

export async function sendMessage(message: string) {
    
    var req = new SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    req.setId(id);
    req.setMessage(message);
    req.setSequence(sequenceNum++);
    await grpc.invoke(BrowserIPC.SendMessage, {
        request: req, host: window.location.origin, onEnd: (code, msg, trailers) => {
            if (code == grpc.Code.OK) {
                console.info("sent:" + message)
            } else {
                console.log("hit an error", code, msg, trailers);
            }
        }
    });
}

export function initializeRemotePhotino() {
    var message = new IdMessageRequest();
    var id = window.location.pathname.split('/')[1];
    message.setId(id);

    grpc.invoke(BrowserIPC.ReceiveMessage,
        {
            request: message,
            host: window.location.origin,
            onMessage: (message: StringRequest) => {
                console.info("Received: " + message.getRequest());
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

    window.addEventListener('resize', (event) => {
        if ((<any>window).RemotePhotino.resizeEventHandlerAttached)
            sendMessage("size:" + JSON.stringify((<any>window).RemotePhotino.size()));
    });

    var prevX = window.screenX;
    var prevY = window.screenY;

    var interval = setInterval(function () {
        if (prevX != window.screenX || prevY != window.screenY) {
            if ((<any>window).RemotePhotino.locationEventHandlerAttached)
                sendMessage("location:" + JSON.stringify((<any>window).RemotePhotino.location()));
        }
        prevX = window.screenX;
        prevY = window.screenY;
    }, 200);


    (<any>window).RemotePhotino = {
        resizeEventHandlerAttached: false,
        setResizeEventHandlerAttached: function (value) {
            (<any>window).RemotePhotino.resizeEventHandlerAttached = value;
        },

        locationEventHandlerAttached: false,
        setLocationEventHandlerAttached: function (value) {
            (<any>window).RemotePhotino.locationEventHandlerAttached = value;
        },

        width: function () {
            return window.outerWidth;
        },
        setWidth: function (width) {
            window.resizeTo(width, window.outerHeight);
        },
        height: function () {
            return window.outerHeight;
        },
        setHeight: function (height) {
            window.resizeTo(window.outerWidth, height);
        },

        left: function () {
            return window.screenLeft;
        },
        setLeft: function (left) {
            window.moveTo(left, window.screenY)
        },
        location: function () {
            return { X: window.screenX, Y: window.screenY }
        },
        setLocation: function (location) {
            window.moveTo(location.x, location.y)
        },
        top: function () {
            return window.screenTop;
        },
        setTop: function (top) {
            window.moveTo(window.screenX, top);
        },
        size: function () {
            return { Width: window.outerWidth, Height: window.outerHeight }
        },

        title: function () {
            return window.document.title
        },

        setTitle: function (title) {
            return window.document.title = title;
        },
        setSize: function (size) {
            return window.resizeTo(size.width, size.height);
        },

        showMessage: function (title,body) {
            window.alert(body);
        }
    };

}