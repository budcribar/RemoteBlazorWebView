using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.DevTools.V126.DOM;
using OpenQA.Selenium.Edge;
using PeakSWC.RemoteWebView;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using static System.Net.WebRequestMethods;

namespace StressServer
{
   
    internal class Program
    {
        protected static int NUM_LOOPS_WAITING_FOR_PAGE_LOAD = 200;
        protected static List<string> WaitForClientToConnect(int num, List<string> gids, GrpcChannel channel)
        {
            List<string> ids = new List<string>();
            var client = new WebViewIPC.WebViewIPCClient(channel);
            int count = 0;
            do
            {
                ids = client.GetIds(new Empty()).Responses.ToList();
                Thread.Sleep(1000);
                count++;

                if (count > 20)
                {
                    Logging.LogEvent("Timeout waiting for clients to start", EventLogEntryType.Error);
                    return new();
                }
              
            } while (!gids.All(x => ids.Contains(x)));
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
        protected static List<ChromeDriver> _driver = new();
        
        static List<string> gids = new();
        public static async Task Main(string[] args)
        {
            
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
                Console.ReadKey();
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
                totalPasses = ExecuteLoop(totalPasses, url, channel, numClients, path);
            }
           
            //ExecutableManager.CleanUp(path);
            Console.ReadKey();
        }

        private static int ExecuteLoop(int totalPasses, string url, GrpcChannel channel, int numClients, string path)
        {
            gids.Clear();

            List<Process> clients = new List<Process>();

            for (int i = 0; i < numClients; i++)
            {
                var id = Guid.NewGuid().ToString();
                gids.Add(id);

                clients.Add(ExecutableManager.RunExecutable(path, $"-u={url}", $"-i={id}" ));

                _driver.Add(new(new ChromeOptions { BrowserVersion = "128.0", AcceptInsecureCertificates=true, PageLoadTimeout=TimeSpan.FromMinutes(2) }));
            }

            var ids = WaitForClientToConnect(numClients, gids, channel);
            if (clients.Count != ids.Count)
            {
                // Startup failed
                Logging.LogEvent("Startup Failed", EventLogEntryType.Error);
                Console.WriteLine("Startup Failed");
                Console.ReadKey();
                return -1;
            }

            // open browser to home page
            Parallel.For(0, numClients, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i => { _driver[i].Url = url + $"/app/{ids[i]}"; });

            Thread.Sleep(3000);


            Parallel.For(0, numClients, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                for (int j = 0; j < NUM_LOOPS_WAITING_FOR_PAGE_LOAD; j++)
                {
                    try
                    {
                        var link = _driver[i].FindElement(By.PartialLinkText("Counter"));
                        link?.Click();
                        Thread.Sleep(100);
                        break;
                    }
                    catch (Exception) {
                       
                    }
                    Thread.Sleep(100);
                }
            });

            List<IWebElement> button = new();
            List<IWebElement> para = new();

            for (int i = 0; i < numClients; i++)
            {
                for (int j = 0; j < NUM_LOOPS_WAITING_FOR_PAGE_LOAD; j++)
                {
                    try
                    {
                        IWebElement buttonElement = _driver[i].FindElement(By.ClassName("btn"));
                        IWebElement paraElement = _driver[i].FindElement(By.XPath("//p"));

                        button.Add(buttonElement);
                        para.Add(paraElement);
                        break;
                    }
                    catch (Exception)
                    {

                    }
                    Thread.Sleep(100);
                }
            }

            int numClicks = 10;
            for (int i = 0; i < numClicks; i++)
            {
                Parallel.For(0, numClients, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, j =>
                {
                    button[j].Click();
                    Thread.Sleep(numClients * 75);
                });

            }
            Thread.Sleep(numClients * 100);
            int passCount = 0;
            Parallel.For(0, numClients, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                var res = para[i].Text;
                if (res.Contains($"{numClicks}")) passCount++;
                else
                {
                    Logging.LogEvent($"{numClicks} not found in {res}", EventLogEntryType.Error);
                }
            });

            if (passCount == numClients)
            {
                totalPasses += numClients;
                Logging.LogEvent($"Counter successful {totalPasses}", EventLogEntryType.SuccessAudit);
            }
            else
            {
                Logging.LogEvent("Counter clicks not as expected", EventLogEntryType.Error);
            }

            _driver.ForEach(x => x.Quit());
            _driver.Clear();
            return totalPasses;
        }
    }
}
