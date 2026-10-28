using Microsoft.AspNetCore.Http;
using PeakSWC.RemoteWebView.Pages;
using System;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Wait()
        {
            return async context =>
            {
                var client = await TryReadClientAsync(context).ConfigureAwait(false);
                if (client == null)
                    return;

                var (guid, serviceDictionary) = client.Value;

                // Wait for the specified service to appear in the dictionary, up to a 30-second timeout
                for (int i = 0; i < 30; i++)
                {
                    if (context.RequestAborted.IsCancellationRequested)
                        return;

                    if (serviceDictionary.TryGetValue(guid.ToString(), out var tcs) && tcs != null && !tcs.Task.IsFaulted && !tcs.Task.IsCanceled)
                    {
                        context.Response.StatusCode = StatusCodes.Status200OK;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("Wait completed", context.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    // Delay for 1 second before the next check
                    try
                    {
                        await Task.Delay(1000, context.RequestAborted).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                    {
                        return;
                    }
                }

                // After 30 seconds, if the condition isn't met, return a timeout response
                context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(RestartFailedPage.Fragment(guid.ToString()), context.RequestAborted).ConfigureAwait(false);
            };
        }
    }
}
