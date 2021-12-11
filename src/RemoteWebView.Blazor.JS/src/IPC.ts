import { sendMessage } from "./RemoteWebView";
import { navigateTo } from "../upstream/aspnetcore/web.js/src/Services/NavigationManager"
import { attachRootComponentToElement, renderBatch } from '../upstream/aspnetcore/web.js/src/Rendering/Renderer';
import { OutOfProcessRenderBatch } from '../upstream/aspnetcore/web.js/src/Rendering/RenderBatch/OutOfProcessRenderBatch';
import { sendRenderCompleted } from '../upstream/aspnetcore/web.js/src/Platform/WebView/WebViewIpcSender';
import { setApplicationIsTerminated, tryDeserializeMessage } from '../upstream/aspnetcore/web.js/src/Platform/WebView/WebViewIpcCommon';
import { showErrorNotification } from '../upstream/aspnetcore/web.js/src/BootErrors';
import { DotNet } from '../upstream/aspnetcore/web.js/node_modules/@microsoft/dotnet-js-interop';
import { internalFunctions as navigationManagerFunctions } from '../upstream/aspnetcore/web.js/src/Services/NavigationManager';
import { grpc } from "@improbable-eng/grpc-web";
import { BrowserIPC } from "./generated/webview_pb_service";
import { PingMessageRequest, PingMessageResponse } from "./generated/webview_pb";

const messageHandlers = {

    'AttachToDocument': (componentId: number, elementSelector: string) => {
        attachRootComponentToElement(elementSelector, componentId);

        //TODO Hack required to get home displayed
        if (componentId == 0) {
            var id = window.location.pathname.split('/')[1];
            navigateTo(`/${id}/`, false, true);
            sendMessage("connected:");
        }

        var ping = new PingMessageRequest();
        var id = window.location.pathname.split('/')[1];
        ping.setId(id);
        ping.setPingintervalseconds(30);
        ping.setInitialize(true);

        //console.log("ping invoke init");
        grpc.invoke(BrowserIPC.Ping,
            {
                request: ping,
                host: window.location.origin,
                onMessage: (message: PingMessageResponse) => {
                    //console.info("Ping Init Response: " + ping.getId());
                },
                onEnd: (code: grpc.Code, msg: string | undefined, trailers: grpc.Metadata) => {
                    if (code == grpc.Code.OK) {
                       // console.log("ping init all ok")
                    } else {
                        console.error("grpc error", code, msg, trailers);
                    }
                }
            });

        ping.setInitialize(false);
        const interval = setInterval(function () {
            //console.log("ping invoke");
            grpc.invoke(BrowserIPC.Ping,
                {
                    request: ping,
                    host: window.location.origin,
                    onMessage: (message: PingMessageResponse) => {
                        //console.info("Ping Response: " + ping.getId());
                    },
                    onEnd: (code: grpc.Code, msg: string | undefined, trailers: grpc.Metadata) => {
                        if (code == grpc.Code.OK) {
                            //console.log("ping all ok")
                        } else {
                            console.error("grpc error", code, msg, trailers);
                        }
                    }
                });
        }, 30000);
    },

    'RenderBatch': (batchId: number, batchDataBase64: string) => {
        try {
            const batchData = base64ToArrayBuffer(batchDataBase64);
            renderBatch(0, new OutOfProcessRenderBatch(batchData));
            sendRenderCompleted(batchId, null);
        } catch (ex) {
            sendRenderCompleted(batchId, (ex as Error).toString());
        }
    },

    'NotifyUnhandledException': (message: string, stackTrace: string) => {
        setApplicationIsTerminated();
        console.error(`${message}\n${stackTrace}`);
        showErrorNotification();
    },

    'BeginInvokeJS': DotNet.jsCallDispatcher.beginInvokeJSFromDotNet,

    'EndInvokeDotNet': DotNet.jsCallDispatcher.endInvokeDotNetFromJS,

    'SendByteArrayToJS': receiveBase64ByteArray,

    'Navigate': navigationManagerFunctions.navigateTo,
};

function receiveBase64ByteArray(id: number, base64Data: string) {
    const data = base64ToArrayBuffer(base64Data);
    DotNet.jsCallDispatcher.receiveByteArray(id, data);
}


function base64ToArrayBuffer(base64: string) {
    const binaryString = atob(base64);
    const length = binaryString.length;
    const result = new Uint8Array(length);
    for (let i = 0; i < length; i++) {
        result[i] = binaryString.charCodeAt(i);
    }
    return result;
}

export function receiveMessage(message: string) {
    const parsedMessage = tryDeserializeMessage(message);
    if (parsedMessage) {
        if (messageHandlers.hasOwnProperty(parsedMessage.messageType)) {
            messageHandlers[parsedMessage.messageType].apply(null, parsedMessage.args);
        } else {
            throw new Error(`Unsupported IPC message type '${parsedMessage.messageType}'`);
        }
    }
}
