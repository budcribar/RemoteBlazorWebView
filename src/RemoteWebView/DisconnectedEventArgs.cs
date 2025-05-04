using System;

namespace PeakSWC.RemoteWebView
{
	public class DisconnectedEventArgs(Guid id, Uri url, Exception exception) : EventArgs
	{
        public Guid Id { get; } = id;
        public Uri Url { get; } = url;
        public Exception Exception { get; } = exception;
    }
}
