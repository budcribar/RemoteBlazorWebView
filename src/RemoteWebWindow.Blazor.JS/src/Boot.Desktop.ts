import { DotNet } from '@microsoft/dotnet-js-interop';
import { Blazor } from '../upstream/aspnetcore/web.js/src/GlobalExports';
import { shouldAutoStart } from '../upstream/aspnetcore/web.js/src/BootCommon';
import { internalFunctions as navigationManagerFunctions } from '../upstream/aspnetcore/web.js/src/Services/NavigationManager';
import { setEventDispatcher } from '../upstream/aspnetcore/web.js/src/Rendering/Events/EventDispatcher';
import { sendBrowserEvent, sendAttachPage, sendBeginInvokeDotNetFromJS, sendEndInvokeJSFromDotNet, sendLocationChanged } from '../upstream/aspnetcore/web.js/src/Platform/WebView/WebViewIpcSender';
import { InputFile } from '../upstream/aspnetcore/web.js/src//InputFile';

import * as ipc from './IPC';
import { initializeRemoteWebWindow } from './RemoteWebWindow';


let started = false;

async function boot(): Promise<void> {
    if (started) {
        throw new Error('Blazor has already started.');
    }

    started = true;

    initializeRemoteWebWindow();

    DotNet.attachDispatcher({
        beginInvokeDotNetFromJS: sendBeginInvokeDotNetFromJS,
        endInvokeJSFromDotNet: sendEndInvokeJSFromDotNet,
    });

    Blazor._internal.InputFile = InputFile;
    navigationManagerFunctions.enableNavigationInterception();
    navigationManagerFunctions.listenForNavigationEvents(sendLocationChanged);

    sendAttachPage(navigationManagerFunctions.getBaseURI(), navigationManagerFunctions.getLocationHref());
   

}

setEventDispatcher(sendBrowserEvent);

Blazor.start = boot;

if (shouldAutoStart()) {
    boot();
}
