using System;

namespace PeakSWC.RemoteBlazorWebView
{
	/// <summary>
	/// Used to provide information about a link (<![CDATA[<a>]]>) clicked within a Blazor WebView.
	/// <para>
	/// Anchor tags with target="_blank" will always open in the default
	/// browser and the UrlLoading event won't be called.
	/// </para>
	/// </summary>
	public class UrlLoadingEventArgs : EventArgs
	{
		public static UrlLoadingEventArgs CreateWithDefaultLoadingStrategy(Uri urlToLoad, Uri appOriginUri)
		{
			var split = urlToLoad.AbsolutePath.Split('/');
			var isMirrorUrl = split.Length == 3 && split[1] == "mirror" && Guid.TryParse(split[2], out Guid _);
			var strategy = (appOriginUri.IsBaseOf(urlToLoad) || urlToLoad.Scheme == "data" || isMirrorUrl) ?
				UrlLoadingStrategy.OpenInWebView :
				UrlLoadingStrategy.OpenExternally;

			return new(urlToLoad, strategy);
		}

		private UrlLoadingEventArgs(Uri url, UrlLoadingStrategy urlLoadingStrategy)
		{
			Url = url;
			UrlLoadingStrategy = urlLoadingStrategy;
		}

		/// <summary>
		/// Gets the <see cref="Url">URL</see> to be loaded.
		/// </summary>
		public Uri Url { get; }

		/// <summary>
		/// The policy to use when loading links from the webview.
		/// Defaults to <see cref="UrlLoadingStrategy.OpenExternally"/> unless <see cref="Url"/> has a host
		/// matching the app origin, in which case the default becomes <see cref="UrlLoadingStrategy.OpenInWebView"/>.
		/// <para>
		/// This value should not be changed to <see cref="UrlLoadingStrategy.OpenInWebView"/> for external links
		/// unless you can ensure they are fully trusted.
		/// </para>
		/// </summary>
		public UrlLoadingStrategy UrlLoadingStrategy { get; set; }
	}
}
