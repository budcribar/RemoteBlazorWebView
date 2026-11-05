using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        // Stops before GetOrAdd. Callers decide whether to insert a registration.
        private static async Task<(Guid Id, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> Services)?> TryReadClientAsync(HttpContext context)
        {
            if (!context.Request.RouteValues.TryGetValue("id", out var idValue) || idValue == null || !Guid.TryParse(idValue.ToString(), out var guid))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                // The 400 write should complete as before, so pass None rather than RequestAborted.
                await context.Response.WriteAsync($"Invalid or missing GUID {idValue}", CancellationToken.None).ConfigureAwait(false);
                return null;
            }

            var services = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
            return (guid, services);
        }
    }
}
