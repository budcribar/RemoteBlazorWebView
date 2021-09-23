using System;

namespace PeakSWC.RemoteWebView
{
	public class RefreshedEventArgs : EventArgs
	{
		public Guid Id { get; }
		public Uri Url { get; }

		public RefreshedEventArgs(Guid id, Uri url)
		{
			Id = id;
			Url = url;
		}
	}
}
