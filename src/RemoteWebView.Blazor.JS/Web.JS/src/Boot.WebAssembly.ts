// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

/* eslint-disable array-element-newline */
import { DotNet } from '@microsoft/dotnet-js-interop';
import { Blazor } from './GlobalExports';
import * as Environment from './Environment';
import { Module, BINDING, monoPlatform } from './Platform/Mono/MonoPlatform';
import { renderBatch, getRendererer } from './Rendering/Renderer';
import { SharedMemoryRenderBatch } from './Rendering/RenderBatch/SharedMemoryRenderBatch';
import { shouldAutoStart } from './BootCommon';
import { WebAssemblyResourceLoader } from './Platform/WebAssemblyResourceLoader';
import { Pointer } from './Platform/Platform';
import { WebAssemblyStartOptions } from './Platform/WebAssemblyStartOptions';
import { setDispatchEventMiddleware } from './Rendering/WebRendererInteropMethods';
import { JSInitializer } from './JSInitializers/JSInitializers';

let started = false;

async function boot(options?: Partial<WebAssemblyStartOptions>): Promise<void> {

  if (started) {
    throw new Error('Blazor has already started.');
  }
  started = true;

  if (inAuthRedirectIframe()) {
    // eslint-disable-next-line @typescript-eslint/no-empty-function
    await new Promise(() => { }); // See inAuthRedirectIframe for explanation
  }

  setDispatchEventMiddleware((browserRendererId, eventHandlerId, continuation) => {
    // It's extremely unusual, but an event can be raised while we're in the middle of synchronously applying a
    // renderbatch. For example, a renderbatch might mutate the DOM in such a way as to cause an <input> to lose
    // focus, in turn triggering a 'change' event. It may also be possible to listen to other DOM mutation events
    // that are themselves triggered by the application of a renderbatch.
    const renderer = getRendererer(browserRendererId);
    if (renderer.eventDelegator.getHandler(eventHandlerId)) {
      monoPlatform.invokeWhenHeapUnlocked(continuation);
    }
  });

  Blazor._internal.applyHotReload = (id: string, metadataDelta: string, ilDelta: string, pdbDelta: string | undefined) => {
    DotNet.invokeMethod('Microsoft.AspNetCore.Components.WebAssembly', 'ApplyHotReloadDelta', id, metadataDelta, ilDelta, pdbDelta);
  };

  Blazor._internal.getApplyUpdateCapabilities = () => DotNet.invokeMethod('Microsoft.AspNetCore.Components.WebAssembly', 'GetApplyUpdateCapabilities');

  // Configure JS interop
  Blazor._internal.invokeJSFromDotNet = invokeJSFromDotNet;
  Blazor._internal.invokeJSJson = invokeJSJson;
  Blazor._internal.endInvokeDotNetFromJS = endInvokeDotNetFromJS;
  Blazor._internal.receiveByteArray = receiveByteArray;

  // Configure environment for execution under Mono WebAssembly with shared-memory rendering
  const platform = Environment.setPlatform(monoPlatform);
  Blazor.platform = platform;
  Blazor._internal.renderBatch = (browserRendererId: number, batchAddress: Pointer) => {
    // We're going to read directly from the .NET memory heap, so indicate to the platform
    // that we don't want anything to modify the memory contents during this time. Currently this
    // is only guaranteed by the fact that .NET code doesn't run during this time, but in the
    // future (when multithreading is implemented) we might need the .NET runtime to understand
    // that GC compaction isn't allowed during this critical section.
    const heapLock = monoPlatform.beginHeapLock();
    try {
      renderBatch(browserRendererId, new SharedMemoryRenderBatch(batchAddress));
    } finally {
      heapLock.release();
    }
  };

  Blazor._internal.navigationManager.listenForNavigationEvents(async (uri: string, state: string | undefined, intercepted: boolean): Promise<void> => {
    await DotNet.invokeMethodAsync(
      'Microsoft.AspNetCore.Components.WebAssembly',
      'NotifyLocationChanged',
      uri,
      state,
      intercepted
    );
  }, async (callId: number, uri: string, state: string | undefined, intercepted: boolean): Promise<void> => {
    const shouldContinueNavigation = await DotNet.invokeMethodAsync<boolean>(
      'Microsoft.AspNetCore.Components.WebAssembly',
      'NotifyLocationChangingAsync',
      uri,
      state,
      intercepted
    );

    Blazor._internal.navigationManager.endLocationChanging(callId, shouldContinueNavigation);
  });

  let resourceLoader: WebAssemblyResourceLoader;
  let jsInitializer: JSInitializer;
  try {
    const api = await platform.start(options ?? {});
    resourceLoader = api.resourceLoader;
    jsInitializer = api.jsInitializer;
  } catch (ex) {
    throw new Error(`Failed to start platform. Reason: ${ex}`);
  }

  // Start up the application
  platform.callEntryPoint(resourceLoader.bootConfig.entryAssembly);
  // At this point .NET has been initialized (and has yielded), we can't await the promise becasue it will
  // only end when the app finishes running
  jsInitializer.invokeAfterStartedCallbacks(Blazor);
}

