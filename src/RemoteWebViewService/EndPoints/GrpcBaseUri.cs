using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using PeakSWC.RemoteWebView.Pages;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate GrpcBaseUri()
        {
            return async context =>
            {
                var baseUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/";

                var response = new GrpcBaseUriResponse
                {
                    GrpcBaseUri = baseUri,
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonContext.Default.GrpcBaseUriResponse)).ConfigureAwait(false);
            };
        }

    }
}
