using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public  static partial class Endpoints
    {
        public static RequestDelegate ResetStats()
        {
            return async context =>
            {
                var stats = context.RequestServices.GetRequiredService<ServerStats>();
                stats.ResetStats();
                context.Response.StatusCode = StatusCodes.Status204NoContent; // No Content
                await Task.CompletedTask;
            };
        }
    }
}
