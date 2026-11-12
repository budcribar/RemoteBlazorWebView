using System.Security.Claims;

namespace PeakSWC.RemoteWebView
{
    internal static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Same lookup order as Microsoft.Identity.Web's GetDisplayName (preferred_username, then the identity's name
        /// claim, then "name"), kept here so the library doesn't need Microsoft.Identity.Web.
        /// </summary>
        public static string? GetUserDisplayName(this ClaimsPrincipal principal)
        {
            string? name = principal.FindFirst("preferred_username")?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            name = principal.FindFirst(ClaimsIdentity.DefaultNameClaimType)?.Value;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            return principal.FindFirst("name")?.Value;
        }

        /// <summary>The user's directory object id (Azure AD's "oid" claim, in either spelling), which
        /// <see cref="IUserService"/> looks groups up by. Same claims as Microsoft.Identity.Web's GetObjectId,
        /// then the standard name identifier and "sub" so a cookie sign-in can use those too.</summary>
        public static string? GetUserObjectId(this ClaimsPrincipal principal) =>
            principal.FindFirst("oid")?.Value
            ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
    }
}
