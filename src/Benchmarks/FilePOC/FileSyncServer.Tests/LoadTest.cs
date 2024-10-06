using Microsoft.Playwright;
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using FileSyncServer; // Make sure you have this NuGet package installed

[Collection("Server collection")]
public class LoadTest
{
    private readonly ServerFixture _serverFixture;
    private readonly ClientFixture _clientFixture; // Assuming you have a ClientFixture
    private readonly string clientCachePath;
    public LoadTest(ClientFixture clientFixture, ServerFixture serverFixture)
    {
        _serverFixture = serverFixture;
        _clientFixture = clientFixture;

        // Determine the path to the client's cache directory
        var testDirectory = Directory.GetCurrentDirectory();

        // Ensure test files exist in the client's cache directory under the specific clientId
        clientCachePath = Path.Combine(testDirectory, "client_cache");
        Directory.CreateDirectory(clientCachePath);
        var filePath = Path.Combine(clientCachePath, $"maxconcurrenttest.txt");

        StringBuilder fileContent = new StringBuilder();

        for (int i = 1; i <= 1000; i++)
        {
            fileContent.AppendLine($"This is line {i} of test.txt");
        }

        File.WriteAllText(filePath, fileContent.ToString());

        // Verify that both Client and Server processes are running
        Process.GetProcessesByName("Client").Length.Should().BeGreaterThan(0, "Client process should be running.");
        Process.GetProcessesByName("RemoteWebViewService").Length.Should().BeGreaterThan(0, "Server process should be running.");
    }



    [Fact]
    public async Task FindMaxConcurrentRequests()
    {
        double nonlinearityThreshold = 1.5;
        int timeoutSeconds = 30;
        string url = Utility.BASE_URL;
        var clientId = _clientFixture.ClientId;
        url = $"{url}/{clientId}/maxconcurrenttest.txt"; // Example using test1.txt

        int minConcurrentRequests = 1;
        int maxConcurrentRequests = 1;
        double previousTime = 0;

        await Utility.SetServerCache(true);
        List<Tuple<int, double>> results = new List<Tuple<int, double>>();

        while (true)
        {
            double averageTime = await MeasureAverageResponseTime(url, maxConcurrentRequests, timeoutSeconds);

            results.Add(Tuple.Create(maxConcurrentRequests, averageTime));

            if (averageTime > timeoutSeconds * 1000 || (previousTime > 0 && averageTime > previousTime * nonlinearityThreshold))
            {
                // Timeout occurred or time increased non-linearly
                break;
            }

            previousTime = averageTime;
            minConcurrentRequests = maxConcurrentRequests;
            maxConcurrentRequests *= 2; // Exponential increase in concurrency
        }

        // Binary search to refine the max concurrent requests (optional, but improves accuracy)
        while (maxConcurrentRequests - minConcurrentRequests > 1)
        {
            int mid = (minConcurrentRequests + maxConcurrentRequests) / 2;
            double midTime = await MeasureAverageResponseTime(url, mid, timeoutSeconds);
            results.Add(Tuple.Create(mid, midTime));

            if (midTime > timeoutSeconds * 1000 || (previousTime > 0 && midTime > previousTime * nonlinearityThreshold))
            {
                maxConcurrentRequests = mid;
            }
            else
            {
                minConcurrentRequests = mid;
                previousTime = midTime;
            }
        }

        string html = CreateHtmlReport(results);
   
        File.WriteAllText(Path.Combine(clientCachePath, "report.html"), html);
    }




    private async Task<double> MeasureAverageResponseTime(string url, int concurrentRequests, int timeoutSeconds)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });


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
                    Timeout = timeoutSeconds * 1000, // Timeout in milliseconds
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
            // Handle timeout
            return timeoutSeconds * 1000 + 1; // Return a value indicating timeout
        }

        return tasks.Select(t => t.Result).Average();
    }




    private string CreateHtmlReport(List<Tuple<int, double>> results)
    {

        var sb = new StringBuilder();
        sb.AppendLine("<html><head><title>Load Test Results</title></head><body>");
        sb.AppendLine("<h1>Load Test Results</h1>");
        sb.AppendLine("<canvas id=\"myChart\"></canvas>");



        sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>"); // Include Chart.js


        sb.AppendLine("<script>");
        sb.AppendLine("const ctx = document.getElementById('myChart').getContext('2d');");
        sb.AppendLine("const myChart = new Chart(ctx, {");
        sb.AppendLine("    type: 'line',");
        sb.AppendLine("    data: {");


        sb.AppendLine($"      labels: [{string.Join(",", results.Select(r => r.Item1))}],"); // Requests



        sb.AppendLine("        datasets: [{");
        sb.AppendLine("            label: 'Response Time (ms)',");
        sb.AppendLine($"            data: [{string.Join(",", results.Select(r => r.Item2))}],"); // Time
        sb.AppendLine("            borderColor: 'rgb(75, 192, 192)',");
        sb.AppendLine("           tension: 0.1");

        sb.AppendLine("        }]");
        sb.AppendLine("    },");
        sb.AppendLine("    options: {");
        sb.AppendLine("        scales: {");
        sb.AppendLine("            y: {");
        sb.AppendLine("                beginAtZero: false");  // Allow Y axis to start at non-zero values
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("});");
        sb.AppendLine("</script>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }




}