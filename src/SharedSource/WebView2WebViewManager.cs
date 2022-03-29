// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if !WEBVIEW2_WINFORMS && !WEBVIEW2_WPF && !WEBVIEW2_MAUI
#error Must specify which WebView2 is targeted
#endif

#if WINDOWS

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.FileProviders;
#if WEBVIEW2_WINFORMS
using Microsoft.Web.WebView2;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.WinForms.WebView2;
#elif WEBVIEW2_WPF
using Microsoft.Web.WebView2;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
#elif WEBVIEW2_MAUI
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;
using System.Runtime.InteropServices.WindowsRuntime;
//using Windows.Storage.Streams;
#endif

namespace PeakSWC.RemoteBlazorWebView
{
	/// <summary>
	/// An implementation of <see cref="WebViewManager"/> that uses the Edge WebView2 browser control
	/// to render web content.
	/// </summary>
	public class WebView2WebViewManager : WebViewManager
	{
		// Using an IP address means that WebView2 doesn't wait for any DNS resolution,
		// making it substantially faster. Note that this isn't real HTTP traffic, since
		// we intercept all the requests within this origin.
		private protected const string AppOrigin = "https://0.0.0.0/";

		private readonly WebView2Control _webview;
		private readonly Task _webviewReadyTask;
#if WEBVIEW2_WINFORMS || WEBVIEW2_WPF
		private protected CoreWebView2Environment _coreWebView2Environment;
#elif WEBVIEW2_MAUI
		private protected CoreWebView2Environment? _coreWebView2Environment;
#endif

		/// <summary>
		/// Constructs an instance of <see cref="WebView2WebViewManager"/>.
		/// </summary>
		/// <param name="webview">A <see cref="WebView2Control"/> to access platform-specific WebView2 APIs.</param>
		/// <param name="services">A service provider containing services to be used by this class and also by application code.</param>
		/// <param name="dispatcher">A <see cref="Dispatcher"/> instance that can marshal calls to the required thread or sync context.</param>
		/// <param name="fileProvider">Provides static content to the webview.</param>
		/// <param name="hostPageRelativePath">Path to the host page within the <paramref name="fileProvider"/>.</param>
		public WebView2WebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore jsComponents, string hostPageRelativePath)
			: base(services, dispatcher, new Uri(AppOrigin), fileProvider, jsComponents, hostPageRelativePath)
		{
			_webview = webview ?? throw new ArgumentNullException(nameof(webview));

			// Unfortunately the CoreWebView2 can only be instantiated asynchronously.
			// We want the external API to behave as if initalization is synchronous,
			// so keep track of a task we can await during LoadUri.
			_webviewReadyTask = InitializeWebView2();
		}

		/// <inheritdoc />
		protected override void NavigateCore(Uri absoluteUri)
		{
			_ = Dispatcher.InvokeAsync(async () =>
			{
				await _webviewReadyTask;
				_webview.Source = absoluteUri;
			});
		}

		/// <inheritdoc />
		public void NavigateToString(string htmlContent)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await _webviewReadyTask;
                _webview.NavigateToString(htmlContent);
            });
        }
        protected override void SendMessage(string message)
			=> _webview.CoreWebView2.PostWebMessageAsString(message);

		private async Task InitializeWebView2()
		{
			_coreWebView2Environment = await CoreWebView2Environment.CreateAsync()
#if WEBVIEW2_MAUI
				.AsTask()
#endif
				.ConfigureAwait(true);
			await _webview.EnsureCoreWebView2Async();
			ApplyDefaultWebViewSettings();

			_webview.CoreWebView2.AddWebResourceRequestedFilter($"{AppOrigin}*", CoreWebView2WebResourceContext.All);
			_webview.CoreWebView2.WebResourceRequested += async (s, eventArgs) =>
			{
				await HandleWebResourceRequest(eventArgs);
			};

			// The code inside blazor.webview.js is meant to be agnostic to specific webview technologies,
			// so the following is an adaptor from blazor.webview.js conventions to WebView2 APIs
			await _webview.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                window.external = {
                    sendMessage: message => {
                        window.chrome.webview.postMessage(message);
                    },
                    receiveMessage: callback => {
                        window.chrome.webview.addEventListener('message', e => callback(e.data));
                    }
                };
            ")
#if WEBVIEW2_MAUI
				.AsTask()
#endif
				.ConfigureAwait(true);

			QueueBlazorStart();

			_webview.CoreWebView2.WebMessageReceived += (s, e) => MessageReceived(new Uri(e.Source), e.TryGetWebMessageAsString());
		}

		protected virtual Task HandleWebResourceRequest(CoreWebView2WebResourceRequestedEventArgs eventArgs)
		{
#if WEBVIEW2_WINFORMS || WEBVIEW2_WPF
			// Unlike server-side code, we get told exactly why the browser is making the request,
			// so we can be smarter about fallback. We can ensure that 'fetch' requests never result
			// in fallback, for example.
			var allowFallbackOnHostPage =
				eventArgs.ResourceContext == CoreWebView2WebResourceContext.Document ||
				eventArgs.ResourceContext == CoreWebView2WebResourceContext.Other; // e.g., dev tools requesting page source

			var requestUri = QueryStringHelper.RemovePossibleQueryString(eventArgs.Request.Uri);

			if (TryGetResponseContent(requestUri, allowFallbackOnHostPage, out var statusCode, out var statusMessage, out var content, out var headers))
			{
				var headerString = GetHeaderString(headers);

				eventArgs.Response = _coreWebView2Environment.CreateWebResourceResponse(content, statusCode, statusMessage, headerString);
			}
#elif WEBVIEW2_MAUI
			// No-op here because all the work is done in the derived WinUIWebViewManager
#endif
			return Task.CompletedTask;
		}

		/// <summary>
		/// Override this method to queue a call to Blazor.start(). Not all platforms require this.
		/// </summary>
		protected virtual void QueueBlazorStart()
		{
		}

		private protected static string GetHeaderString(IDictionary<string, string> headers) =>
			string.Join(Environment.NewLine, headers.Select(kvp => $"{kvp.Key}: {kvp.Value}"));

		private void ApplyDefaultWebViewSettings()
		{
			// Desktop applications typically don't want the default web browser context menu
			_webview.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

			// Desktop applications almost never want to show a URL preview when hovering over a link
			_webview.CoreWebView2.Settings.IsStatusBarEnabled = false;
		}
	}
}

#endif
