using System.Security.Claims;

namespace PeakSWC.RemoteWebView
{
    internal static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Same lookup order as Microsoft.Identity.Web's GetDisplayName (preferred_username, then the identity's name
        /// claim, then "name"), kept here so the library doesn't need Microsoft.Identity.Web unless AUTHORIZATION is on.
        /// </summary>
        public static string? GetUserDisplayName(this ClaimsPrincipal principal)
        {
            string? name = principal.FindFirst("preferred_username")?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            name = principal.FindFirst(ClaimsIdentity.DefaultNameClaimType)?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return principal.FindFirst("name")?.Value;
        }
    }
}
