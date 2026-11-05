// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { JSAsset, JSInitializer } from './JSInitializers';

export async function fetchAndInvokeInitializers() : Promise<JSInitializer> {
  const jsInitializersResponse = await fetch('_framework/blazor.modules.json', {
    method: 'GET',
    // RemoteBlazorWebView: 'same-origin' instead of upstream's 'include'. A page in a sandboxed iframe has an opaque origin, so
    // this request is cross-origin; a credentialed one can't be answered with the relay's "Access-Control-Allow-Origin: *".
    // Same-origin pages (the normal case) still send cookies.
    credentials: 'same-origin',
    cache: 'no-cache',
  });

  const initializers = (await jsInitializersResponse.json()).map(name => ({
    name,
  })) as JSAsset[];
  const jsInitializer = new JSInitializer();
  await jsInitializer.importInitializersAsync(initializers, []);
  return jsInitializer;
}
