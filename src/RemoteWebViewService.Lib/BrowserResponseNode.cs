using Grpc.Core;

namespace PeakSWC.RemoteWebView
{
    public class BrowserResponseNode(IServerStreamWriter<StringRequest> streamWriter, string clientId, bool isPrimary)
    {
        public IServerStreamWriter<StringRequest> StreamWriter { get; set; } = streamWriter;
        public string ClientId { get; set; } = clientId;
        public bool IsPrimary { get; set; } = isPrimary;

        /// <summary>Order in which connections joined (process-wide), so the roster lists them oldest first.</summary>
        public long JoinOrder { get; } = System.Threading.Interlocked.Increment(ref joinCounter);
        private static long joinCounter;
    }
}
