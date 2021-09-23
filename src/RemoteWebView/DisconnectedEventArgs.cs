using System;

namespace PeakSWC.RemoteWebView
{
	public class DisconnectedEventArgs : EventArgs
	{
		public Guid Id { get; }
		public Uri Url { get; }

		public DisconnectedEventArgs(Guid id, Uri url)
		{
			Id = id;
			Url = url;
		}
	}
}
