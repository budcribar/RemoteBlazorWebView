import { DotNet } from '@microsoft/dotnet-js-interop';
import '@browserjs/GlobalExports';
import { setEventDispatcher } from '../upstream/aspnetcore/web.js/src/Rendering/Events/EventDispatcher';
//import { setEventDispatcher } from '@browserjs/Rendering/RendererEventDispatcher';
import { internalFunctions as navigationManagerFunctions } from '@browserjs/Services/NavigationManager';
import { decode } from 'base64-arraybuffer';
import * as ipc from './IPC';
import { RenderQueue } from './RenderQueue';
import { initializeRemotePhotino } from './RemoteWebWindow';
import { attachRootComponentToElement, attachRootComponentToLogicalElement } from '@browserjs/Rendering/Renderer';


function boot() {

    initializeRemotePhotino();

    setEventDispatcher((eventDescriptor, eventArgs) => DotNet.invokeMethodAsync('Photino.Blazor', 'DispatchEvent', eventDescriptor, JSON.stringify(eventArgs)));
    navigationManagerFunctions.listenForNavigationEvents((uri: string, intercepted: boolean) => {
        return DotNet.invokeMethodAsync('Photino.Blazor', 'NotifyLocationChanged', uri, intercepted);
    });
    const renderQueue = RenderQueue.getOrCreate();

    // Configure the mechanism for JS<->NET calls
    DotNet.attachDispatcher({
        beginInvokeDotNetFromJS: (callId: number, assemblyName: string | null, methodIdentifier: string, dotNetObjectId: number | null, argsJson: string) => {
            ipc.send('BeginInvokeDotNetFromJS', [callId ? callId.toString() : null, assemblyName, methodIdentifier, dotNetObjectId || 0, argsJson]);
        },
        endInvokeJSFromDotNet: (callId: number, succeeded: boolean, resultOrError: any) => {
            ipc.send('EndInvokeJSFromDotNet', [callId, succeeded, resultOrError]);
        }
    });

    window['Blazor']._internal.attachRootComponentToElement = attachRootComponentToElement;

    navigationManagerFunctions.enableNavigationInterception();

    ipc.on('JS.BeginInvokeJS', (asyncHandle, identifier, argsJson, resultType, targetInstanceId) => {
        DotNet.jsCallDispatcher.beginInvokeJSFromDotNet(asyncHandle, identifier, argsJson, resultType, targetInstanceId);
    });

    ipc.on('JS.EndInvokeDotNet', (callId, success, resultOrError) => {
        DotNet.jsCallDispatcher.endInvokeDotNetFromJS(callId, success, resultOrError);
    });

    ipc.on('JS.RenderBatch', (batchId, batchBase64) => {
        const batchData = new Uint8Array(decode(batchBase64));
        renderQueue.processBatch(batchId, batchData);
    });

    ipc.on('JS.Error', (message) => {
        console.error(message);
    });

    // Confirm that the JS side is ready for the app to start
    ipc.send('components:init', [
        navigationManagerFunctions.getLocationHref().replace(/\/index\.html$/, ''),
        navigationManagerFunctions.getBaseURI()]);
}

  
boot();
