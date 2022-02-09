using System;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.FileProviders;

namespace PeakSWC.RemoteBlazorWebView.Maui
{
	public class BlazorWebViewBase : Microsoft.Maui.Controls.View, IBlazorWebView
	{
		private readonly JSComponentConfigurationStore _jSComponents = new();

		public BlazorWebViewBase()
		{
			RootComponents = new RootComponentsCollection(_jSComponents);
		}

		JSComponentConfigurationStore IBlazorWebView.JSComponents => _jSComponents;

		public string? HostPage { get; set; }

		public RootComponentsCollection RootComponents { get; }

		/// <inheritdoc/>
		public virtual IFileProvider CreateFileProvider(string contentRootDir)
		{
			// Call into the platform-specific code to get that platform's asset file provider
			return ((BlazorWebViewHandler)(Handler!)).CreateFileProvider(contentRootDir);
		}
	}
}
