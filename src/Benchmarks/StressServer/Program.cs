using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using PeakSWC.RemoteWebView;
using System.Diagnostics;

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
                // Initialize SemaphoreSlim to limit concurrent initializations
                int maxConcurrentInitializations = Environment.ProcessorCount;
                //using var semaphore = new SemaphoreSlim(maxConcurrentInitializations);
                using var semaphore = new SemaphoreSlim(1);
                // Initialize clients and drivers concurrently
                List<Task> initializationTasks = new List<Task>();
                for (int i = 0; i < numClients; i++)
                {
                    await semaphore.WaitAsync();
                    initializationTasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            var id = Guid.NewGuid().ToString();
                            lock (gids) { gids.Add(id); }

                            var clientProcess = ExecutableManager.RunExecutable(path, $"-u={url}", $"-i={id}");
                            if (clientProcess == null)
                            {
                                Logging.LogEvent($"Failed to start client process for ID {id}.", EventLogEntryType.Error);
                                Interlocked.Increment(ref failCount);
                                return;
                            }
                            lock (clients) { clients.Add(clientProcess); }

                            var chromeOptions = new ChromeOptions
                            {
                                BrowserVersion = "128.0",
                                AcceptInsecureCertificates = true,
                                PageLoadTimeout = TimeSpan.FromMinutes(2)
                            };
                            //chromeOptions.AddArgument("--headless");
                            chromeOptions.AddArgument("--disable-gpu");
                            chromeOptions.AddArgument("--no-sandbox");
                            chromeOptions.AddArgument("--disable-extensions");
                            chromeOptions.AddArgument("--disable-dev-shm-usage");

                            var chromeDriver = new ChromeDriver(chromeOptions);
                            lock (drivers) { drivers.Add(chromeDriver); }
                        }
                        catch (Exception ex)
                        {
                            Logging.LogEvent($"Failed to initialize ChromeDriver: {ex.Message}", EventLogEntryType.Error);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(initializationTasks);

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

                // Open browser to home page concurrently
                List<Task> navigationTasks = new List<Task>();
                for (int i = 0; i < drivers.Count; i++)
                {
                    int driverIndex = i;
                    navigationTasks.Add(Task.Run(() =>
                    {
                        drivers[driverIndex].Url = $"{url}/app/{ids[driverIndex]}";
                    }));
                }
                await Task.WhenAll(navigationTasks);

                await Task.Delay(3000); // Consider reducing if possible

                // Interact with the page: Click 'Counter' link
                List<Task> interactionTasks = new List<Task>();
                for (int i = 0; i < drivers.Count; i++)
                {
                    int driverIndex = i;
                    interactionTasks.Add(Task.Run(async () =>
                    {
                        for (int j = 0; j < NUM_LOOPS_WAITING_FOR_PAGE_LOAD; j++)
                        {
                            try
                            {
                                var link = waits[driverIndex].Until(d => d.FindElement(By.PartialLinkText("Counter")));
                                link?.Click();
                                await Task.Delay(100);
                                break;
                            }
                            catch (WebDriverTimeoutException)
                            {
                                // Element not found yet, retry
                            }
                            catch (Exception ex)
                            {
                                Logging.LogEvent($"Unexpected error while clicking 'Counter' link for client {ids[driverIndex]}: {ex.Message}", EventLogEntryType.Error);
                            }
                            await Task.Delay(100);
                        }
                    }));
                }
                await Task.WhenAll(interactionTasks);

                // Retrieve buttons and paragraphs
                IWebElement[] buttons = new IWebElement[numClients];
                IWebElement[] paras = new IWebElement[numClients];

                for (int i = 0; i < numClients; i++)
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
                        await Task.Delay(100);
                    }

                    if (buttons[i] == null || paras[i] == null)
                    {
                        Logging.LogEvent($"Failed to retrieve elements for client {ids[i]}.", EventLogEntryType.Error);
                        Interlocked.Increment(ref failCount);
                    }
                }

                // Ensure all buttons and paras were found
                if (buttons.Any(b => b == null) || paras.Any(p => p == null))
                {
                    Logging.LogEvent("Not all buttons and paragraphs were found. Aborting this loop iteration.", EventLogEntryType.Error);
                    return (passCount, numClients - passCount);
                }

                int numClicks = 10;
                // Click buttons asynchronously
                for (int i = 0; i < numClicks; i++)
                {
                    List<Task> clickTasks = new List<Task>();
                    for (int j = 0; j < numClients; j++)
                    {
                        int clientIndex = j;
                        clickTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                buttons[clientIndex].Click();
                                await Task.Delay(75);
                            }
                            catch (Exception ex)
                            {
                                Logging.LogEvent($"Failed to click button for client {ids[clientIndex]}: {ex.Message}", EventLogEntryType.Error);
                            }
                        }));
                    }
                    await Task.WhenAll(clickTasks);
                }

                await Task.Delay(numClients * 100); // Consider reducing if possible

                // Validate results concurrently
                List<Task> validationTasks = new List<Task>();
                for (int i = 0; i < numClients; i++)
                {
                    int clientIndex = i;
                    validationTasks.Add(Task.Run(() =>
                    {
                        var res = paras[clientIndex].Text;
                        if (res.Contains($"{numClicks}"))
                            Interlocked.Increment(ref passCount);
                        else
                        {
                            Interlocked.Increment(ref failCount);
                            Logging.LogEvent($"{numClicks} expected but found {res}", EventLogEntryType.Error);
                        }
                    }));
                }
                await Task.WhenAll(validationTasks);

                return (passCount, failCount);
            }
            catch (Exception e)
            {
                Logging.LogEvent($"ExecuteLoop encountered an exception: {e.Message}\n{e.StackTrace}", EventLogEntryType.Error);
                return (passCount, numClients - passCount);
            }
            finally
            {
                // Cleanup ChromeDrivers concurrently
                var driverCleanupTasks = drivers.Select(driver => Task.Run(() =>
                {
                    try
                    {
                        driver.Quit();
                    }
                    catch (Exception ex)
                    {
                        Logging.LogEvent($"Exception on ChromeDriver.Quit(): {ex.Message}", EventLogEntryType.Error);
                    }
                })).ToArray();

                // Cleanup client processes concurrently
                //var clientCleanupTasks = clients.Select(client => Task.Run(() =>
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
                //})).ToArray();

                await Task.WhenAll(driverCleanupTasks);
                //await Task.WhenAll(clientCleanupTasks);
            }
        }
    }
}
