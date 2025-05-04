using System;

namespace PeakSWC.RemoteWebView
{
	public class ConnectedEventArgs(Guid id, Uri url, string ipAddress, string user) : EventArgs
	{
        public Guid Id { get; } = id;
        public Uri Url { get; } = url;

        public string IpAddress { get; } = ipAddress;

        public string User { get; } = user;
    }
}
