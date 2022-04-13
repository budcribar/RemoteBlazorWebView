using System;
#if WEBVIEW2_WINFORMS
using PeakSWC.RemoteBlazorWebView.WindowsForms;
#elif WEBVIEW2_WPF
using PeakSWC.RemoteBlazorWebView.Wpf;
#elif WEBVIEW2_MAUI
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Hosting;
#else
#error Must define WEBVIEW2_WINFORMS, WEBVIEW2_WPF, WEBVIEW2_MAUI
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
#if WEBVIEW2_WINFORMS
		public static IServiceCollection AddWindowsFormsBlazorWebView(this IServiceCollection services)
#elif WEBVIEW2_WPF
		public static IServiceCollection AddWpfBlazorWebView(this IServiceCollection services)
#elif WEBVIEW2_MAUI
		public static IServiceCollection AddMauiBlazorWebView(this IServiceCollection services)
#else
#error Must define WEBVIEW2_WINFORMS, WEBVIEW2_WPF, WEBVIEW2_MAUI
#endif
		{
			services.AddBlazorWebView();
			services.TryAddSingleton(new BlazorWebViewDeveloperTools { Enabled = false });
#if WEBVIEW2_MAUI
			services.TryAddSingleton<MauiBlazorMarkerService>();
			services.ConfigureMauiHandlers(static handlers => handlers.AddHandler<IBlazorWebView, BlazorWebViewHandler>());
#elif WEBVIEW2_WINFORMS
			services.TryAddSingleton<WindowsFormsBlazorMarkerService>();
#elif WEBVIEW2_WPF
			services.TryAddSingleton<WpfBlazorMarkerService>();
#endif
			return services;
		}

		/// <summary>
		/// Enables Developer tools on the underlying WebView controls.
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/>.</param>
		/// <returns>The <see cref="IServiceCollection"/>.</returns>
		public static IServiceCollection AddBlazorWebViewDeveloperTools(this IServiceCollection services)
		{
			return services.AddSingleton<BlazorWebViewDeveloperTools>(new BlazorWebViewDeveloperTools { Enabled = true });
		}
	}
}
