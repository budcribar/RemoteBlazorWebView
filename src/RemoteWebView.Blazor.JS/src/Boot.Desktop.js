"use strict";
var __awaiter = (this && this.__awaiter) || function (thisArg, _arguments, P, generator) {
    function adopt(value) { return value instanceof P ? value : new P(function (resolve) { resolve(value); }); }
    return new (P || (P = Promise))(function (resolve, reject) {
        function fulfilled(value) { try { step(generator.next(value)); } catch (e) { reject(e); } }
        function rejected(value) { try { step(generator["throw"](value)); } catch (e) { reject(e); } }
        function step(result) { result.done ? resolve(result.value) : adopt(result.value).then(fulfilled, rejected); }
        step((generator = generator.apply(thisArg, _arguments || [])).next());
    });
};
var __generator = (this && this.__generator) || function (thisArg, body) {
    var _ = { label: 0, sent: function() { if (t[0] & 1) throw t[1]; return t[1]; }, trys: [], ops: [] }, f, y, t, g;
    return g = { next: verb(0), "throw": verb(1), "return": verb(2) }, typeof Symbol === "function" && (g[Symbol.iterator] = function() { return this; }), g;
    function verb(n) { return function (v) { return step([n, v]); }; }
    function step(op) {
        if (f) throw new TypeError("Generator is already executing.");
        while (g && (g = 0, op[0] && (_ = 0)), _) try {
            if (f = 1, y && (t = op[0] & 2 ? y["return"] : op[0] ? y["throw"] || ((t = y["return"]) && t.call(y), 0) : y.next) && !(t = t.call(y, op[1])).done) return t;
            if (y = 0, t) op = [op[0] & 2, t.value];
            switch (op[0]) {
                case 0: case 1: t = op; break;
                case 4: _.label++; return { value: op[1], done: false };
                case 5: _.label++; y = op[1]; op = [0]; continue;
                case 7: op = _.ops.pop(); _.trys.pop(); continue;
                default:
                    if (!(t = _.trys, t = t.length > 0 && t[t.length - 1]) && (op[0] === 6 || op[0] === 2)) { _ = 0; continue; }
                    if (op[0] === 3 && (!t || (op[1] > t[0] && op[1] < t[3]))) { _.label = op[1]; break; }
                    if (op[0] === 6 && _.label < t[1]) { _.label = t[1]; t = op; break; }
                    if (t && _.label < t[2]) { _.label = t[2]; _.ops.push(op); break; }
                    if (t[2]) _.ops.pop();
                    _.trys.pop(); continue;
            }
            op = body.call(thisArg, _);
        } catch (e) { op = [6, e]; y = 0; } finally { f = t = 0; }
        if (op[0] & 5) throw op[1]; return { value: op[0] ? op[1] : void 0, done: true };
    }
};
Object.defineProperty(exports, "__esModule", { value: true });
var dotnet_js_interop_1 = require("../web.js/node_modules/@microsoft/dotnet-js-interop");
var GlobalExports_1 = require("../web.js/src/GlobalExports");
var BootCommon_1 = require("../web.js/src/BootCommon");
var NavigationManager_1 = require("../web.js/src/Services/NavigationManager");
var WebViewIpcSender_1 = require("../web.js/src/Platform/WebView/WebViewIpcSender");
var JSInitializers_WebView_1 = require("../web.js/src/JSInitializers/JSInitializers.WebView");
var Boot_WebView_1 = require("../web.js/src/Boot.WebView");
var RemoteWebView_1 = require("./RemoteWebView");
var StreamingInterop_1 = require("../web.js/src/StreamingInterop");
var Boot_WebView_2 = require("../web.js/src/Boot.WebView");
var started = false;
function boot() {
    return __awaiter(this, void 0, void 0, function () {
        var dispatcher, jsInitializer;
        return __generator(this, function (_a) {
            switch (_a.label) {
                case 0:
                    if (started) {
                        throw new Error('Blazor has already started.');
                    }
                    started = true;
                    dispatcher = dotnet_js_interop_1.DotNet.attachDispatcher({
                        beginInvokeDotNetFromJS: WebViewIpcSender_1.sendBeginInvokeDotNetFromJS,
                        endInvokeJSFromDotNet: WebViewIpcSender_1.sendEndInvokeJSFromDotNet,
                        sendByteArray: WebViewIpcSender_1.sendByteArray,
                    });
                    (0, Boot_WebView_1.setDispatcher)(dispatcher);
                    return [4 /*yield*/, (0, JSInitializers_WebView_1.fetchAndInvokeInitializers)()];
                case 1:
                    jsInitializer = _a.sent();
                    (0, RemoteWebView_1.initializeRemoteWebView)();
                    GlobalExports_1.Blazor._internal.receiveWebViewDotNetDataStream = receiveWebViewDotNetDataStream;
                    NavigationManager_1.internalFunctions.enableNavigationInterception();
                    NavigationManager_1.internalFunctions.listenForNavigationEvents(WebViewIpcSender_1.sendLocationChanged, WebViewIpcSender_1.sendLocationChanging);
                    // sendAttachPage is done in initializeRemoteWebView()
                    return [4 /*yield*/, jsInitializer.invokeAfterStartedCallbacks(GlobalExports_1.Blazor)];
                case 2:
                    // sendAttachPage is done in initializeRemoteWebView()
                    _a.sent();
                    return [2 /*return*/];
            }
        });
    });
}
function receiveWebViewDotNetDataStream(streamId, data, bytesRead, errorMessage) {
    (0, StreamingInterop_1.receiveDotNetDataStream)(Boot_WebView_2.dispatcher, streamId, data, bytesRead, errorMessage);
}
GlobalExports_1.Blazor.start = boot;
window['DotNet'] = dotnet_js_interop_1.DotNet;
if ((0, BootCommon_1.shouldAutoStart)()) {
    boot();
}
//# sourceMappingURL=Boot.Desktop.js.map