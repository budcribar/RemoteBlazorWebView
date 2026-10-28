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
                    Connected = serviceDictionary.TryGetValue(guid.ToString(), out var tcs) && tcs != null && !tcs.Task.IsFaulted && !tcs.Task.IsCanceled
                };

                // Set content type to JSON and return the response
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response).ConfigureAwait(false);
            };
        }
    }
}
