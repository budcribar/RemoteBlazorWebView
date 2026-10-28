// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.FileProviders;
using Photino.NET;
using Photino.Blazor;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteBlazorWebView;

namespace PeakSWC.RemoteWebView
{
    public class RemotePhotinoWebViewManager : PhotinoWebViewManager
    {
        Uri url;

        private RemoteWebView RemoteWebView { get; }
        private IBlazorWebView BlazorWebView { get; }

        public RemotePhotinoWebViewManager(RemoteBlazorWebViewWindow window, IServiceProvider provider, Dispatcher dispatcher, Uri appBaseUri, IFileProvider fileProvider, JSComponentConfigurationStore jsComponents, string hostPageRelativePath, ILogger logger)
            : base(window, provider, dispatcher, appBaseUri, fileProvider, jsComponents, hostPageRelativePath)
        {
            ArgumentNullException.ThrowIfNull(window);
            BlazorWebView = window;
            RemoteWebView = new RemoteWebView(
                window,
                hostPageRelativePath,
                dispatcher,
                new OptimizedCompositeFileProvider(new EmbeddedFileProvider(typeof(RemoteWebView).Assembly),StaticWebAssetsLoader.UseStaticWebAssets(fileProvider) ),
                logger
                );

            RemoteWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
            //RemoteWebView.Initialize();

            this.url = new Uri($"https://{HostAddressHelper.GetAppHostAddress()}/");
        }

        private void RemoteOnWebMessageReceived(object? sender, string e)
        {
            this.Dispatcher.InvokeAsync(() =>
            {
                MessageReceived(RemoteMessageUrl.Rewrite(sender?.ToString(), BlazorWebView.ServerUri, BlazorWebView.Id, RemoteWebView.HostHtmlPath, this.url), e);
            });
        }

        protected override void NavigateCore(Uri absoluteUri)
        {
            this.url = absoluteUri;
            RemoteWebView.NavigateToUrl(absoluteUri.AbsoluteUri);
        }

        protected override void SendMessage(string message)
        {
            RemoteWebView.SendMessage(message);
        }

    }
}
