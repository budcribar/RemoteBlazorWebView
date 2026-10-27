using Microsoft.Playwright;
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using FileSyncServer;
using FileSyncServer.Tests;
using Xunit.Abstractions;
namespace FileSyncServer.Tests
{
    [Collection("Server collection")]
    public class LoadTest
    {
        private readonly ServerFixture _serverFixture;
        private readonly ClientFixture _clientFixture;
        private readonly ITestOutputHelper _output;
        private readonly string clientCachePath;
        private string filePath;
        private readonly string clientId;

        public LoadTest(ClientFixture clientFixture, ServerFixture serverFixture, ITestOutputHelper output)
        {
            _serverFixture = serverFixture;
            _clientFixture = clientFixture;
            _output = output;
            clientId = _clientFixture.ClientId.ToString();

            var testDirectory = Directory.GetCurrentDirectory();
            clientCachePath = Path.Combine(testDirectory, "client_cache", clientId);
            Directory.CreateDirectory(clientCachePath);

            filePath = Path.Combine(clientCachePath, $"maxconcurrenttest.txt");
            CreateTestFile(1000);
        }

        private void CreateTestFile(int numLines)
        {
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                for (int i = 1; i <= numLines; i++)
                {
                    writer.WriteLine($"This is line {i} of test.txt for client {clientId}");
                }
            }
        }

        [WindowsOnlyFact]
        public async Task FindMaxConcurrentRequests()
        {
            double nonlinearityThreshold = 1.5;
            int timeoutSeconds = 30;
            string baseUrl = Utilities.BASEURL;
            string testUrl = $"{baseUrl}/{clientId}/maxconcurrenttest.txt";

            // Process checks (consider moving to fixtures)
            Process.GetProcessesByName("Client").Length.Should().BeGreaterThan(0, "Client process should be running.");
            Process.GetProcessesByName("RemoteWebViewService").Length.Should().BeGreaterThan(0, "Server process should be running.");

            await Utilities.SetServerCache(true);

            int minConcurrentRequests = 1;
            int maxConcurrentRequests = 1;
            List<Tuple<int, double>> results = new List<Tuple<int, double>>();

            while (true)
            {
                Log($"Measuring {maxConcurrentRequests} concurrent requests.");
                double averageTime = await MeasureAverageResponseTime(testUrl, maxConcurrentRequests, timeoutSeconds);
                results.Add(Tuple.Create(maxConcurrentRequests, averageTime));
                Log($"Measured {maxConcurrentRequests} concurrent requests in {averageTime:F0} ms.");

                if (averageTime > timeoutSeconds * 1000)
                {
                    Log($"Timeout detected at {maxConcurrentRequests} requests.");
                    break;
                }

                if (results.Count > 1 && averageTime > results[results.Count - 2].Item2 * nonlinearityThreshold)
                {
                    Log($"Nonlinearity threshold exceeded at {maxConcurrentRequests} requests.");
                    break;
                }

                maxConcurrentRequests *= 2;
            }

            minConcurrentRequests = maxConcurrentRequests / 2;
            Log($"Binary search between {minConcurrentRequests} and {maxConcurrentRequests} concurrent requests.");
            while (maxConcurrentRequests - minConcurrentRequests > 1)
            {
                int mid = (minConcurrentRequests + maxConcurrentRequests) / 2;
                Log($"Measuring {mid} concurrent requests.");
                double midTime = await MeasureAverageResponseTime(testUrl, mid, timeoutSeconds);
                results.Add(Tuple.Create(mid, midTime));
                Log($"Measured {mid} concurrent requests in {midTime:F0} ms.");

                if (midTime > timeoutSeconds * 1000 || (results.Count > 1 && midTime > results[results.Count - 2].Item2 * nonlinearityThreshold))
                {
                    maxConcurrentRequests = mid;
                }
                else
                {
                    minConcurrentRequests = mid;
                }
            }

            string html = CreateHtmlReport(results);
            string reportPath = Path.Combine(clientCachePath, "report.html");
            File.WriteAllText(reportPath, html);
            Log($"Load test report saved to {reportPath}. Ceiling search ended at {maxConcurrentRequests} concurrent requests.");

            // Your assertions about maxConcurrentRequests here.
            // Example: maxConcurrentRequests.Should().BeLessThan(someValue);
        }


        private static async Task<double> MeasureAverageResponseTime(string url, int concurrentRequests, int timeoutSeconds)
        {
            using var playwright = await Playwright.CreateAsync();
            // Same Chromium launch as the 300-page test. Playwright 1.63's default
            // --disable-features list does not include TcpPortRandomizationWin, and that
            // Windows feature fails connect() with WSAENOBUFS (net::ERR_NO_BUFFER_SPACE)
            // while ephemeral ports are still free. Each measurement still opens every
            // page together; this browser is disposed before the next concurrency step.
            await using var browser = await playwright.Chromium.LaunchAsync(ConcurrentRequestsTests.HeadlessChromiumOptions());

            var tasks = new List<Task<double>>();
            var stopwatch = new Stopwatch();

            for (int i = 0; i < concurrentRequests; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var page = await browser.NewPageAsync();
                    stopwatch.Start();
                    var response = await page.GotoAsync(url, new PageGotoOptions
                    {
                        Timeout = timeoutSeconds * 1000,
                        WaitUntil = WaitUntilState.NetworkIdle
                    });
                    stopwatch.Stop();
                    return stopwatch.Elapsed.TotalMilliseconds;
                }));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (TimeoutException)
            {
                return timeoutSeconds * 1000 + 1;
            }

            return tasks.Select(t => t.Result).Average();
        }

        private void Log(string message)
        {
            _output.WriteLine(message);
            Console.WriteLine(message);
        }

        private static string CreateHtmlReport(List<Tuple<int, double>> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<html><head><title>Load Test Results</title></head><body>");
            sb.AppendLine("<h1>Load Test Results</h1>");
            sb.AppendLine("<canvas id=\"myChart\"></canvas>");

            sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");

            sb.AppendLine("<script>");
            sb.AppendLine("const ctx = document.getElementById('myChart').getContext('2d');");
            sb.AppendLine("const myChart = new Chart(ctx, {");
            sb.AppendLine("    type: 'line',");
            sb.AppendLine("    data: {");
            sb.AppendLine($"      labels: [{string.Join(",", results.Select(r => r.Item1))}],");
            sb.AppendLine("        datasets: [{");
            sb.AppendLine("            label: 'Response Time (ms)',");
            sb.AppendLine($"            data: [{string.Join(",", results.Select(r => r.Item2))}],");
            sb.AppendLine("            borderColor: 'rgb(75, 192, 192)',");
            sb.AppendLine("           tension: 0.1");
            sb.AppendLine("        }]");
            sb.AppendLine("    },");
            sb.AppendLine("    options: {");
            sb.AppendLine(" scales: {");
            sb.AppendLine(" y: {");
            sb.AppendLine(" beginAtZero: false");
            sb.AppendLine(" }");
            sb.AppendLine(" }");
            sb.AppendLine(" }");
            sb.AppendLine("});");
            sb.AppendLine("</script>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }
    }
}