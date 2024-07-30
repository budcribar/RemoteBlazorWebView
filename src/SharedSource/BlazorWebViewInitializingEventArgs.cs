using System;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.WinForms.WebView2;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;
using AWebView = Android.Webkit.WebView;
using WebKit;
using TWebView = Tizen.WebView.WebView;
#if WEBVIEW2_WINFORMS
#elif WEBVIEW2_WPF
#elif WINDOWS && WEBVIEW2_MAUI
#elif ANDROID
#elif IOS || MACCATALYST
#elif TIZEN
#endif

namespace PeakSWC.RemoteBlazorWebView
{
	/// <summary>
	/// Allows configuring the underlying web view when the application is initializing.
	/// </summary>
	public class BlazorWebViewInitializingEventArgs : EventArgs
	{
#nullable disable
#if WINDOWS
		/// <summary>
		/// Gets or sets the browser executable folder path for the <see cref="WebView2Control"/>.
		/// </summary>
		public string BrowserExecutableFolder { get; set; }
		/// Gets or sets the user data folder path for the <see cref="WebView2Control"/>.
		public string UserDataFolder { get; set; }
		/// Gets or sets the environment options for the <see cref="WebView2Control"/>.
		public CoreWebView2EnvironmentOptions EnvironmentOptions { get; set; }
#elif MACCATALYST || IOS
		/// Gets or sets the web view <see cref="WKWebViewConfiguration"/>.
		public WKWebViewConfiguration Configuration { get; set; }
	}
}