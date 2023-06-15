// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { DotNet } from '@microsoft/dotnet-js-interop';
import { EventDescriptor } from './Events/EventDelegator';
import { enableJSRootComponents, JSComponentParametersByIdentifier, JSComponentIdentifiersByInitializer } from './JSRootComponents';

const interopMethodsByRendererId: DotNet.DotNetObject[] = [];

let resolveRendererAttached : () => void;

export const rendererAttached = new Promise<void>((resolve) => {
  resolveRendererAttached = resolve;
});

export function attachWebRendererInterop(
  interopMethods: DotNet.DotNetObject,
  jsComponentParameters: JSComponentParametersByIdentifier,
  jsComponentInitializers: JSComponentIdentifiersByInitializer,
): number {
  const rendererId = interopMethodsByRendererId.length;
  interopMethodsByRendererId.push(interopMethods);

  if (Object.keys(jsComponentParameters).length > 0) {
    const manager = getInteropMethods(rendererId);
    enableJSRootComponents(manager, jsComponentParameters, jsComponentInitializers);
  }

  resolveRendererAttached();
  return rendererId;
}

export function dispatchEvent(browserRendererId: number, eventDescriptor: EventDescriptor, eventArgs: any): void {
  return dispatchEventMiddleware(browserRendererId, eventDescriptor.eventHandlerId, () => {
    const interopMethods = getInteropMethods(browserRendererId);
    return interopMethods.invokeMethodAsync('DispatchEventAsync', eventDescriptor, eventArgs);
  });
}

function getInteropMethods(rendererId: number): DotNet.DotNetObject {
  const interopMethods = interopMethodsByRendererId[rendererId];
  if (!interopMethods) {
    throw new Error(`No interop methods are registered for renderer ${rendererId}`);
  }

  return interopMethods;
}

// On some hosting platforms, we may need to defer the event dispatch, so they can register this middleware to do so
type DispatchEventMiddlware = (browserRendererId: number, eventHandlerId: number, continuation: () => void) => void;

let dispatchEventMiddleware: DispatchEventMiddlware = (browserRendererId, eventHandlerId, continuation) => continuation();
export function addDispatchEventMiddleware(middleware: DispatchEventMiddlware): void {
  const next = dispatchEventMiddleware;
  dispatchEventMiddleware = (browserRendererId, eventHandlerId, continuation) => {
    middleware(browserRendererId, eventHandlerId, () => next(browserRendererId, eventHandlerId, continuation));
  };
}
