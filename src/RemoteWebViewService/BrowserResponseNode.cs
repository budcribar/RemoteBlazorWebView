using Grpc.Core;

namespace PeakSWC.RemoteWebView
{
    public class BrowserResponseNode(IServerStreamWriter<StringRequest> streamWriter, string clientId, bool isPrimary)
    {
        public IServerStreamWriter<StringRequest> StreamWriter { get; set; } = streamWriter;
        public string ClientId { get; set; } = clientId;
        public bool IsPrimary { get; set; } = isPrimary;
    }
}
