import { initializeRemoteWebView, sendMessage } from "./RemoteWebView";
import { navigateTo } from "../web.js/src/Services/NavigationManager"
import { attachRootComponentToElement, renderBatch } from '../web.js/src/Rendering/Renderer';
import { OutOfProcessRenderBatch } from '../web.js/src/Rendering/RenderBatch/OutOfProcessRenderBatch';
import { sendRenderCompleted } from '../web.js/src/Platform/WebView/WebViewIpcSender';
import { setApplicationIsTerminated, tryDeserializeMessage } from '../web.js/src/Platform/WebView/WebViewIpcCommon';
import { showErrorNotification } from '../web.js/src/BootErrors';
import { DotNet } from '../web.js/node_modules/@microsoft/dotnet-js-interop';
import { internalFunctions as navigationManagerFunctions, NavigationOptions } from '../web.js/src/Services/NavigationManager';
import { dispatcher } from '../web.js/src/Boot.WebView';

const messageHandlers = {

    'AttachToDocument': (componentId: number, elementSelector: string) => {
        attachRootComponentToElement(elementSelector, componentId);
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

    'BeginInvokeJS': dispatcher.beginInvokeJSFromDotNet.bind(dispatcher),

    'EndInvokeDotNet': dispatcher.endInvokeDotNetFromJS.bind(dispatcher),

    'SendByteArrayToJS': receiveBase64ByteArray,

    'Navigate': navigationManagerFunctions.navigateTo,
       
    'SetHasLocationChangingListeners': navigationManagerFunctions.setHasLocationChangingListeners,

    'EndLocationChanging': navigationManagerFunctions.endLocationChanging,
};

function receiveBase64ByteArray(id: number, base64Data: string) {
    const data = base64ToArrayBuffer(base64Data);
    dispatcher.receiveByteArray(id, data);
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
    console.log("Receive:" + message);
    const parsedMessage = tryDeserializeMessage(message);
    if (parsedMessage) {
        if (messageHandlers.hasOwnProperty(parsedMessage.messageType)) {
            messageHandlers[parsedMessage.messageType].apply(null, parsedMessage.args);
        } else {
            throw new Error(`Unsupported IPC message type '${parsedMessage.messageType}'`);
        }
    }
}
