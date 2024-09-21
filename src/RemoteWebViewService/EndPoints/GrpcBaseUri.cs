using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate GrpcBaseUri()
        {
            return async context =>
            {
                try
                {
                    var baseUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}/";

                    var response = new GrpcBaseUriResponse
                    {
                        GrpcBaseUri = baseUri,
                    };

                    var jsonResponse = JsonSerializer.Serialize(response, JsonContext.Default.GrpcBaseUriResponse);

                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength = jsonResponse.Length; 

                    await context.Response.WriteAsync(jsonResponse).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ILogger<RemoteWebViewService> logger = context.RequestServices.GetRequiredService<ILogger<RemoteWebViewService>>();
                    logger.LogError(ex, ex.Message);

                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("An error occurred while processing the request.").ConfigureAwait(false);
                }
            };
        }
    }
}
