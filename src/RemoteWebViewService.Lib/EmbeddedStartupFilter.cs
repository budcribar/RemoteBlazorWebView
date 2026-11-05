using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class MaxClientsOptions
    {
        public int MaxClients { get; set; } = int.MaxValue;
    }

    /// <summary>
    /// Middleware the embedded server puts in front of Startup's pipeline: the client limit and the
    /// frame-ancestors policy from <see cref="RemoteWebViewServerOptions"/>.
    /// </summary>
    internal sealed class EmbeddedStartupFilter : IStartupFilter
    {
        private const string CreateWebViewPath = "/webview.WebViewIPC/CreateWebView";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var maxClients = app.ApplicationServices.GetRequiredService<MaxClientsOptions>().MaxClients;
            var frameAncestorsOptions = app.ApplicationServices.GetRequiredService<FrameAncestorsOptions>();
            var frameAncestors = frameAncestorsOptions.Origins;
            var allowOpaqueOrigin = frameAncestorsOptions.AllowOpaqueOrigin;
            var clients = app.ApplicationServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();

            if (maxClients < int.MaxValue)
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    // Only a new RemoteWebView client counts against the limit. Browser requests for clients that are
                    // already connected must still get through, so the check is on CreateWebView alone. Entries that
                    // a browser added while waiting for a client (incomplete tasks) are not counted.
                    if (context.Request.Path.Equals(CreateWebViewPath, StringComparison.Ordinal) &&
                        clients.Values.Count(t => t.Task.IsCompletedSuccessfully) >= maxClients)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("Server is at maximum client capacity.").ConfigureAwait(false);
                        return;
                    }
                    await nextMiddleware().ConfigureAwait(false);
                });
            }

            if (frameAncestors.Count > 0)
            {
                string policy = $"frame-ancestors {string.Join(' ', frameAncestors)}";
                app.Use(async (context, nextMiddleware) =>
                {
                    // Cross-origin script calls (fetch / gRPC-Web) are only accepted from the allowed origins.
                    // Same-origin calls are always allowed: a page served from here and framed by the host
                    // calls back with this server's own origin.
                    var origin = context.Request.Headers.Origin.ToString();
                    // A sandboxed frame without allow-same-origin sends "Origin: null"; accepted only when the host opted in.
                    if (!string.IsNullOrEmpty(origin) && !IsSameOrigin(context.Request, origin) &&
                        !(allowOpaqueOrigin && origin == "null") &&
                        !frameAncestors.Contains(origin.TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsync("Forbidden: Origin not allowed").ConfigureAwait(false);
                        return;
                    }

                    context.Response.OnStarting(() =>
                    {
                        var contentType = context.Response.ContentType;
                        if (contentType != null && contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                            context.Response.Headers.ContentSecurityPolicy = policy;
                        return Task.CompletedTask;
                    });

                    await nextMiddleware().ConfigureAwait(false);
                });
            }

            next(app);
        };

        private static bool IsSameOrigin(HttpRequest request, string origin) =>
            string.Equals(origin.TrimEnd('/'), $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase);
    }
}
