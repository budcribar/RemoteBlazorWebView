"use strict";
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
Object.defineProperty(exports, "__esModule", { value: true });
exports.startIpcReceiver = void 0;
var BootErrors_1 = require("../../BootErrors");
var OutOfProcessRenderBatch_1 = require("../../Rendering/RenderBatch/OutOfProcessRenderBatch");
var Renderer_1 = require("../../Rendering/Renderer");
var WebViewIpcCommon_1 = require("./WebViewIpcCommon");
var WebViewIpcSender_1 = require("./WebViewIpcSender");
var NavigationManager_1 = require("../../Services/NavigationManager");
var Boot_WebView_1 = require("../../Boot.WebView");
function startIpcReceiver() {
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
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    window.external.receiveMessage(function (message) {
        var parsedMessage = (0, WebViewIpcCommon_1.tryDeserializeMessage)(message);
        if (parsedMessage) {
            if (Object.prototype.hasOwnProperty.call(messageHandlers, parsedMessage.messageType)) {
                messageHandlers[parsedMessage.messageType].apply(null, parsedMessage.args);
            }
            else {
                throw new Error("Unsupported IPC message type '".concat(parsedMessage.messageType, "'"));
            }
        }
    });
}
exports.startIpcReceiver = startIpcReceiver;
function receiveBase64ByteArray(id, base64Data) {
    var data = base64ToArrayBuffer(base64Data);
    Boot_WebView_1.dispatcher.receiveByteArray(id, data);
}
// https://stackoverflow.com/a/21797381
// TODO: If the data is large, consider switching over to the native decoder as in https://stackoverflow.com/a/54123275
// But don't force it to be async all the time. Yielding execution leads to perceptible lag.
function base64ToArrayBuffer(base64) {
    var binaryString = atob(base64);
    var length = binaryString.length;
    var result = new Uint8Array(length);
    for (var i = 0; i < length; i++) {
        result[i] = binaryString.charCodeAt(i);
    }
    return result;
}
//# sourceMappingURL=WebViewIpcReceiver.js.map