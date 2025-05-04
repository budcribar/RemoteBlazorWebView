using System;

namespace PeakSWC.RemoteWebView
{
	public class ReadyToConnectEventArgs(Guid id, Uri url) : EventArgs
	{
        public Guid Id { get; } = id;
        public Uri Url { get; } = url;
    }
}
