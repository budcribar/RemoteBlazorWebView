using System;

namespace PeakSWC.RemoteWebView
{
	public class ConnectedEventArgs : EventArgs
	{
		public Guid Id { get; }
		public Uri Url { get; }

		public string IpAddress { get; }

		public string User { get; }

		public ConnectedEventArgs(Guid id, Uri url, string ipAddress, string user)
		{
			Id = id;
			Url = url;
			IpAddress = ipAddress;
			User = user;
		}
	}
}
