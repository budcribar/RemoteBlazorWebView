using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace PeakSWC.RemoteBlazorWebView.Maui
{
	public static class BlazorWebViewRegistrationExtensions
	{
		public static MauiAppBuilder RegisterBlazorMauiWebView(this MauiAppBuilder appHostBuilder)
		{
			if (appHostBuilder is null)
			{
				throw new ArgumentNullException(nameof(appHostBuilder));
			}

			appHostBuilder.ConfigureMauiHandlers(handlers => handlers.AddHandler<IBlazorWebView, BlazorWebViewHandler>());

			return appHostBuilder;
		}
	}
}
