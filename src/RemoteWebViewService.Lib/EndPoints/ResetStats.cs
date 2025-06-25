using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate ResetStats()
        {
            return context =>
            {
                // Retrieve the ServerStats service from the DI container
                var stats = context.RequestServices.GetRequiredService<ServerStats>();

                // Reset the statistics
                stats.ResetStats();

                context.Response.StatusCode = StatusCodes.Status204NoContent;

                return Task.CompletedTask;
            };
        }
    }
}
