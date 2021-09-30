using System;

namespace PeakSWC.RemoteWebView
{
	public class DisconnectedEventArgs : EventArgs
	{
		public Guid Id { get; }
		public Uri Url { get; }
		public Exception Exception {  get; }

		public DisconnectedEventArgs(Guid id, Uri url, Exception exception)
		{
			Id = id;
			Url = url;
			Exception = exception;
		}
	}
}
