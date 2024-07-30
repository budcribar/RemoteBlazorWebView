using System;
using PeakSWC.RemoteBlazorWebView.WindowsForms;
using PeakSWC.RemoteBlazorWebView.Wpf;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if WEBVIEW2_WINFORMS
#elif WEBVIEW2_WPF
#elif WEBVIEW2_MAUI
#else
#error Must define WEBVIEW2_WINFORMS, WEBVIEW2_WPF, WEBVIEW2_MAUI
#endif

namespace PeakSWC.RemoteBlazorWebView
{
	/// <summary>
	/// Extension methods to <see cref="IServiceCollection"/>.
	/// </summary>
	public static class BlazorWebViewServiceCollectionExtensions
	{
		/// <summary>
		/// Configures <see cref="IServiceCollection"/> to add support for <see cref="BlazorWebView"/>.
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/>.</param>
		/// <returns>The <see cref="IServiceCollection"/>.</returns>
		public static IWindowsFormsBlazorWebViewBuilder AddRemoteWindowsFormsBlazorWebView(this IServiceCollection services)
		public static IWpfBlazorWebViewBuilder AddRemoteWpfBlazorWebView(this IServiceCollection services)
#if ANDROID
	    [System.Runtime.Versioning.SupportedOSPlatform("android23.0")]
#elif IOS
		[System.Runtime.Versioning.SupportedOSPlatform("ios11.0")]
		public static IMauiBlazorWebViewBuilder AddMauiBlazorWebView(this IServiceCollection services)
		{
			services.AddBlazorWebView();
			services.TryAddSingleton(new BlazorWebViewDeveloperTools { Enabled = false });
#if WEBVIEW2_MAUI
			services.TryAddSingleton(_ => new MauiBlazorMarkerService());
			services.ConfigureMauiHandlers(static handlers => handlers.AddHandler<IBlazorWebView>(_ => new BlazorWebViewHandler()));
			return new MauiBlazorWebViewBuilder(services);
#elif WEBVIEW2_WINFORMS
			services.TryAddSingleton(_ => new WindowsFormsBlazorMarkerService());
			return new WindowsFormsBlazorWebViewBuilder(services);
			services.TryAddSingleton(_ => new WpfBlazorMarkerService());
			return new WpfBlazorWebViewBuilder(services);
		}
		/// Enables Developer tools on the underlying WebView controls.
		public static IServiceCollection AddRemoteBlazorWebViewDeveloperTools(this IServiceCollection services)
			return services.AddSingleton<BlazorWebViewDeveloperTools>(new BlazorWebViewDeveloperTools { Enabled = true });
	}
}