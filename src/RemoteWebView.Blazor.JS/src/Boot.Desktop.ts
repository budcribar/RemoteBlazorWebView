import { DotNet } from '../Web.JS/node_modules/@microsoft/dotnet-js-interop';
import { Blazor } from '../Web.JS/src/GlobalExports';
import { shouldAutoStart } from '../Web.JS/src/BootCommon';
import { internalFunctions as navigationManagerFunctions } from '../Web.JS/src/Services/NavigationManager';
import { sendBeginInvokeDotNetFromJS, sendEndInvokeJSFromDotNet, sendByteArray, sendLocationChanged, sendLocationChanging } from '../Web.JS/src/Platform/WebView/WebViewIpcSender';
import { fetchAndInvokeInitializers } from '../Web.JS/src/JSInitializers/JSInitializers.WebView';
import { initializeRemoteWebView } from './RemoteWebView';
import { receiveDotNetDataStream } from '../Web.JS/src/StreamingInterop';
import { WebRendererId } from '../Web.JS/src/Rendering/WebRendererId';


let started = false;
export let dispatcher: DotNet.ICallDispatcher;
async function boot(): Promise<void> {
    if (started) {
        throw new Error('Blazor has already started.');
    }
    started = true;

    dispatcher = DotNet.attachDispatcher({
        beginInvokeDotNetFromJS: sendBeginInvokeDotNetFromJS,
        endInvokeJSFromDotNet: sendEndInvokeJSFromDotNet,
        sendByteArray: sendByteArray,
    });

    const jsInitializer = await fetchAndInvokeInitializers();

    initializeRemoteWebView();

    Blazor._internal.receiveWebViewDotNetDataStream = receiveWebViewDotNetDataStream;

    navigationManagerFunctions.enableNavigationInterception(WebRendererId.WebView);
    navigationManagerFunctions.listenForNavigationEvents(WebRendererId.WebView, sendLocationChanged, sendLocationChanging);

    // sendAttachPage is done in initializeRemoteWebView()

    await jsInitializer.invokeAfterStartedCallbacks(Blazor);
}

function receiveWebViewDotNetDataStream(streamId: number, data: any, bytesRead: number, errorMessage: string): void {
    receiveDotNetDataStream(dispatcher, streamId, data, bytesRead, errorMessage);
}

Blazor.start = boot;
window['DotNet'] = DotNet;


if (shouldAutoStart()) {
    boot();
}
