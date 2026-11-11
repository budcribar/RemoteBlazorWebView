using System.Runtime.CompilerServices;

namespace PeakSWC.RemoteWebView
{
    // Compiled into every configuration but NoAuthorization (the native relay): see RemoteWebViewService.csproj.
    internal static class AzureB2CRegistration
    {
        /// <summary>Makes "Authorization:Provider": "AzureB2C" available before the relay starts.</summary>
        [ModuleInitializer]
        internal static void Register() =>
            RelayAuthorization.Register(AzureB2CAuthorization.ProviderName, () => new AzureB2CAuthorization());
    }
}
