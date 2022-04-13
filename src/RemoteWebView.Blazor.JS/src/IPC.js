"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.receiveMessage = void 0;
var RemoteWebView_1 = require("./RemoteWebView");
var Renderer_1 = require("../web.js/src/Rendering/Renderer");
var OutOfProcessRenderBatch_1 = require("../web.js/src/Rendering/RenderBatch/OutOfProcessRenderBatch");
var WebViewIpcSender_1 = require("../web.js/src/Platform/WebView/WebViewIpcSender");
var WebViewIpcCommon_1 = require("../web.js/src/Platform/WebView/WebViewIpcCommon");
var BootErrors_1 = require("../web.js/src/BootErrors");
var dotnet_js_interop_1 = require("../web.js/node_modules/@microsoft/dotnet-js-interop");
var NavigationManager_1 = require("../web.js/src/Services/NavigationManager");
var messageHandlers = {
    'AttachToDocument': function (componentId, elementSelector) {
        (0, Renderer_1.attachRootComponentToElement)(elementSelector, componentId);
        if (componentId == 0) {
            (0, RemoteWebView_1.sendMessage)("connected:");
        }
    },
    'RenderBatch': function (batchId, batchDataBase64) {
        try {
            var batchData = base64ToArrayBuffer(batchDataBase64);
            (0, Renderer_1.renderBatch)(0, new OutOfProcessRenderBatch_1.OutOfProcessRenderBatch(batchData));
            (0, WebViewIpcSender_1.sendRenderCompleted)(batchId, null);
        }
        catch (ex) {
            (0, WebViewIpcSender_1.sendRenderCompleted)(batchId, ex.toString());
        }
    },
    'NotifyUnhandledException': function (message, stackTrace) {
        (0, WebViewIpcCommon_1.setApplicationIsTerminated)();
        console.error("".concat(message, "\n").concat(stackTrace));
        (0, BootErrors_1.showErrorNotification)();
    },
    'BeginInvokeJS': dotnet_js_interop_1.DotNet.jsCallDispatcher.beginInvokeJSFromDotNet,
    'EndInvokeDotNet': dotnet_js_interop_1.DotNet.jsCallDispatcher.endInvokeDotNetFromJS,
    'SendByteArrayToJS': receiveBase64ByteArray,
    'Navigate': NavigationManager_1.internalFunctions.navigateTo,
};
function receiveBase64ByteArray(id, base64Data) {
    var data = base64ToArrayBuffer(base64Data);
    dotnet_js_interop_1.DotNet.jsCallDispatcher.receiveByteArray(id, data);
}
function base64ToArrayBuffer(base64) {
    var binaryString = atob(base64);
    var length = binaryString.length;
    var result = new Uint8Array(length);
    for (var i = 0; i < length; i++) {
        result[i] = binaryString.charCodeAt(i);
    }
    return result;
}
function receiveMessage(message) {
    var parsedMessage = (0, WebViewIpcCommon_1.tryDeserializeMessage)(message);
    if (parsedMessage) {
        if (messageHandlers.hasOwnProperty(parsedMessage.messageType)) {
            messageHandlers[parsedMessage.messageType].apply(null, parsedMessage.args);
        }
        else {
            throw new Error("Unsupported IPC message type '".concat(parsedMessage.messageType, "'"));
        }
    }
}
exports.receiveMessage = receiveMessage;
//# sourceMappingURL=IPC.js.map