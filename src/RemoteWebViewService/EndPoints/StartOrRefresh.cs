using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using PeakSwc.StaticFiles;
using PeakSWC.RemoteWebView.Pages;
using PeakSWC.RemoteWebView.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate StartOrRefresh()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, ServiceState>>();

                if (serviceDictionary.TryGetValue(guid, out var serviceState))
                {
                    if (!serviceState.Refresh)
                    {
                        serviceState.Refresh = true;
                        var home = serviceState.HtmlHostPath;
                        var rfr = context.RequestServices.GetRequiredService<RemoteFileResolver>();
                        var fi = await rfr.GetFileInfo($"/{guid}/{home}").ConfigureAwait(false);
                        context.Response.ContentLength = fi.Length;
                        using Stream stream = fi.CreateReadStream();
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "text/html";
                        await stream.CopyToAsync(context.Response.Body).ConfigureAwait(false);
                    }
                    else
                    {
                        await serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = "refreshed:" }).ConfigureAwait(false);

                        // Wait until client shuts down 
                        for (int i = 0; i < 3000; i++)
                        {
                            if (!serviceDictionary.ContainsKey(guid))
                            {
                                context.Response.ContentType = "text/html";
                                await context.Response.WriteAsync(RestartPage.Html(guid, serviceState?.ProcessName ?? "", serviceState?.HostName ?? "")).ConfigureAwait(false);
                                return;
                            }

                            await Task.Delay(10).ConfigureAwait(false);
                        }

                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";

                        await context.Response.WriteAsync(RestartFailedPage.Html(serviceState.ProcessName, serviceState.Pid, serviceState.HostName)).ConfigureAwait(false);

                        // Shutdown since client did not respond to restart request
                        await context.RequestServices.GetRequiredService<ShutdownService>().Shutdown(guid).ConfigureAwait(false);
                    }

                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid, true)).ConfigureAwait(false);
                }
            };
        }

    }
}
