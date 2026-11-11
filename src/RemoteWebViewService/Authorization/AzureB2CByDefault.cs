using System.Runtime.CompilerServices;

namespace PeakSWC.RemoteWebView
{
    // Compiled into the Authorization configuration only: see RemoteWebViewService.csproj.
    internal static class AzureB2CByDefault
    {
        /// <summary>A relay built with -c Authorization requires Azure AD B2C sign-in unless the
        /// "Authorization:Provider" setting says otherwise, as that configuration always has.</summary>
        [ModuleInitializer]
        internal static void Set() => RelayAuthorization.DefaultProvider = AzureB2CAuthorization.ProviderName;
    }
}
