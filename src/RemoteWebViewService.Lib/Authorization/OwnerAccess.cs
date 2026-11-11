using Microsoft.AspNetCore.Http;
using System;
using System.Security.Cryptography;
using System.Text;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Who may act as the machine owner in a shared session.
    ///
    /// The app makes a secret owner key and sends it with CreateWebView. Its own window opens /app/{id}?role=owner&amp;key=...;
    /// the relay checks the key there and answers with a cookie holding the session's owner token. The owner's page sends
    /// that token with its connection, and only a connection that does is treated as the owner (it may take control back).
    /// Adding ?role=owner to a mirror link no longer makes a remote user the owner.
    ///
    /// Sessions from apps that send no owner key keep the old behaviour: ?role=owner is trusted.
    /// </summary>
    public static class OwnerAccess
    {
        public static string CookieName(string id) => $"rbwv-owner-{id}";

        public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        /// <summary>True when the session requires the owner key (the app sent one).</summary>
        public static bool IsKeyed(ServiceState state) => !string.IsNullOrEmpty(state.OwnerKey);

        /// <summary>The key on /app/{id}?role=owner&amp;key=... matches the one the app sent.</summary>
        public static bool KeyMatches(ServiceState state, string? key) => IsKeyed(state) && FixedEquals(state.OwnerKey, key);

        /// <summary>The token sent with a browser connection (or in the owner cookie) is this session's owner token.</summary>
        public static bool TokenMatches(ServiceState state, string? token) => IsKeyed(state) && FixedEquals(state.OwnerToken, token);

        /// <summary>
        /// Whether a connection that says it is the owner is accepted as the owner: always for a session without an owner
        /// key (older apps), otherwise only with the owner token.
        /// </summary>
        public static bool AcceptsOwner(ServiceState state, bool claimsOwner, string? token) =>
            claimsOwner && (!IsKeyed(state) || TokenMatches(state, token));

        /// <summary>The request carries this session's owner cookie.</summary>
        public static bool HasOwnerCookie(HttpContext context, ServiceState state) =>
            context.Request.Cookies.TryGetValue(CookieName(state.Id), out var token) && TokenMatches(state, token);

        /// <summary>
        /// Sets the owner cookie. Readable by the page's script (not HttpOnly): the page sends the token on its gRPC
        /// connection, which may go to a different host than the page. Scoped to this session's id.
        /// </summary>
        public static void SetOwnerCookie(HttpContext context, ServiceState state) =>
            context.Response.Cookies.Append(CookieName(state.Id), state.OwnerToken, new CookieOptions
            {
                Path = "/",
                Secure = true,
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
            });

        private static bool FixedEquals(string expected, string? actual)
        {
            if (string.IsNullOrEmpty(actual))
                return false;
            var a = Encoding.UTF8.GetBytes(expected);
            var b = Encoding.UTF8.GetBytes(actual);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
