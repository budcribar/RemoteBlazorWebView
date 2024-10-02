using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteWebView.Pages;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Wait()
        {
            return async context =>
            {
                // Check if 'id' route value exists and is a valid GUID
                if (!context.Request.RouteValues.TryGetValue("id", out var idValue) || idValue == null || !Guid.TryParse(idValue.ToString(), out var guid))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync($"Invalid or missing GUID {idValue}").ConfigureAwait(false);
                    return;
                }

                // Retrieve the service state from the service dictionary
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, ServiceState>>();

                // Wait for the specified service to appear in the dictionary, up to a 30-second timeout
                for (int i = 0; i < 30; i++)
                {
                    if (serviceDictionary.ContainsKey(guid.ToString()))
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("Wait completed").ConfigureAwait(false);
                        return;
                    }

                    // Delay for 1 second before the next check
                    await Task.Delay(1000).ConfigureAwait(false);
                }

                // After 30 seconds, if the condition isn't met, return a timeout response
                context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(RestartFailedPage.Fragment(guid.ToString())).ConfigureAwait(false);
            };
        }
    }
}
