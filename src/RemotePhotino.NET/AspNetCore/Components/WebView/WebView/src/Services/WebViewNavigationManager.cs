#nullable disable
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Components;

namespace PeakSWC.RemoteBlazorWebView.Windows
{
    internal class WebViewNavigationManager : NavigationManager
    {
        private IpcSender _ipcSender;

        public void AttachToWebView(IpcSender ipcSender, string baseUrl, string initialUrl)
        {
            _ipcSender = ipcSender;
            Initialize(baseUrl, initialUrl);
        }

        public void LocationUpdated(string newUrl, bool intercepted)
        {
            Uri = newUrl;
            NotifyLocationChanged(intercepted);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            _ipcSender.Navigate(uri, forceLoad);
        }
    }
}
