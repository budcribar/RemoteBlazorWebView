using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using PeakSWC.RemoteWebView;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace StressServer
{
    internal class Program
    {
        protected static int NUM_LOOPS_WAITING_FOR_PAGE_LOAD = 200;

        protected static async Task<List<string>> WaitForClientToConnectAsync(int num, List<string> gids, GrpcChannel channel)
        {
            List<string> ids = new List<string>();
            var client = new WebViewIPC.WebViewIPCClient(channel);
            int count = 0;
            HashSet<string> idsSet = new HashSet<string>();

            do
            {
                try
                {
                    var response = await client.GetIdsAsync(new Empty());
                    idsSet = new HashSet<string>(response.Responses);
                }
                catch (Exception ex)
                {
                    Logging.LogEvent($"gRPC call failed: {ex.Message}", EventLogEntryType.Error);
                    return new List<string>();
                }

                await Task.Delay(1000);
                count++;

                if (count > 20)
                {
                    Logging.LogEvent("Timeout waiting for clients to start", EventLogEntryType.Error);
                    return new List<string>();
                }

            } while (!gids.All(x => idsSet.Contains(x)));

            return gids;
        }

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
            Stopwatch stopwatch = Stopwatch.StartNew();
            Console.WriteLine("Extracting Resources...");

            Utilities.ExtractResourcesToExecutionDirectory();
            Console.WriteLine("Extracting Resources Completed");

            //Utilities.SetWebView2UserDataFolder();

            try
            {
                Process currentProcess = Process.GetCurrentProcess();
                currentProcess.PriorityClass = ProcessPriorityClass.High; // Changed to High for better compatibility
                Console.WriteLine("Process priority set to High.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set priority: {ex.Message}");
            }

            int totalPasses = 0;
            int totalFailures = 0;

            string url = "https://192.168.1.35:5002";
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

            // Safely kill existing client processes
            await Utilities.KillProcessesAsync("RemoteBlazorWebViewTutorial.WpfApp");
            Logging.SetupEventLog();
            Logging.ClearEventLog();

            CertificateInstaller.AddCertificateToLocalMachine("DevCertificate.cer");

            if (await PollHttpRequest(httpClient, url))
            {
                Console.WriteLine("Server is running");
            }
            else
            {
                Logging.LogEvent("Server not running", EventLogEntryType.Error);
                Console.WriteLine("Server not running");
                return;
            }

            int numClients = 10;
            int numLoops = 100;

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


            // 
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

            for (int i = 0; i < numLoops; i++)
            {
                var results = await ExecuteLoop(url, channel, numClients, path);
                totalPasses += results.Item1;
                totalFailures += results.Item2;

                Logging.LogEvent($"Counter Passes: {totalPasses} Fails: {totalFailures}", EventLogEntryType.SuccessAudit);
            }

            // ExecutableManager.CleanUp(path); // Uncomment if cleanup is necessary

           Logging.LogEvent($"Elapsed Time: {stopwatch.Elapsed} Seconds per pass: {stopwatch.Elapsed.TotalSeconds / numLoops}", EventLogEntryType.Warning);
        }

        private static async Task<(int, int)> ExecuteLoop(string url, GrpcChannel channel, int numClients, string path)
        {
            List<Process> clients = new List<Process>();
            List<ChromeDriver> drivers = new List<ChromeDriver>();
            List<string> gids = new List<string>();
            int passCount = 0;
            int failCount = 0;

            try
            {
                // Use thread-safe collections
                ConcurrentBag<Process> clientBag = new ConcurrentBag<Process>();
                ConcurrentBag<ChromeDriver> driverBag = new ConcurrentBag<ChromeDriver>();
                ConcurrentBag<string> gidBag = new ConcurrentBag<string>();

                // Initialize clients and drivers concurrently using Parallel.ForEachAsync
                var clientIds = Enumerable.Range(0, numClients).Select(_ => Guid.NewGuid().ToString()).ToList();

                await Parallel.ForEachAsync(clientIds, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (id, cancellationToken) =>
                {
                    try
                    {
                        Process clientProcess;
                        lock (clientBag)
                        {
                            // Start client process
                            clientProcess = ExecutableManager.RunExecutable(path, $"-u={url}", $"-i={id}");
                        }
                        if (clientProcess == null)
                        {
                            Logging.LogEvent($"Failed to start client process for ID {id}.", EventLogEntryType.Error);
                            Interlocked.Increment(ref failCount);
                            return;
                        }
                        clientBag.Add(clientProcess);

                        // Initialize ChromeDriver
                        var chromeOptions = new ChromeOptions
                        {
                            BrowserVersion = "128.0",
                            AcceptInsecureCertificates = true,
                            PageLoadTimeout = TimeSpan.FromMinutes(2)
                        };
                        chromeOptions.AddArgument("--headless"); // Uncomment for headless mode
                        chromeOptions.AddArgument("--disable-gpu");
                        chromeOptions.AddArgument("--no-sandbox");
                        chromeOptions.AddArgument("--disable-extensions");
                        chromeOptions.AddArgument("--disable-dev-shm-usage");

                        var chromeDriver = new ChromeDriver(chromeOptions);
                        driverBag.Add(chromeDriver);
                    }
                    catch (Exception ex)
                    {
                        Logging.LogEvent($"Failed to initialize ChromeDriver for client {id}: {ex.Message}", EventLogEntryType.Error);
                        // Attempt to kill the client process if it was started
                        var process = clients.FirstOrDefault(p => p.Id.ToString() == id);
                        if (process != null && !process.HasExited)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception killEx)
                            {
                                Logging.LogEvent($"Failed to kill client process {id}: {killEx.Message}", EventLogEntryType.Error);
                            }
                        }
                        Interlocked.Increment(ref failCount);
                    }
                });

                // Transfer from ConcurrentBag to List for further processing
                clients = clientBag.ToList();
                drivers = driverBag.ToList();
                gids = clientIds.ToList();

                // Wait for clients to connect
                var ids = await WaitForClientToConnectAsync(numClients, gids, channel);
                if (ids == null || !gids.All(x => ids.Contains(x)) || clients.Count != numClients || drivers.Count != numClients)
                {
                    Logging.LogEvent("Startup Failed: Not all clients connected.", EventLogEntryType.Error);
                    Console.WriteLine("Startup Failed: Not all clients connected.");
                    return (passCount, numClients - passCount);
                }

                // Initialize WebDriverWait for each driver
                List<WebDriverWait> waits = drivers.Select(d => new WebDriverWait(d, TimeSpan.FromSeconds(10))).ToList();

                // Open browser to home page concurrently using Parallel.ForEachAsync
                await Parallel.ForEachAsync(drivers.Select((driver, index) => new { driver, index }), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (item, cancellationToken) =>
                {
                    item.driver.Url = $"{url}/app/{ids[item.index]}";
                    await Task.CompletedTask; // Placeholder for any asynchronous operations if needed
                });

                await Task.Delay(3000); // Consider reducing if possible

                // Interact with the page: Click 'Counter' link using Parallel.ForEachAsync
                await Parallel.ForEachAsync(drivers.Select((driver, index) => new { driver, index }), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (item, cancellationToken) =>
                {
                    //for (int j = 0; j < NUM_LOOPS_WAITING_FOR_PAGE_LOAD; j++)
                    //{
                        try
                        {
                            var link = waits[item.index].Until(d => d.FindElement(By.PartialLinkText("Counter")));
                            link?.Click();
                            //await Task.Delay(100, cancellationToken);
                            //break;
                        }
                        catch (WebDriverTimeoutException)
                        {
                            // Element not found yet, retry
                        }
                        catch (Exception ex)
                        {
                            Logging.LogEvent($"Unexpected error while clicking 'Counter' link for client {ids[item.index]}: {ex.Message}", EventLogEntryType.Error);
                        }
                        //await Task.Delay(100, cancellationToken);
                    // }
                });

                // Retrieve buttons and paragraphs
                IWebElement[] buttons = new IWebElement[numClients];
                IWebElement[] paras = new IWebElement[numClients];

                // Retrieve elements sequentially or with controlled parallelism
                await Parallel.ForEachAsync(Enumerable.Range(0, numClients), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (i, cancellationToken) =>
                {
                    for (int j = 0; j < NUM_LOOPS_WAITING_FOR_PAGE_LOAD; j++)
                    {
                        try
                        {
                            buttons[i] = drivers[i].FindElement(By.ClassName("btn"));
                            paras[i] = drivers[i].FindElement(By.XPath("//p"));
                            break;
                        }
                        catch (NoSuchElementException)
                        {
                            // Elements not found yet, retry
                        }
                        catch (Exception ex)
                        {
                            Logging.LogEvent($"Unexpected error while retrieving elements for client {ids[i]}: {ex.Message}", EventLogEntryType.Error);
                        }
                        await Task.Delay(100, cancellationToken);
                    }

                    if (buttons[i] == null || paras[i] == null)
                    {
                        Logging.LogEvent($"Failed to retrieve elements for client {ids[i]}.", EventLogEntryType.Error);
                        Interlocked.Increment(ref failCount);
                    }
                });

                // Ensure all buttons and paras were found
                if (buttons.Any(b => b == null) || paras.Any(p => p == null))
                {
                    Logging.LogEvent("Not all buttons and paragraphs were found. Aborting this loop iteration.", EventLogEntryType.Error);
                    return (passCount, numClients - passCount);
                }

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
                            buttons[j].Click();
                            await Task.Delay(75, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Logging.LogEvent($"Failed to click button for client {ids[j]}: {ex.Message}", EventLogEntryType.Error);
                        }
                    });
                }

                await Task.Delay(numClients * 100); // Consider reducing if possible

                // Validate results concurrently using Parallel.ForEachAsync
                await Parallel.ForEachAsync(Enumerable.Range(0, numClients), new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (i, cancellationToken) =>
                {
                    var res = paras[i].Text;
                    if (res.Contains($"{numClicks}"))
                        Interlocked.Increment(ref passCount);
                    else
                    {
                        Interlocked.Increment(ref failCount);
                        Logging.LogEvent($"{numClicks} expected but found {res}", EventLogEntryType.Error);
                    }
                    await Task.CompletedTask; // Placeholder for any asynchronous operations if needed
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
                // Cleanup ChromeDrivers concurrently using Parallel.ForEachAsync
                await Parallel.ForEachAsync(drivers, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, async (driver, cancellationToken) =>
                {
                    try
                    {
                        driver.Quit();
                    }
                    catch (Exception ex)
                    {
                        Logging.LogEvent($"Exception on ChromeDriver.Quit(): {ex.Message}", EventLogEntryType.Error);
                    }
                    await Task.CompletedTask; // Placeholder for any asynchronous operations if needed
                });

                // Cleanup client processes concurrently using Parallel.ForEachAsync
                //await Parallel.ForEachAsync(clients, new ParallelOptions
                //{
                //    MaxDegreeOfParallelism = Environment.ProcessorCount
                //}, async (client, cancellationToken) =>
                //{
                //    try
                //    {
                //        if (!client.HasExited)
                //            client.Kill();
                //    }
                //    catch (Exception ex)
                //    {
                //        Logging.LogEvent($"Unable to kill client process (ID: {client.Id}): {ex.Message}", EventLogEntryType.Error);
                //    }
                //    await Task.CompletedTask; // Placeholder for any asynchronous operations if needed
                //});
            }
        }
    }
}
