import { attachRootComponentToElement, renderBatch } from '../web.js/src/Rendering/Renderer';
import { OutOfProcessRenderBatch } from '../web.js/src/Rendering/RenderBatch/OutOfProcessRenderBatch';
import { sendRenderCompleted } from '../web.js/src/Platform/WebView/WebViewIpcSender';
import { setApplicationIsTerminated, tryDeserializeMessage } from '../web.js/src/Platform/WebView/WebViewIpcCommon';
import { showErrorNotification } from '../web.js/src/BootErrors';
import { internalFunctions as navigationManagerFunctions } from '../web.js/src/Services/NavigationManager';
import { dispatcher } from './Boot.Desktop'
import { WebRendererId } from '../web.js/src/Rendering/WebRendererId';


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
    const messageHandlers = {

        'AttachToDocument': (componentId: number, elementSelector: string) => {
            attachRootComponentToElement(elementSelector, componentId, WebRendererId.WebView);
        },

        'RenderBatch': (batchId: number, batchDataBase64: string) => {
            try {
                const batchData = base64ToArrayBuffer(batchDataBase64);
                renderBatch(WebRendererId.WebView, new OutOfProcessRenderBatch(batchData));
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

        'Refresh': navigationManagerFunctions.refresh,

        'SetHasLocationChangingListeners': navigationManagerFunctions.setHasLocationChangingListeners,

        'EndLocationChanging': navigationManagerFunctions.endLocationChanging,
    };
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
