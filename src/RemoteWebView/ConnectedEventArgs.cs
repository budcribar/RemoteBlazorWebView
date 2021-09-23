using System;

namespace PeakSWC.RemoteWebView
{
	public class ConnectedEventArgs : EventArgs
	{
		public Guid Id { get; }
		public Uri Url { get; }

		public ConnectedEventArgs(Guid id, Uri url)
		{
			Id = id;
			Url = url;
		}
	}
}
