using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using PeakSwc.StaticFiles;
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

                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, ServiceState>>();
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();

                if (!serviceDictionary.TryGetValue(guid, out var serviceState))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Service state not found").ConfigureAwait(false);
                    return;
                }


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
                    context.Response.StatusCode = fileInfo.StatusCode;
                    context.Response.ContentType = "text/html";

                    if ((HttpStatusCode)fileInfo.StatusCode != HttpStatusCode.OK)
                    {
                        await context.Response.WriteAsync("Mirroring is not enabled").ConfigureAwait(false);
                        return;
                    }
                    context.Response.ContentLength = fileInfo.Length;
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
           
        };

    }
}
}
