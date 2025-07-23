using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate SetMarkup()
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

                // Only allow POST requests
                if (context.Request.Method != HttpMethods.Post)
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    await context.Response.WriteAsync("Only POST method is allowed").ConfigureAwait(false);
                    return;
                }

                // Read the HTML markup from the request body
                string markup;
                using (var reader = new StreamReader(context.Request.Body))
                {
                    markup = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(markup))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Markup cannot be empty").ConfigureAwait(false);
                    return;
                }

                // Retrieve service dictionary and check if the service state exists
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();
                
                if (!serviceDictionary.TryGetValue(guid.ToString(), out var serviceStateTaskSource))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync($"Service with GUID {guid} not found").ConfigureAwait(false);
                    return;
                }

                try
                {
                    // Wait for the service state to be available
                    var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                    
                    // Update the markup property
                    serviceState.Markup = markup;

                    // Notify all channels about the markup change to regenerate ClientResponseList
                    foreach (var channel in serviceStateChannel.Values)
                    {
                        await channel.Writer.WriteAsync($"MarkupChanged:{guid}").ConfigureAwait(false);
                    }

                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "text/plain";
                    await context.Response.WriteAsync("Markup updated successfully").ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                    await context.Response.WriteAsync("Service state not available within timeout").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync($"Error updating markup: {ex.Message}").ConfigureAwait(false);
                }
            };
        }
    }
}