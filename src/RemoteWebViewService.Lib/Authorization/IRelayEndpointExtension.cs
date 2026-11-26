using Microsoft.AspNetCore.Routing;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Extra HTTP routes a sign-in provider owns (shop signup, hosted certificates). The relay calls this from
    /// its endpoint map when the active <see cref="IRelayAuthorization"/> implements it. No reflection.
    /// </summary>
    public interface IRelayEndpointExtension
    {
        void MapEndpoints(IEndpointRouteBuilder endpoints);
    }
}
