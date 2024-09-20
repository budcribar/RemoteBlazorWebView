using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Stats()
        {
            return async context =>
            {
                // Retrieve the ServerStats instance from DI
                var stats = context.RequestServices.GetRequiredService<ServerStats>();

                // Get the current statistics
                var data = stats.GetStats();

                // Build the HTML content
                var htmlBuilder = new StringBuilder();

                htmlBuilder.AppendLine("<!DOCTYPE html>");
                htmlBuilder.AppendLine("<html lang=\"en\">");
                htmlBuilder.AppendLine("<head>");
                htmlBuilder.AppendLine("    <meta charset=\"UTF-8\">");
                htmlBuilder.AppendLine("    <meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
                htmlBuilder.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
                htmlBuilder.AppendLine("    <title>gRPC Server Statistics</title>");
                // Basic CSS for styling
                htmlBuilder.AppendLine("    <style>");
                htmlBuilder.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; background-color: #f4f4f4; }");
                htmlBuilder.AppendLine("        h1 { color: #333; }");
                htmlBuilder.AppendLine("        table { width: 100%; border-collapse: collapse; margin-bottom: 20px; }");
                htmlBuilder.AppendLine("        th, td { padding: 12px; border: 1px solid #ddd; text-align: left; }");
                htmlBuilder.AppendLine("        th { background-color: #f2f2f2; }");
                htmlBuilder.AppendLine("        tr:nth-child(even) { background-color: #f9f9f9; }");
                htmlBuilder.AppendLine("    </style>");
                htmlBuilder.AppendLine("</head>");
                htmlBuilder.AppendLine("<body>");
                htmlBuilder.AppendLine("    <h1>gRPC Server Statistics</h1>");

                // Basic Metrics Table
                htmlBuilder.AppendLine("    <h2>Basic Metrics</h2>");
                htmlBuilder.AppendLine("    <table>");
                htmlBuilder.AppendLine("        <tr><th>Metric</th><th>Value</th></tr>");
                htmlBuilder.AppendLine($"        <tr><td>Total Requests</td><td>{data["TotalRequests"]}</td></tr>");
                htmlBuilder.AppendLine($"        <tr><td>Successful Requests</td><td>{data["SuccessfulRequests"]}</td></tr>");
                htmlBuilder.AppendLine($"        <tr><td>Failed Requests</td><td>{data["FailedRequests"]}</td></tr>");
                htmlBuilder.AppendLine($"        <tr><td>Active Connections</td><td>{data["ActiveConnections"]}</td></tr>");
                htmlBuilder.AppendLine("    </table>");

                // Response Time Metrics Table
                htmlBuilder.AppendLine("    <h2>Response Time Metrics (ms)</h2>");
                htmlBuilder.AppendLine("    <table>");
                htmlBuilder.AppendLine("        <tr><th>Metric</th><th>Value</th></tr>");
                htmlBuilder.AppendLine($"        <tr><td>Average Response Time</td><td>{data["AverageResponseTime(ms)"]:F2}</td></tr>");
                htmlBuilder.AppendLine($"        <tr><td>Maximum Response Time</td><td>{data["MaxResponseTime(ms)"]:F2}</td></tr>");
                htmlBuilder.AppendLine($"        <tr><td>Minimum Response Time</td><td>{data["MinResponseTime(ms)"]:F2}</td></tr>");
                htmlBuilder.AppendLine("    </table>");

                // Latency Percentiles Table
                htmlBuilder.AppendLine("    <h2>Latency Percentiles</h2>");
                htmlBuilder.AppendLine("    <table>");
                htmlBuilder.AppendLine("        <tr><th>Percentile</th><th>Value (ms)</th></tr>");
                var percentiles = (Dictionary<int, double>)data["LatencyPercentiles"];
                foreach (var percentile in percentiles.OrderBy(p => p.Key))
                {
                    htmlBuilder.AppendLine($"        <tr><td>{percentile.Key}th</td><td>{percentile.Value:F2}</td></tr>");
                }
                htmlBuilder.AppendLine("    </table>");

                // Error Metrics Table
                htmlBuilder.AppendLine("    <h2>Error Metrics</h2>");
                htmlBuilder.AppendLine("    <table>");
                htmlBuilder.AppendLine("        <tr><th>Error Type</th><th>Count</th></tr>");
                var errorTypes = (Dictionary<string, long>)data["ErrorTypes"];
                foreach (var error in errorTypes.OrderBy(e => e.Key))
                {
                    htmlBuilder.AppendLine($"        <tr><td>{error.Key}</td><td>{error.Value}</td></tr>");
                }
                htmlBuilder.AppendLine("    </table>");

                htmlBuilder.AppendLine("</body>");
                htmlBuilder.AppendLine("</html>");

                // Set the response content type to HTML
                context.Response.ContentType = "text/html; charset=utf-8";

                // Write the HTML content to the response
                await context.Response.WriteAsync(htmlBuilder.ToString());
            };
        }
    }
}
