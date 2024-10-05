using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using PeakSwc.StaticFiles;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Mirror()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(guid))
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid or missing GUID").ConfigureAwait(false);
                    return;
                }
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();

                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
                var serviceStateTaskSource = serviceDictionary.GetOrAdd(guid.ToString(), _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));


                try
                {
                    // Wait for the task to be completed or time out using the extension method
                    var serviceState = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60));
                    var ready = await serviceState.FileManagerReady.Task.WaitWithTimeout(TimeSpan.FromSeconds(60));
                    if (serviceState.EnableMirrors && serviceState.InUse)
                    {
                        serviceState.User = context.User.GetDisplayName() ?? string.Empty;
                        serviceState.IsMirroredConnection.Add(context.Connection.Id);

                        if (serviceState.IPC.ClientResponseStream != null)
                        {
                            await serviceState.IPC.ClientResponseStream
                                .WriteAsync(new WebMessageResponse { Response = "browserAttached:" })
                                .ConfigureAwait(false);
                        }
                        foreach (var channel in serviceStateChannel.Values)
                        {
                            await channel.Writer.WriteAsync($"Connect:{guid}").ConfigureAwait(false);
                        }

                        var home = serviceState.HtmlHostPath;
                        var rfr = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                        var fileInfo = await rfr.GetFileMetaDataAsync(guid.ToString(), serviceState.HtmlHostPath).ConfigureAwait(false);
                        FileStats.Update(serviceState, guid, fileInfo);
                        //ILogger<RemoteWebViewService> logger = context.RequestServices.GetRequiredService<ILogger<RemoteWebViewService>>();
                        //logger.LogCritical($"Read {serviceState.TotalFilesRead} file {serviceState.HtmlHostPath}");
                        context.Response.StatusCode = fileInfo.StatusCode;
                        context.Response.ContentType = "text/html";

                        if ((HttpStatusCode)fileInfo.StatusCode != HttpStatusCode.OK)
                        {
                            await context.Response.WriteAsync("Mirroring is not enabled").ConfigureAwait(false);
                            return;
                        }
                        // TODO edit causes Length to be different than FileInfo
                        //context.Response.ContentLength = fileInfo.Length;
                        var fileStream = await rfr.GetFileStreamAsync(guid.ToString(), serviceState.HtmlHostPath).ConfigureAwait(false);
                        using Stream stream = fileStream.Stream;
                        await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync("Mirroring is not enabled").ConfigureAwait(false);
                    }
                }
                catch(Exception)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Service state not found").ConfigureAwait(false);
                    return;
                }


              
           
        };

    }
}
}
