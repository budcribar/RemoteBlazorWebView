"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.receiveMessage = void 0;
var Renderer_1 = require("../web.js/src/Rendering/Renderer");
var OutOfProcessRenderBatch_1 = require("../web.js/src/Rendering/RenderBatch/OutOfProcessRenderBatch");
var WebViewIpcSender_1 = require("../web.js/src/Platform/WebView/WebViewIpcSender");
var WebViewIpcCommon_1 = require("../web.js/src/Platform/WebView/WebViewIpcCommon");
var BootErrors_1 = require("../web.js/src/BootErrors");
var NavigationManager_1 = require("../web.js/src/Services/NavigationManager");
var Boot_WebView_1 = require("../web.js/src/Boot.WebView");
var messageHandlers = {
    'AttachToDocument': function (componentId, elementSelector) {
        (0, Renderer_1.attachRootComponentToElement)(elementSelector, componentId);
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
    'BeginInvokeJS': Boot_WebView_1.dispatcher.beginInvokeJSFromDotNet.bind(Boot_WebView_1.dispatcher),
    'EndInvokeDotNet': Boot_WebView_1.dispatcher.endInvokeDotNetFromJS.bind(Boot_WebView_1.dispatcher),
    'SendByteArrayToJS': receiveBase64ByteArray,
    'Navigate': NavigationManager_1.internalFunctions.navigateTo,
    'SetHasLocationChangingListeners': NavigationManager_1.internalFunctions.setHasLocationChangingListeners,
    'EndLocationChanging': NavigationManager_1.internalFunctions.endLocationChanging,
};
function receiveBase64ByteArray(id, base64Data) {
    var data = base64ToArrayBuffer(base64Data);
    Boot_WebView_1.dispatcher.receiveByteArray(id, data);
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
    console.log("Receive:" + message);
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