// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace PeakSWC.RemoteBlazorWebView.Windows
{
    // Shared interop constants
    internal static class BrowserNavigationManagerInterop
    {
        private static readonly string Prefix = "Blazor._internal.navigationManager.";

        public static readonly string EnableNavigationInterception = Prefix + "enableNavigationInterception";

        public static readonly string GetLocationHref = Prefix + "getUnmarshalledLocationHref";

        public static readonly string GetBaseUri = Prefix + "getUnmarshalledBaseURI";

        public static readonly string NavigateTo = Prefix + "navigateTo";
    }
}
