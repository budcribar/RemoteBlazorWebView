// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.FileProviders;
using PhotinoNET;
using Photino.Blazor;

namespace PeakSWC.RemoteWebView
{
    public class RemotePhotinoWebViewManager : PhotinoWebViewManager
    {
        Uri url;
        private readonly PhotinoWindow _window;

        private RemoteWebView RemoteWebView { get; }
        private IBlazorWebView BlazorWebView { get; }

        public RemotePhotinoWebViewManager(RemoteBlazorWebViewWindow window, IServiceProvider provider, Dispatcher dispatcher, Uri appBaseUri, IFileProvider fileProvider, JSComponentConfigurationStore jsComponents, string hostPageRelativePath)
            : base(window, provider, dispatcher, appBaseUri, fileProvider, jsComponents, hostPageRelativePath)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            BlazorWebView = window;
            RemoteWebView = new RemoteWebView(
                window,
                hostPageRelativePath,
                dispatcher,
                new CompositeFileProvider(StaticWebAssetsLoader.UseStaticWebAssets(fileProvider), new EmbeddedFileProvider(typeof(RemoteWebView).Assembly))
                );

            RemoteWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
            RemoteWebView.Initialize();

            this.url = new Uri("https://0.0.0.0/");
        }

        private void RemoteOnWebMessageReceived(object? sender, string e)
        {
            this.Dispatcher.InvokeAsync(() =>
            {
                var url = sender?.ToString() ?? "";
                if (BlazorWebView.ServerUri != null && url.StartsWith(BlazorWebView.ServerUri.ToString()))
                {
                    url = url.Replace(BlazorWebView.ServerUri.ToString(), this.url?.ToString() ?? "");
                    url = url.Replace(BlazorWebView.Id.ToString() + $"/", "");
                    if (url.EndsWith(RemoteWebView.HostHtmlPath)) url = url.Replace(RemoteWebView.HostHtmlPath, "");
                }

                MessageReceived(new Uri(url), e);
            });

        }

        protected override void NavigateCore(Uri absoluteUri)
        {
            string link = $"<a href='{BlazorWebView.ServerUri}app/{BlazorWebView.Id}'> link </a>";
            _window.LoadRawString(link);
            this.url = absoluteUri;
            RemoteWebView.NavigateToUrl(absoluteUri.AbsoluteUri);
        }

        protected override void SendMessage(string message)
        {
            RemoteWebView.SendMessage(message);
        }

    }
}
