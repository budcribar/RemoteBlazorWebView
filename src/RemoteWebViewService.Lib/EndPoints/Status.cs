using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Status()
        {
            return async context =>
            {
                var client = await TryReadClientAsync(context).ConfigureAwait(false);
                if (client == null)
                    return;

                var (guid, serviceDictionary) = client.Value;
                var response = new StatusResponse
                {
                    Connected = IsRegistered(serviceDictionary, guid.ToString())
                };

                // Set content type to JSON and return the response
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response, JsonContext.Default.StatusResponse).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// True once an app has registered this id. An entry alone is not enough: the page endpoints add an unfinished one for
        /// an id a browser asks for before its app is up, so that they can wait for it, and that must not read as "connected".
        /// </summary>
        internal static bool IsRegistered(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> services, string id) =>
            services.TryGetValue(id, out var registration) && registration is not null && registration.Task.IsCompletedSuccessfully;
    }
}
