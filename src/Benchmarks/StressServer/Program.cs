using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using PeakSWC.RemoteWebView;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WebdriverTestProject;
using System.Net.Http;
using System.Reflection;

namespace StressServer
{
    internal class Program
    {
        protected static int NUM_LOOPS_WAITING_FOR_PAGE_LOAD = 200;
        protected static string url = "https://192.168.1.35:5002";

        static async Task<bool> PollHttpRequest(HttpClient httpClient, string url)
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                await DoMain(args);
            }
            catch
            {
                Console.ReadLine();
            }
            Console.ReadLine();
        }
        public static async Task DoMain(string[] args)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Console.WriteLine("Extracting Resources...");

            Utilities.ExtractResourcesToExecutionDirectory();
            Console.WriteLine("Extracting Resources Completed");

            //Console.WriteLine("Setting up playwright...");
            //SetupPlaywright();
            //Console.WriteLine("Setting up playwright completed");

            try
            {
                // Changed to High for better compatibility
                Process currentProcess = Process.GetCurrentProcess();
                currentProcess.PriorityClass = ProcessPriorityClass.High;
                Console.WriteLine("Process priority set to High.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set priority: {ex.Message}");
            }

            int totalPasses = 0;
            int totalFailures = 0;


            using var handler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(90),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(60),
                EnableMultipleHttp2Connections = true
            };

            using var httpClient = new HttpClient(handler);

            using var channel = GrpcChannel.ForAddress(url, new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            Logging.SetupEventLog();
            Logging.ClearEventLog();

            // Safely kill existing client processes
            await Utilities.KillProcessesAsync("RemoteBlazorWebViewTutorial.WpfApp");


            Utilities.AddCertificateToLocalMachine("DevCertificate.cer");


            int numClients = 10;
            int numLoops = 10;

            if (args.Length == 2)
            {
                if (!int.TryParse(args[0], out numClients))
                {
                    Console.WriteLine("Invalid number for numClients. Using default value 10.");
                    numClients = 10;
                }
                if (!int.TryParse(args[1], out numLoops))
                {
                    Console.WriteLine("Invalid number for numLoops. Using default value 100.");
                    numLoops = 100;
                }
            }

            Console.WriteLine($"numClients = {numClients} numLoops = {numLoops}");


            await Task.Delay(1000);
            var path = ExecutableManager.ExtractExecutable();

            if (Path.Exists(path))
            {
                Console.WriteLine("Extraction worked");
            }
            else
            {
                Console.WriteLine("Extraction failed. Exiting.");
                return;
            }
            var clientIds = Enumerable.Range(0, numClients).Select(_ => Guid.NewGuid().ToString()).ToList();
            for (int i = 0; i < numLoops; i++)
            {
                var results = await ExecuteLoop(url, httpClient, channel, numClients, path, clientIds);
                totalPasses += results.Item1;
                totalFailures += results.Item2;

                Logging.LogEvent($"Counter Passes: {totalPasses} Fails: {totalFailures}", EventLogEntryType.SuccessAudit);
            }

            // ExecutableManager.CleanUp(path); // Uncomment if cleanup is necessary

            Logging.LogEvent($"Elapsed Time: {stopwatch.Elapsed} Seconds per pass: {stopwatch.Elapsed.TotalSeconds / numLoops}", EventLogEntryType.Warning);
        }

        private static void SetupPlaywright()
        {
            // Set the environment variable to point to the correct .playwright directory
            string playwrightSourcePath = Path.Combine(AppContext.BaseDirectory, "playwright");

            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "..");
            appDataPath = Path.GetFullPath(appDataPath); // Resolves the ".." to get the actual path

            // Append the .playwright folder
            string playwrightTargetPath = Path.Combine(appDataPath, ".playwright");

            //Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_PATH", playwrightTargetPath, EnvironmentVariableTarget.Process);

            try
            {
                Utilities.CopyDirectory(playwrightSourcePath, playwrightTargetPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to copy playwright files {ex.Message}");
                Console.ReadLine();
            }
        }

        private static async Task<(int, int)> ExecuteLoop(string url,HttpClient httpClient, GrpcChannel channel, int numClients, string path, List<string> clientIds)
        {
            await WaitForServerToStart(url, httpClient);

            List<Process> clients = new List<Process>();

            // npx playwright install 
            IPlaywright PlaywrightInstance = await Playwright.CreateAsync();
            IBrowser Browser = await PlaywrightInstance.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false, // Set to true for headless mode
                Args = ["--no-sandbox", "--disable-setuid-sandbox"]
            });

            List<IBrowserContext> BrowserContexts = new();
            List<IPage> Pages = new();

            int passCount = 0;
            int failCount = 0;

            try
            {
                // Use thread-safe collections

               
                Dictionary<string, Process> processDict = new Dictionary<string, Process>();

                foreach (var clientId in clientIds)
                {
                    Process clientProcess = await ExecutableManager.RunExecutableAsync(path, clientId, channel, $"-u={url}", $"-i={clientId}");
                    processDict.Add(clientId, clientProcess);
                }


              

                for (int i = 0; i < numClients; i++)
                {
                    var context = await Browser.NewContextAsync(new BrowserNewContextOptions
                    {
                        IgnoreHTTPSErrors = true,
                        ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
                    });

                    BrowserContexts.Add(context);

                    var page = await context.NewPageAsync();
                    Pages.Add(page);
                }            

                // Transfer from ConcurrentBag to List for further processing
                clients = processDict.Values.ToList();
               

                // Open browser to home page concurrently using Parallel.ForEachAsync
                await Parallel.ForEachAsync(Pages.Select((driver, index) => new { driver, index }), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (item, cancellationToken) =>
                {
                    await item.driver.GotoAsync($"{url}/app/{clientIds[item.index]}");
                });

                //await Task.Delay(3000); // Consider reducing if possible

                // Interact with the page: Click 'Counter' link using Parallel.ForEachAsync
                await Parallel.ForEachAsync(Pages.Select((driver, index) => new { driver, index }), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (item, cancellationToken) =>
                {
                    try
                    {
                        var linkLocator = item.driver.Locator("text=Counter");
                        await linkLocator.ClickAsync();

                        // Wait for the header to appear, indicating navigation
                        var headerLocator = item.driver.Locator("h1:text('Counter')");
                        await headerLocator.WaitForAsync(new LocatorWaitForOptions
                        {
                            State = WaitForSelectorState.Visible,
                            Timeout = 8000
                        });

                    }

                    catch (Exception ex)
                    {
                        Logging.LogEvent($"Unexpected error while clicking 'Counter' link for client {clientIds[item.index]}: {ex.Message}", EventLogEntryType.Error);
                    }
                });

                // Retrieve buttons and paragraphs
                List<ILocator> buttons = new();
                List<ILocator> paragraphs = new();

                for (int i = 0; i< Pages.Count; i++)    
                {
                    try
                    {
                        // Locate the increment button
                        var buttonLocator = Pages[i].Locator(".btn");
                        await buttonLocator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                        buttons.Add(buttonLocator);

                        // Locate the paragraph displaying the count
                        var paragraphLocator = Pages[i].Locator("p");
                        await paragraphLocator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                        paragraphs.Add(paragraphLocator);
                    }
                    catch
                    {
                        Logging.LogEvent("Not all buttons and paragraphs were found. Aborting this loop iteration.", EventLogEntryType.Error);
                        return (passCount, numClients - passCount);
                    }
                    
                };


                int numClicks = 10;
                // Click buttons asynchronously using Parallel.ForEachAsync
                for (int i = 0; i < numClicks; i++)
                {
                    await Parallel.ForEachAsync(Enumerable.Range(0, numClients), new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    }, async (j, cancellationToken) =>
                    {
                        try
                        {
                            await buttons[j].ClickAsync();
                        }
                        catch (Exception ex)
                        {
                            Logging.LogEvent($"Failed to click button for client {clientIds[j]}: {ex.Message}", EventLogEntryType.Error);
                        }
                    });
                }

                // Validate results concurrently using Parallel.ForEachAsync
                await Parallel.ForEachAsync(Enumerable.Range(0, numClients), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (i, cancellationToken) =>
                {
                    try
                    {
                        await Assertions.Expect(paragraphs[i]).ToContainTextAsync($"{numClicks}", new LocatorAssertionsToContainTextOptions { Timeout = 8000 });
                        Interlocked.Increment(ref passCount);
                    }

                    catch {              
                        Interlocked.Increment(ref failCount);
                        Logging.LogEvent($"{numClicks} expected but found {await paragraphs[i].AllTextContentsAsync()}", EventLogEntryType.Error);
                    }
                });

                return (passCount, failCount);
            }
            catch (Exception e)
            {
                Logging.LogEvent($"ExecuteLoop encountered an exception: {e.Message}\n{e.StackTrace}", EventLogEntryType.Error);
                return (passCount, numClients - passCount);
            }
            finally
            {
                foreach (var bc in BrowserContexts)
                    await bc.DisposeAsync();
               
                await Browser.DisposeAsync();
                PlaywrightInstance.Dispose();
                
                foreach (var clientId in clientIds)
                {

                    bool isClientDisconnected = await ExecutableManager.WaitForClientToDisconnectAsync(
                      clientId: clientId,
                      channel: channel,
                      timeoutMs: 10000, // 10 seconds timeout
                      checkIntervalMs: 100 // Check every 100ms
                  );

                    if (!isClientDisconnected)
                    {
                        Logging.LogEvent($"Client process (ID: {clientId}) did not shut down", EventLogEntryType.Error);
                        Environment.Exit(-1);
                    }

                }
            }
        }

        private static async Task WaitForServerToStart(string url, HttpClient httpClient)
        {
            while (true)
            {
                if (await PollHttpRequest(httpClient, url))
                {
                    Console.WriteLine("Server is running");
                    break;
                }
                else
                {
                    Logging.LogEvent("Waiting for server", EventLogEntryType.Error);
                    Console.WriteLine("Waiting for server");
                    await Task.Delay(1000);
                }
            }
        }
    }
}
