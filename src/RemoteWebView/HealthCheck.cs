using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    internal static class HealthCheck
    {
        /// <summary>
        /// Waits until the server's health endpoint returns a successful response or until a timeout occurs.
        /// </summary>
        /// <param name="healthCheckUrl">The URL of the server's health check endpoint.</param>
        /// <param name="httpHandler">The HTTP handler configured to bypass SSL certificate validation.</param>
        /// <param name="timeoutSeconds">Maximum time to wait for the server to become healthy.</param>
        /// <param name="retryIntervalSeconds">Time interval between health check attempts.</param>
        /// <returns>True if the server is healthy within the timeout; otherwise, false.</returns>
        public static async Task<bool> WaitAsync(string healthCheckUrl, int timeoutSeconds = 30, int retryIntervalSeconds = 1)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var delay = TimeSpan.FromSeconds(retryIntervalSeconds);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    var response = await httpClient.GetAsync(healthCheckUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }              
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Health check request failed: {ex.Message}. Retrying in {delay.Seconds} seconds...");
                }

                await Task.Delay(delay);
            }

            Console.WriteLine("Server health check timed out.");
            return false;
        }
    }
}
