using Grpc.Core;

namespace PeakSWC.RemoteWebView
{
    public class BrowserResponseNode
    {
        public IServerStreamWriter<StringRequest> StreamWriter { get;set; }
        public string ClientId { get; set; }
        public bool IsPrimary { get; set; }

        public BrowserResponseNode( IServerStreamWriter<StringRequest> streamWriter, string clientId, bool isPrimary)
        {
            StreamWriter = streamWriter;
            ClientId = clientId;
            IsPrimary = isPrimary;
        }

         
    }
}
