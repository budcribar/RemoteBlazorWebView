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
                var client = await TryReadClientAsync(context).ConfigureAwait(false);
                if (client == null)
                    return;

                var (guid, serviceDictionary) = client.Value;
                var serviceStateTaskSource = serviceDictionary.GetOrAdd(guid.ToString(), _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

                ServiceState? serviceState = null;
                try
                {
                    // Wait for the task to be completed or time out using the extension method
                    serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                    var ready = await serviceState.FileManagerReady.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

                    // In a shared session only the owner's surface loads the app's page; any other browser is sent to the mirror,
                    // so a remote tab coming back after the PC restarted can never be taken for a refresh and restart the app.
                    if (SendsToMirror(serviceState, context))
                    {
                        LogPageRequest(context, guid.ToString(), serviceState, "sent to /mirror (no owner cookie)");
                        RedirectToMirror(context, guid.ToString());
                        return;
                    }

                    // The first load serves the page; a later one is a refresh (the app restarts), except the owner's first.
                    if (ServesPage(serviceState, context))
                    {
                        LogPageRequest(context, guid.ToString(), serviceState, serviceState.Refresh ? "served the page (the owner's first load)" : "served the page (first load)");
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
                        var voiceKey = serviceState.Voice?.TakeControllerPage(context, guid.ToString());
                        using Stream stream = fileStream.Stream;
                        await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
                        serviceState.Refresh = true;
                        if (OwnerAccess.HasOwnerCookie(context, serviceState))
                            serviceState.OwnerPageServed = true;
                        if (voiceKey is not null)
                            await context.Response.WriteAsync(VoiceRoom.ControllerScript(guid, voiceKey)).ConfigureAwait(false);
                    }
                    else
                    {
                        // Handle the refresh logic
                        LogPageRequest(context, guid.ToString(), serviceState, "refresh: the app is asked to restart");
                        await serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = "refreshed:" }).ConfigureAwait(false);

                        // Wait for the client to shut down, with a timeout of 30 seconds
                        bool clientShutdown = await WaitForClientShutdown(serviceDictionary, guid.ToString(), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

                        if (clientShutdown)
                        {
                            // The client has shut down, serve the restart page
                            context.Response.ContentType = "text/html";
                            await context.Response.WriteAsync(RestartPage.Html(guid.ToString(), serviceState?.ProcessName ?? "", serviceState?.HostName ?? "")).ConfigureAwait(false);
                        }
                        else
                        {
                            // The client did not respond within the timeout
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            context.Response.ContentType = "text/html";

                            await context.Response.WriteAsync(RestartFailedPage.Html(serviceState.ProcessName, serviceState.Pid, serviceState.HostName)).ConfigureAwait(false);

                            // Shutdown since the client did not respond to the restart request
                            await context.RequestServices.GetRequiredService<ShutdownService>().Shutdown(guid.ToString(), SessionEndReason.RefreshRestartTimedOut).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    LogPageRequest(context, guid.ToString(), serviceState, serviceState == null ? "no app registered this id in time" : "failed");
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid.ToString(), true)).ConfigureAwait(false);
                    return;
                }
               
            };
        }

        /// <summary>
        /// Whether a load of /{id} is given the app's page (true) or counts as a refresh, which restarts the app (false).
        /// The first load is given the page. So is the first load by the owner's own surface (it carries the owner cookie
        /// that /app/{id}?role=owner&amp;key=... set), whoever came before it: after the PC restarts, a browser tab left on
        /// the session can reach the new copy of the app before the app's own window does, and the window's load must not
        /// then restart the app it belongs to. The owner's later loads are refreshes like anyone else's.
        /// </summary>
        internal static bool ServesPage(ServiceState serviceState, HttpContext context) =>
            !serviceState.Refresh || (!serviceState.OwnerPageServed && OwnerAccess.HasOwnerCookie(context, serviceState));

        // The object captured before the loop is the registration that was live when
        // this wait started. A missing key or a different object means that registration
        // is gone, even if a later call inserted a new one under the same id.
        internal static async Task<bool> WaitForClientShutdown(
            ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary,
            string guid,
            TimeSpan? timeout = null,
            TimeSpan? delay = null)
        {
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            var effectiveDelay = delay ?? TimeSpan.FromMilliseconds(100);
            serviceDictionary.TryGetValue(guid, out var original);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (true)
            {
                if (!serviceDictionary.TryGetValue(guid, out var current) || !ReferenceEquals(current, original))
                {
                    return true;
                }

                var remaining = effectiveTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                var step = effectiveDelay > TimeSpan.Zero && remaining >= effectiveDelay ? effectiveDelay : remaining;
                await Task.Delay(step).ConfigureAwait(false);
            }
        }
    }
}
