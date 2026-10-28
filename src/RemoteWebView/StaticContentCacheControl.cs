using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteBlazorWebView;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Request passed to <see cref="IBlazorWebView.StaticContentCacheControlProvider"/>.
    /// Matches the MAUI <c>BlazorWebViewStaticContentRequest</c> shape: absolute URI and resolved content type.
    /// </summary>
    public sealed class BlazorWebViewStaticContentRequest
    {
        public BlazorWebViewStaticContentRequest(Uri uri, string contentType)
        {
            Uri = uri;
            ContentType = contentType;
        }

        public Uri Uri { get; }
        public string ContentType { get; }
    }

    public static class StaticContentCacheControl
    {
        // Historical MAUI default. See dotnet/maui#35706.
        public const string Default = "no-cache, max-age=0, must-revalidate, no-store";

        /// <summary>
        /// Provider installed on a desktop WebView when the app does not set one.
        /// Same header local BlazorWebView's StaticContentProvider sends.
        /// </summary>
        public static readonly Func<BlazorWebViewStaticContentRequest, string?> DefaultProvider = static _ => Default;

        /// <summary>
        /// Returns the app provider, or <see cref="DefaultProvider"/> when the app set none.
        /// </summary>
        public static Func<BlazorWebViewStaticContentRequest, string?> ProviderOrDefault(
            Func<BlazorWebViewStaticContentRequest, string?>? provider)
            => provider ?? DefaultProvider;

        // Remote-only cap. MAUI does not limit the provider string. The value is copied into an HTTP
        // response header, so a multi-kilobyte result is rejected and the default is used instead.
        public const int MaxLength = 1024;

        /// <summary>
        /// Returns the header to send when a provider is installed, or null when no provider is installed.
        /// Null, empty, whitespace, CR/LF, an oversized value, and thrown exceptions become <see cref="Default"/>.
        /// </summary>
        public static string? ResolveForMetadata(
            Func<BlazorWebViewStaticContentRequest, string?>? provider,
            string requestUri,
            string contentType,
            ILogger? logger)
        {
            if (provider is null)
                return null;

            if (!TryCreateAbsoluteUri(requestUri, out var uri))
                return Default;

            string? cacheControl;
            try
            {
                cacheControl = provider(new BlazorWebViewStaticContentRequest(uri, contentType ?? string.Empty));
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "The StaticContentCacheControlProvider threw an exception for request '{RequestUri}'. Falling back to the default Cache-Control header.", requestUri);
                return Default;
            }

            if (string.IsNullOrWhiteSpace(cacheControl))
                return Default;

            if (cacheControl.Contains('\r') || cacheControl.Contains('\n') || cacheControl.Length > MaxLength)
                return Default;

            return cacheControl;
        }

        public static bool TryCreateAbsoluteUri(string requestUri, out Uri uri)
        {
            if (Uri.TryCreate(requestUri, UriKind.Absolute, out uri!)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                return true;

            var raw = string.IsNullOrWhiteSpace(requestUri) ? "/" : requestUri;
            var fragmentIndex = raw.IndexOf('#');
            var fragment = fragmentIndex >= 0 ? raw[fragmentIndex..] : "";
            var withoutFragment = fragmentIndex >= 0 ? raw[..fragmentIndex] : raw;

            var queryIndex = withoutFragment.IndexOf('?');
            var pathPart = queryIndex >= 0 ? withoutFragment[..queryIndex] : withoutFragment;
            var query = queryIndex >= 0 ? withoutFragment[queryIndex..] : "";
            if (!pathPart.StartsWith('/'))
                pathPart = "/" + pathPart;

            // Escape each segment so a decoded space survives Uri parsing, without double-encoding %20.
            var escapedPath = string.Join("/", pathPart.Split('/').Select(segment => Uri.EscapeDataString(Uri.UnescapeDataString(segment))));
            return Uri.TryCreate($"https://{HostAddressHelper.GetAppHostAddress()}{escapedPath}{query}{fragment}", UriKind.Absolute, out uri!);
        }
    }
}
