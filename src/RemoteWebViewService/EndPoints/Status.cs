using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Text.Json;
using System;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Status()
        {
            return async context =>
            {
                // Check if 'id' route value exists and is a valid GUID
                if (!context.Request.RouteValues.TryGetValue("id", out var idValue) || idValue == null || !Guid.TryParse(idValue.ToString(), out var guid))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Invalid or missing GUID").ConfigureAwait(false);
                    return;
                }

                // Retrieve service state from the service dictionary
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, ServiceState>>();
                var response = new StatusResponse
                {
                    Connected = serviceDictionary.ContainsKey(guid.ToString())
                };

                // Set content type to JSON and return the response
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, response).ConfigureAwait(false);
            };
        }
    }
}
