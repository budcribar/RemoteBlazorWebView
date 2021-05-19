import { sendMessage } from "./RemoteWebWindow";
import { navigateTo } from "../upstream/aspnetcore/web.js/src/Services/NavigationManager"
import { attachRootComponentToElement, renderBatch } from '../upstream/aspnetcore/web.js/src/Rendering/Renderer';
import { OutOfProcessRenderBatch } from '../upstream/aspnetcore/web.js/src/Rendering/RenderBatch/OutOfProcessRenderBatch';
import { sendRenderCompleted } from '../upstream/aspnetcore/web.js/src/Platform/WebView/WebViewIpcSender';
import { setApplicationIsTerminated, tryDeserializeMessage } from '../upstream/aspnetcore/web.js/src/Platform/WebView/WebViewIpcCommon';
import { showErrorNotification } from '../upstream/aspnetcore/web.js/src/BootErrors';
import { DotNet } from '@microsoft/dotnet-js-interop';
import { internalFunctions as navigationManagerFunctions } from '../upstream/aspnetcore/web.js/src/Services/NavigationManager';

const messageHandlers = {

    'AttachToDocument': (componentId: number, elementSelector: string) => {
        attachRootComponentToElement(elementSelector, componentId);

        //TODO Hack required to get home displayed
        if (componentId == 0) {
            var id = window.location.pathname.split('/')[1];
            navigateTo(`/${id}/`, false);
            sendMessage("connected:");
        }
    },

    'RenderBatch': (batchId: number, batchDataBase64: string) => {
        try {
            const batchData = base64ToArrayBuffer(batchDataBase64);
            renderBatch(0, new OutOfProcessRenderBatch(batchData));
            sendRenderCompleted(batchId, null);
        } catch (ex) {
            sendRenderCompleted(batchId, ex.toString());
        }
    },

    'NotifyUnhandledException': (message: string, stackTrace: string) => {
        setApplicationIsTerminated();
        console.error(`${message}\n${stackTrace}`);
        showErrorNotification();
    },

    'BeginInvokeJS': DotNet.jsCallDispatcher.beginInvokeJSFromDotNet,

    'EndInvokeDotNet': (asyncCallId: string, success: boolean, invocationResultOrError: string) => {
        const resultOrExceptionMessage: any = DotNet.parseJsonWithRevivers(invocationResultOrError);
        DotNet.jsCallDispatcher.endInvokeDotNetFromJS(asyncCallId, success, resultOrExceptionMessage);

      
    },

    'Navigate': navigationManagerFunctions.navigateTo,
};

function base64ToArrayBuffer(base64: string) {
    const binaryString = atob(base64);
    const length = binaryString.length;
    const result = new Uint8Array(length);
    for (let i = 0; i < length; i++) {
        result[i] = binaryString.charCodeAt(i);
    }
    return result;
}


export function send(eventName: string, args: any): void {
    sendMessage(`ipc:${eventName} ${JSON.stringify(args)}`);
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


