using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
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
                var client = await TryReadClientAsync(context).ConfigureAwait(false);
                if (client == null)
                    return;

                var (parsedGuid, serviceDictionary) = client.Value;
                string guid = parsedGuid.ToString();
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();
                var serviceStateTaskSource = serviceDictionary.GetOrAdd(guid, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

                try
                {
                    // Wait for the task to be completed or time out using the extension method
                    var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                    var ready = await serviceState.FileManagerReady.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                    if (serviceState.EnableMirrors && serviceState.InUse)
                    {
                        var rfr = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                        var fileInfo = await rfr.GetFileMetaDataAsync(guid.ToString(), serviceState.HtmlHostPath).ConfigureAwait(false);
                        FileStats.Update(serviceState, guid, fileInfo);
                        context.Response.StatusCode = fileInfo.StatusCode;
                        context.Response.ContentType = "text/html";

                        if ((HttpStatusCode)fileInfo.StatusCode != HttpStatusCode.OK)
                        {
                            await context.Response.WriteAsync("Mirroring is not enabled").ConfigureAwait(false);
                            return;
                        }

                        if (string.IsNullOrEmpty(serviceState.User))
                        {
                            serviceState.User = context.User.GetDisplayName() ?? string.Empty;
                        }
                        serviceState.IsMirroredConnection.Add(context.Connection.Id);

                        if (serviceState.IPC.ClientResponseStream != null)
                        {
                            await serviceState.IPC.ClientResponseStream
                                .WriteAsync(new WebMessageResponse { Response = "browserAttached:" })
                                .ConfigureAwait(false);
                        }
                        foreach (var channel in serviceStateChannel.Values)
                        {
                            channel.Writer.TryWrite($"Connect:{guid}");
                        }

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
