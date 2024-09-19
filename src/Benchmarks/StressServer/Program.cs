using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
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
                currentProcess.PriorityClass = ProcessPriorityClass.RealTime;
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
          
            Process.GetProcesses().Where(p => p.ProcessName == "RemoteBlazorWebViewTutorial.WpfApp").ToList().ForEach(x => x.Kill());

            int numClients = 10;
            int numLoops = 100;

            if (args.Count() == 2)
            {
                int.TryParse(args[0], out numClients);
                int.TryParse(args[1], out numLoops);
            }

           

            Console.WriteLine($"numClients = {numClients} numLoops = {numLoops}");

            var path = ExecutableManager.ExtractExecutable();

            if (Path.Exists(path))
            {
                Console.WriteLine("Extraction worked");
            }

            for (int i = 0; i < numLoops; i++) {
                var results = await ExecuteLoop(url, channel, numClients, path);
                totalPasses += results.Item1;
                totalFailures += results.Item2;

                Logging.LogEvent($"Counter Passes: {totalPasses} Fails: {totalFailures}", EventLogEntryType.SuccessAudit);
            }

            //ExecutableManager.CleanUp(path);

            Logging.LogEvent($"Elapsed Time: {stopwatch.Elapsed} Seconds per pass: {stopwatch.Elapsed.TotalSeconds/numLoops}",EventLogEntryType.Warning);
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
                for (int i = 0; i < numClients; i++)
                {
                    var id = Guid.NewGuid().ToString();
                    gids.Add(id);

                    var clientProcess = ExecutableManager.RunExecutable(path, $"-u={url}", $"-i={id}");
                    
                    clients.Add(clientProcess);

                    try
                    {
                        var chromeDriver = new ChromeDriver(new ChromeOptions
                        {
                            BrowserVersion = "128.0",
                            AcceptInsecureCertificates = true,
                            PageLoadTimeout = TimeSpan.FromMinutes(2)
                        });
                        drivers.Add(chromeDriver);
                    }
                    catch (Exception ex)
                    {
                        Logging.LogEvent($"Failed to initialize ChromeDriver for client {id}: {ex.Message}", EventLogEntryType.Error);
                        clientProcess.Kill();
                        failCount++;
                    }
                }

                var ids = await WaitForClientToConnectAsync(numClients, gids, channel);
                if (ids == null || !gids.All(x => ids.Contains(x)) || gids.Count != numClients || clients.Count != numClients || drivers.Count != numClients)
                {
                    Logging.LogEvent("Startup Failed: Not all clients connected.", EventLogEntryType.Error);
                    Console.WriteLine("Startup Failed: Not all clients connected.");
                    return (passCount, numClients - passCount);
                }

                // Open browser to home page
                Parallel.For(0, drivers.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
                {
                    drivers[i].Url = $"{url}/app/{ids[i]}";
                });

                await Task.Delay(3000);

                // Interact with the page
                Parallel.For(0, drivers.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async i =>
                {
                    for (int j = 0; j < NUM_LOOPS_WAITING_FOR_PAGE_LOAD; j++)
                    {
                        try
                        {
                            var link = drivers[i].FindElement(By.PartialLinkText("Counter"));
                            link?.Click();
                            await Task.Delay(100);
                            break;
                        }
                        catch (NoSuchElementException)
                        {
                            // Element not found yet, retry
                        }
                        catch (Exception ex)
                        {
                            Logging.LogEvent($"Unexpected error while clicking 'Counter' link for client {ids[i]}: {ex.Message}", EventLogEntryType.Error);
                        }
                        await Task.Delay(100);
                    }
                });

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
                        failCount++;
                    }
                }

                // Ensure all buttons and paras were found
                if (buttons.Any(b => b == null) || paras.Any(p => p == null))
                {
                    Logging.LogEvent("Not all buttons and paragraphs were found. Aborting this loop iteration.", EventLogEntryType.Error);
                    return (passCount, numClients - passCount);
                }

                int numClicks = 10;
                for (int i = 0; i < numClicks; i++)
                {
                    Parallel.For(0, numClients, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async j =>
                    {
                        try
                        {
                            buttons[j].Click();
                            await Task.Delay(75); // Consider using Task.Delay if refactoring to async
                        }
                        catch (Exception ex)
                        {
                            Logging.LogEvent($"Failed to click button for client {ids[j]}: {ex.Message}", EventLogEntryType.Error);
                        }
                    });
                }

                await Task.Delay(numClients * 100);

                // Validate results
                Parallel.For(0, numClients, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
                {
                    var res = paras[i].Text;
                    if (res.Contains($"{numClicks}"))
                        Interlocked.Increment(ref passCount);
                    else
                    {
                        Interlocked.Increment(ref failCount);
                        Logging.LogEvent($"{numClicks} expected but found {res}", EventLogEntryType.Error);
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
                foreach (var driver in drivers)
                {
                    try
                    {
                        driver.Quit();
                    }
                    catch (Exception ex)
                    {
                        Logging.LogEvent($"Exception on ChromeDriver.Quit(): {ex.Message}", EventLogEntryType.Error);
                    }
                }
                drivers.Clear();

                foreach (var client in clients)
                {
                    try
                    {
                        if (!client.HasExited)
                            client.Kill();
                    }
                    catch (Exception ex)
                    {
                        Logging.LogEvent($"Unable to kill client process (ID: {client.Id}): {ex.Message}", EventLogEntryType.Error);
                    }
                }
            }
        }

    }
}
