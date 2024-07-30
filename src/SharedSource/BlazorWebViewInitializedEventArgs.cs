using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.WinForms.WebView2;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.UI.Xaml.Controls.WebView2;
using AWebView = Android.Webkit.WebView;
using WebKit;
using TWebView = Tizen.NUI.BaseComponents.WebView;
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
	/// Allows configuring the underlying web view after it has been initialized.
	/// </summary>
	public class BlazorWebViewInitializedEventArgs : EventArgs
	{
#nullable disable
#if WINDOWS
		/// <summary>
		/// Gets the <see cref="WebView2Control"/> instance that was initialized.
		/// </summary>
		public WebView2Control WebView { get; internal set; }
		/// Gets the <see cref="AWebView"/> instance that was initialized.
		public AWebView WebView { get; internal set; }
#elif MACCATALYST || IOS
		/// Gets the <see cref="WKWebView"/> instance that was initialized.
		/// the default values to allow further configuring additional options.
		public WKWebView WebView { get; internal set; }
		/// Gets the <see cref="TWebView"/> instance that was initialized.
		public TWebView WebView { get; internal set; }
	}
}