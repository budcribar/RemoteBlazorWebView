using System;

namespace PeakSWC.RemoteWebView
{
	public class ReadyToConnectEventArgs : EventArgs
	{
		public Guid Id { get; }
		public Uri Url { get; }

		public ReadyToConnectEventArgs(Guid id, Uri url)
		{
			Id = id;
			Url = url;
		}
	}
}
