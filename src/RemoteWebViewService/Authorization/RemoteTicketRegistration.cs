using System.Runtime.CompilerServices;

namespace PeakSWC.RemoteWebView
{
    // Compiled into every configuration but NoAuthorization (the native relay): see RemoteWebViewService.csproj.
    internal static class RemoteTicketRegistration
    {
        /// <summary>Makes "Authorization:Provider": "RemoteTicket" available before the relay starts.</summary>
        [ModuleInitializer]
        internal static void Register() =>
            RelayAuthorization.Register(RemoteTicketAuthorization.ProviderName, () => new RemoteTicketAuthorization());
    }
}
