using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Pages;
using PeakSWC.RemoteWebView.Services;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate StartOrRefresh()
        {
            return async context =>
            {
                // Retrieve the GUID from the route values and validate it
                if (!context.Request.RouteValues.TryGetValue("id", out var idValue) || idValue == null || !Guid.TryParse(idValue.ToString(), out var guid))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync($"Invalid or missing GUID {idValue}").ConfigureAwait(false);
                    return;
                }

                // Retrieve service dictionary and check if the service state exists
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
                var serviceStateTaskSource = PeakSWC.RemoteWebView.RegistryLog.GetOrAdd(serviceDictionary, guid.ToString(), PeakSWC.RemoteWebView.RaceLog.Ev.RegStartOrRefresh);

                try
                {
                    // Wait for the task to be completed or time out using the extension method
                    var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                    var ready = await serviceState.FileManagerReady.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

                    // Handle the case where refresh is not enabled
                    if (!serviceState.Refresh)
                    {
                        serviceState.Refresh = true;

                        // Retrieve and stream the home HTML file
                        var rfr = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                        var fileInfo = await rfr.GetFileMetaDataAsync(guid.ToString(), serviceState.HtmlHostPath).ConfigureAwait(false);
                        FileStats.Update(serviceState, guid.ToString(), fileInfo);
                        ILogger<RemoteWebViewService> logger = context.RequestServices.GetRequiredService<ILogger<RemoteWebViewService>>();
                        //logger.LogCritical($"Read {serviceState.TotalFilesRead} file {serviceState.HtmlHostPath}");
                        context.Response.StatusCode = fileInfo.StatusCode;
                        context.Response.ContentType = "text/html";

                        if ((HttpStatusCode)fileInfo.StatusCode != HttpStatusCode.OK)
                        {
                            await context.Response.WriteAsync($"File not found {serviceState.HtmlHostPath}").ConfigureAwait(false);
                            return;
                        }
                        // TODO edit causes Length to be different than FileInfo
                        // context.Response.ContentLength = fileInfo.Length;
                        var fileStream = await rfr.GetFileStreamAsync(guid.ToString(), serviceState.HtmlHostPath).ConfigureAwait(false);
                        using Stream stream = fileStream.Stream;
                        await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
                    }
                    else
                    {
                        // Handle the refresh logic
                        var id = guid.ToString();
                        PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.RefreshHttpEnter, guid, serviceState.Pid);
                        await serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = "refreshed:" }).ConfigureAwait(false);
                        PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.RefreshedWritten, guid);

                        // Wait for the client to shut down, with a timeout of 30 seconds
                        var waitStart = DateTime.UtcNow;
                        bool clientShutdown = await WaitForClientShutdown(serviceDictionary, id, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                        int elapsedMs = (int)(DateTime.UtcNow - waitStart).TotalMilliseconds;
                        int flags = (clientShutdown ? 1 : 0) | (serviceDictionary.ContainsKey(id) ? 2 : 0);
                        PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.WaitEnd, guid, elapsedMs, flags);

                        if (clientShutdown)
                        {
                            // The client has shut down, serve the restart page
                            context.Response.ContentType = "text/html";
                            PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.RestartPage, guid);
                            await context.Response.WriteAsync(RestartPage.Html(guid.ToString(), serviceState?.ProcessName ?? "", serviceState?.HostName ?? "")).ConfigureAwait(false);
                        }
                        else
                        {
                            // The client did not respond within the timeout
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            context.Response.ContentType = "text/html";

                            PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.FailurePage, guid, serviceState.Pid);
                            PeakSWC.RemoteWebView.RaceLog.Flush();
                            await context.Response.WriteAsync(RestartFailedPage.Html(serviceState.ProcessName, serviceState.Pid, serviceState.HostName)).ConfigureAwait(false);

                            // Shutdown since the client did not respond to the restart request
                            PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.FallbackShutdown, guid);
                            await context.RequestServices.GetRequiredService<ShutdownService>().Shutdown(guid.ToString()).ConfigureAwait(false);
                            PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.FallbackShutdownReturn, guid);
                            PeakSWC.RemoteWebView.RaceLog.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    PeakSWC.RemoteWebView.RaceLog.Mark(PeakSWC.RemoteWebView.RaceLog.Ev.RefreshCatch, guid, ex.HResult);
                    PeakSWC.RemoteWebView.RaceLog.Flush();
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid.ToString(), true)).ConfigureAwait(false);
                    return;
                }
               
            };
        }

        // Helper method to wait for the client to shut down, with a timeout and delay specified via TimeSpan
        private static async Task<bool> WaitForClientShutdown(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, string guid, TimeSpan timeout, TimeSpan delay)
        {
            int attempts = (int)(timeout.TotalMilliseconds / delay.TotalMilliseconds);

            for (int i = 0; i < attempts; i++)
            {
                if (!serviceDictionary.ContainsKey(guid))
                {
                    return true; // Client has shut down
                }

                await Task.Delay(delay).ConfigureAwait(false);
            }

            return false; // Client did not shut down within the timeout
        }
    }
}
