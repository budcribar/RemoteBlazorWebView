import { DotNet } from '../web.js/node_modules/@microsoft/dotnet-js-interop';
import { Blazor } from '../web.js/src/GlobalExports';
import { shouldAutoStart } from '../web.js/src/BootCommon';
import { internalFunctions as navigationManagerFunctions } from '../web.js/src/Services/NavigationManager';
import {  sendAttachPage, sendBeginInvokeDotNetFromJS, sendEndInvokeJSFromDotNet, sendByteArray, sendLocationChanged } from '../web.js/src/Platform/WebView/WebViewIpcSender';
import { fetchAndInvokeInitializers } from '../web.js/src/JSInitializers/JSInitializers.WebView';

import { initializeRemoteWebView } from './RemoteWebView';

let started = false;

async function boot(): Promise<void> {
    if (started) {
        throw new Error('Blazor has already started.');
    }
    started = true;

    const jsInitializer = await fetchAndInvokeInitializers();

    initializeRemoteWebView();

    DotNet.attachDispatcher({
        beginInvokeDotNetFromJS: sendBeginInvokeDotNetFromJS,
        endInvokeJSFromDotNet: sendEndInvokeJSFromDotNet,
        sendByteArray: sendByteArray,
    });

    navigationManagerFunctions.enableNavigationInterception();
    navigationManagerFunctions.listenForNavigationEvents(sendLocationChanged);

    sendAttachPage(navigationManagerFunctions.getBaseURI(), navigationManagerFunctions.getLocationHref());

    await jsInitializer.invokeAfterStartedCallbacks(Blazor);
}

Blazor.start = boot;

if (shouldAutoStart()) {
    boot();
}
