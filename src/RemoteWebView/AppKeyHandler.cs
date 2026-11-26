using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>Adds the session's app key (x-app-key) to every call the app makes to the relay.</summary>
    internal sealed class AppKeyHandler(string appKey) : DelegatingHandler
    {
        public const string HeaderName = "x-app-key";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove(HeaderName);
            request.Headers.TryAddWithoutValidation(HeaderName, appKey);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