// obsolete, legacy, don't use for new code!
function invokeJSFromDotNet(callInfo: Pointer, arg0: any, arg1: any, arg2: any): any {
  const functionIdentifier = monoPlatform.readStringField(callInfo, 0)!;
  const resultType = monoPlatform.readInt32Field(callInfo, 4);
  const marshalledCallArgsJson = monoPlatform.readStringField(callInfo, 8);
  const targetInstanceId = monoPlatform.readUint64Field(callInfo, 20);

  if (marshalledCallArgsJson !== null) {
    const marshalledCallAsyncHandle = monoPlatform.readUint64Field(callInfo, 12);

    if (marshalledCallAsyncHandle !== 0) {
      DotNet.jsCallDispatcher.beginInvokeJSFromDotNet(marshalledCallAsyncHandle, functionIdentifier, marshalledCallArgsJson, resultType, targetInstanceId);
      return 0;
    } else {
      const resultJson = DotNet.jsCallDispatcher.invokeJSFromDotNet(functionIdentifier, marshalledCallArgsJson, resultType, targetInstanceId)!;
      return resultJson === null ? 0 : BINDING.js_string_to_mono_string(resultJson);
    }
  } else {
    const func = DotNet.jsCallDispatcher.findJSFunction(functionIdentifier, targetInstanceId);
    const result = func.call(null, arg0, arg1, arg2);

    switch (resultType) {
      case DotNet.JSCallResultType.Default:
        return result;
      case DotNet.JSCallResultType.JSObjectReference:
        return DotNet.createJSObjectReference(result).__jsObjectId;
      case DotNet.JSCallResultType.JSStreamReference: {
        const streamReference = DotNet.createJSStreamReference(result);
        const resultJson = JSON.stringify(streamReference);
        return BINDING.js_string_to_mono_string(resultJson);
      }
      case DotNet.JSCallResultType.JSVoidResult:
        return null;
      default:
        throw new Error(`Invalid JS call result type '${resultType}'.`);
    }
  }
}

function invokeJSJson(identifier: string, targetInstanceId: number, resultType: number, argsJson: string, asyncHandle: number): string | null {
  if (asyncHandle !== 0) {
    DotNet.jsCallDispatcher.beginInvokeJSFromDotNet(asyncHandle, identifier, argsJson, resultType, targetInstanceId);
    return null;
  } else {
    return DotNet.jsCallDispatcher.invokeJSFromDotNet(identifier, argsJson, resultType, targetInstanceId);
  }
}

function endInvokeDotNetFromJS(callId: string, success: boolean, resultJsonOrErrorMessage: string): void {
  DotNet.jsCallDispatcher.endInvokeDotNetFromJS(callId, success, resultJsonOrErrorMessage);
}

function receiveByteArray(id: number, data: Uint8Array): void {
  DotNet.jsCallDispatcher.receiveByteArray(id, data);
}

function inAuthRedirectIframe(): boolean {
  // We don't want the .NET runtime to start up a second time inside the AuthenticationService.ts iframe. It uses resources
  // unnecessarily and can lead to errors (#37355), plus the behavior is not well defined as the frame will be terminated shortly.
  // So, if we're in that situation, block the startup process indefinitely so that anything chained to Blazor.start never happens.
  // The detection logic here is based on the equivalent check in AuthenticationService.ts.
  // TODO: Later we want AuthenticationService.ts to become responsible for doing this via a JS initializer. Doing it here is a
  //       tactical fix for .NET 6 so we don't have to change how authentication is initialized.
  if (window.parent !== window && !window.opener && window.frameElement) {
    const settingsJson = window.sessionStorage && window.sessionStorage['Microsoft.AspNetCore.Components.WebAssembly.Authentication.CachedAuthSettings'];
    const settings = settingsJson && JSON.parse(settingsJson);
    return settings && settings.redirect_uri && location.href.startsWith(settings.redirect_uri);
  }

  return false;
}

Blazor.start = boot;
if (shouldAutoStart()) {
  boot().catch(error => {
    if (typeof Module !== 'undefined' && Module.printErr) {
      // Logs it, and causes the error UI to appear
      Module.printErr(error);
    } else {
      // The error must have happened so early we didn't yet set up the error UI, so just log to console
      console.error(error);
    }
  });
}
