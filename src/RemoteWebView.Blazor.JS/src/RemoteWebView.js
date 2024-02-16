"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.initializeRemoteWebView = exports.sendMessage = void 0;
var IPC_1 = require("./IPC");
var grpc_web_1 = require("@improbable-eng/grpc-web");
var webview_pb_service_1 = require("./generated/webview_pb_service");
var webview_pb_1 = require("./generated/webview_pb");
var NavigationManager_1 = require("../web.js/src/Services/NavigationManager");
var BootErrors_1 = require("../web.js/src/BootErrors");
var WebViewIpcSender_1 = require("../web.js/src/Platform/WebView/WebViewIpcSender");
if (!window.RemoteBlazor) {
    window.RemoteBlazor = {};
}
window.RemoteBlazor.sequenceNum = 1;
window.RemoteBlazor.clientId = '';
window.RemoteBlazor.isPrimary = true;
var locationOrigin = window.location.origin;
function sendMessage(message) {
    var req = new webview_pb_1.SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    if (id == 'mirror') {
        id = window.location.pathname.split('/')[2];
    }
    req.setId(id);
    req.setClientid(window.RemoteBlazor.clientId);
    req.setMessage(message);
    req.setSequence(window.RemoteBlazor.sequenceNum++);
    req.setUrl(NavigationManager_1.internalFunctions.getLocationHref());
    req.setIsprimary(window.RemoteBlazor.isPrimary);
    grpc_web_1.grpc.invoke(webview_pb_service_1.BrowserIPC.SendMessage, {
        request: req,
        host: window.RemoteBlazor && window.RemoteBlazor.grpcHost ? window.RemoteBlazor.grpcHost : locationOrigin,
        onMessage: function (message) {
            if (!message.getSuccess()) {
                var error = "Client ".concat(id, " is unresponsive");
                console.log(error);
                (0, BootErrors_1.showErrorNotification)();
            }
        },
        onEnd: function (code, msg, trailers) {
            if (code == grpc_web_1.grpc.Code.OK) {
                //console.log("sent:" + req.getSequence() + ":" + message + " window.RemoteBlazor.clientId:" + clientId);
            }
            else {
                console.log("grpc error", code, msg, trailers);
                (0, BootErrors_1.showErrorNotification)();
            }
        }
    });
}
exports.sendMessage = sendMessage;
function makePageReadOnly() {
    // Create the overlay element
    var overlay = document.createElement('div');
    overlay.style.position = 'fixed';
    overlay.style.top = '0px';
    overlay.style.left = '0px';
    overlay.style.width = '100%';
    overlay.style.height = '100%';
    overlay.style.zIndex = '10000';
    overlay.style.background = 'transparent';
    overlay.style.cursor = 'not-allowed';
    // Append the overlay to the body
    document.body.appendChild(overlay);
    // Disable scrolling
    document.body.style.overflow = 'hidden';
    // Bind events for keyboard
    window.addEventListener('keydown', preventInteraction, true);
    window.addEventListener('keyup', preventInteraction, true);
    function preventInteraction(event) {
        event.preventDefault();
        event.stopPropagation();
    }
    document.body.style.userSelect = 'none';
    document.body.setAttribute('oncontextmenu', 'return false;');
}
function initializeRemoteWebView() {
    window.external.sendMessage = sendMessage;
    var pathParts = window.location.pathname.split('/');
    var id = pathParts[1];
    if (id === 'mirror') {
        id = pathParts[2];
        window.RemoteBlazor.isPrimary = false;
        makePageReadOnly();
    }
    window.RemoteBlazor.clientId = window.crypto.randomUUID();
    sendMessage("connected:");
    (0, WebViewIpcSender_1.sendAttachPage)(NavigationManager_1.internalFunctions.getBaseURI(), NavigationManager_1.internalFunctions.getLocationHref());
    var message = new webview_pb_1.ReceiveMessageRequest();
    message.setId(id);
    message.setClientid(window.RemoteBlazor.clientId);
    message.setIsprimary(window.RemoteBlazor.isPrimary);
    grpc_web_1.grpc.invoke(webview_pb_service_1.BrowserIPC.ReceiveMessage, {
        request: message,
        host: window.RemoteBlazor && window.RemoteBlazor.grpcHost ? window.RemoteBlazor.grpcHost : locationOrigin,
        onMessage: function (message) {
            console.info("BrowserIPC.ReceiveMessage: " + message.getRequest());
            (0, IPC_1.receiveMessage)(message.getRequest());
            if (!window.RemoteBlazor.isPrimary)
                makePageReadOnly();
        },
        onEnd: function (code, msg, trailers) {
            if (code == grpc_web_1.grpc.Code.OK) {
                console.log("BrowserIPC.ReceiveMessage:onEnd:ok");
            }
            else {
                console.error("grpc error", code, msg, trailers);
            }
        }
    });
}
exports.initializeRemoteWebView = initializeRemoteWebView;
//# sourceMappingURL=RemoteWebView.js.map