using System;
using Microsoft.Extensions.FileProviders;
using Microsoft.Maui.Handlers;

namespace PeakSWC.RemoteBlazorWebView.Maui
{
	public partial class BlazorWebViewHandler : ViewHandler<IBlazorWebView, object>
	{
		protected override object CreateNativeView() => throw new NotImplementedException();

		public virtual IFileProvider CreateFileProvider(string contentRootDir) => throw new NotImplementedException();
	}
}