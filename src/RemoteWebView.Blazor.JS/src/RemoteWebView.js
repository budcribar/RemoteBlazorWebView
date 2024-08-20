"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.sendMessage = sendMessage;
exports.initializeRemoteWebView = initializeRemoteWebView;
var IPC_1 = require("./IPC");
var WebviewServiceClientPb_1 = require("./generated/WebviewServiceClientPb");
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
var client = new WebviewServiceClientPb_1.BrowserIPCClient(window.RemoteBlazor.grpcHost || locationOrigin);
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
    client.sendMessage(req, {}, function (err, response) {
        if (err) {
            console.log("grpc error", err.code, err.message);
            (0, BootErrors_1.showErrorNotification)();
            return;
        }
        if (!response.getSuccess()) {
            var error = "Client ".concat(id, " is unresponsive");
            console.log(error);
            (0, BootErrors_1.showErrorNotification)();
        }
        else {
            //console.log("sent:" + req.getSequence() + ":" + message + " window.RemoteBlazor.clientId:" + window.RemoteBlazor.clientId);
        }
    });
}
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
    window.RemoteBlazor.clientId = crypto.randomUUID();
    sendMessage("connected:");
    (0, WebViewIpcSender_1.sendAttachPage)(NavigationManager_1.internalFunctions.getBaseURI(), NavigationManager_1.internalFunctions.getLocationHref());
    var message = new webview_pb_1.ReceiveMessageRequest();
    message.setId(id);
    message.setClientid(window.RemoteBlazor.clientId);
    message.setIsprimary(window.RemoteBlazor.isPrimary);
    var stream = client.receiveMessage(message, {});
    stream.on('data', function (response) {
        console.info("BrowserIPC.ReceiveMessage: " + response.getRequest());
        (0, IPC_1.receiveMessage)(response.getRequest());
        if (!window.RemoteBlazor.isPrimary)
            makePageReadOnly();
    });
    stream.on('error', function (err) {
        console.error("grpc error", err.code, err.message);
    });
    stream.on('end', function () {
        console.log("BrowserIPC.ReceiveMessage: stream ended");
    });
}
//# sourceMappingURL=RemoteWebView.js.map