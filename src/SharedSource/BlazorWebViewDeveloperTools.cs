using System;

#if WEBVIEW2_WINFORMS
namespace PeakSWC.RemoteBlazorWebView.WindowsForms
#elif WEBVIEW2_WPF
namespace PeakSWC.RemoteBlazorWebView.Wpf
#elif WEBVIEW2_MAUI
namespace PeakSWC.RemoteBlazorWebView.Maui
#else
#error Must define WEBVIEW2_WINFORMS, WEBVIEW2_WPF, WEBVIEW2_MAUI
#endif
{
	public class BlazorWebViewDeveloperTools
{
	public bool Enabled { get; set; } = false;
}
}
