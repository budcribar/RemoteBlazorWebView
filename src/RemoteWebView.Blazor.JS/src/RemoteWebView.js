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
var sequenceNum = 1;
var clientId;
var isPrimary = true;
function sendMessage(message) {
    var req = new webview_pb_1.SendSequenceMessageRequest();
    var id = window.location.pathname.split('/')[1];
    if (id == 'mirror')
        id = window.location.pathname.split('/')[2];
    req.setId(id);
    req.setClientid(clientId);
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
                //console.log("sent:" + req.getSequence() + ":" + message + " clientId:" + clientId);
            }
            else {
                console.log("grpc error", code, msg, trailers);
                (0, BootErrors_1.showErrorNotification)();
            }
        }
    });
}
exports.sendMessage = sendMessage;
function setNotAllowedCursor(isPrimary) {
    if (!isPrimary) {
        document.body.style.cursor = 'not-allowed';
        var a = document.getElementsByTagName('a');
        for (var idx = 0; idx < a.length; ++idx) {
            a[idx].style.cursor = 'not-allowed';
        }
        var b = document.getElementsByTagName('button');
        for (var idx = 0; idx < b.length; ++idx) {
            b[idx].style.cursor = 'not-allowed';
        }
        var s = document.getElementsByTagName('span');
        for (var idx = 0; idx < s.length; ++idx) {
            s[idx].style.cursor = 'not-allowed';
        }
        var d = document.getElementsByTagName('div');
        for (var idx = 0; idx < d.length; ++idx) {
            d[idx].style.cursor = 'not-allowed';
        }
        var i = document.getElementsByTagName('input');
        for (var idx = 0; idx < i.length; ++idx) {
            i[idx].style.cursor = 'not-allowed';
        }
        var t = document.getElementsByTagName('textarea');
        for (var idx = 0; idx < t.length; ++idx) {
            t[idx].style.cursor = 'not-allowed';
        }
        var l = document.getElementsByTagName('li');
        for (var idx = 0; idx < l.length; ++idx) {
            l[idx].style.cursor = 'not-allowed';
        }
    }
}
function initializeRemoteWebView() {
    window.external.sendMessage = sendMessage;
    var message = new webview_pb_1.IdMessageRequest();
    var id = window.location.pathname.split('/')[1];
    if (id == 'mirror')
        id = window.location.pathname.split('/')[2];
    message.setId(id);
    grpc_web_1.grpc.invoke(webview_pb_service_1.BrowserIPC.GetClientId, {
        request: message,
        host: window.location.origin,
        onMessage: function (message) {
            //console.info("ClientId: " + message.getId());
            clientId = message.getClientid();
            isPrimary = message.getIsprimary();
            sendMessage("connected:");
            (0, WebViewIpcSender_1.sendAttachPage)(NavigationManager_1.internalFunctions.getBaseURI(), NavigationManager_1.internalFunctions.getLocationHref());
            setNotAllowedCursor(isPrimary);
        },
        onEnd: function (code, msg, trailers) {
            if (code == grpc_web_1.grpc.Code.OK) {
                //console.log("all ok:" + clientId)
            }
            else {
                console.error("grpc error", code, msg, trailers);
            }
        }
    });
    grpc_web_1.grpc.invoke(webview_pb_service_1.BrowserIPC.ReceiveMessage, {
        request: message,
        host: window.location.origin,
        onMessage: function (message) {
            //console.info("Received: " + message.getRequest());
            (0, IPC_1.receiveMessage)(message.getRequest());
            setNotAllowedCursor(isPrimary);
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
}
exports.initializeRemoteWebView = initializeRemoteWebView;
//# sourceMappingURL=RemoteWebView.js.map