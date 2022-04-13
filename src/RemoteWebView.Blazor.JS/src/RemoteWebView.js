"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.initializeRemoteWebView = exports.sendMessage = void 0;
var IPC_1 = require("./IPC");
var grpc_web_1 = require("@improbable-eng/grpc-web");
var webview_pb_service_1 = require("./generated/webview_pb_service");
var webview_pb_1 = require("./generated/webview_pb");
var NavigationManager_1 = require("../web.js/src/Services/NavigationManager");
var BootErrors_1 = require("../web.js/src/BootErrors");
var sequenceNum = 1;
function sendMessage(message) {
    var req = new webview_pb_1.SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    req.setId(id);
    req.setMessage(message);
    req.setSequence(sequenceNum++);
    req.setUrl(NavigationManager_1.internalFunctions.getLocationHref());
    grpc_web_1.grpc.invoke(webview_pb_service_1.BrowserIPC.SendMessage, {
        request: req,
        host: window.location.origin,
        onMessage: function (message) {
            if (!message.getSuccess()) {
                var error = "Client ".concat(id, " is unresponsive");
                console.log(error);
                (0, BootErrors_1.showErrorNotification)();
            }
        },
        onEnd: function (code, msg, trailers) {
            if (code == grpc_web_1.grpc.Code.OK) {
                //console.log("sent:" + req.getSequence() + ":" + message);
            }
            else {
                console.log("grpc error", code, msg, trailers);
                (0, BootErrors_1.showErrorNotification)();
            }
        }
    });
}
exports.sendMessage = sendMessage;
function initializeRemoteWebView() {
    var message = new webview_pb_1.IdMessageRequest();
    var id = window.location.pathname.split('/')[1];
    message.setId(id);
    grpc_web_1.grpc.invoke(webview_pb_service_1.BrowserIPC.ReceiveMessage, {
        request: message,
        host: window.location.origin,
        onMessage: function (message) {
            //console.info("Received: " + message.getRequest());
            (0, IPC_1.receiveMessage)(message.getRequest());
        },
        onEnd: function (code, msg, trailers) {
            if (code == grpc_web_1.grpc.Code.OK) {
                //console.log("all ok")
            }
            else {
                console.error("grpc error", code, msg, trailers);
            }
        }
    });
    window.external.sendMessage = sendMessage;
}
exports.initializeRemoteWebView = initializeRemoteWebView;
//# sourceMappingURL=RemoteWebView.js.map