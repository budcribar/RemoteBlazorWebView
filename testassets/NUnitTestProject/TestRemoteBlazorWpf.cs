using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using PeakSWC.RemoteWebView;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WebdriverTestProject
{
    //https://intellitect.com/selenium-chrome-csharp/
    // https://stackoverflow.com/questions/64233124/how-to-attach-a-selenium-chromedriver-to-an-embedded-cefsharp-browser-in-a-wpf-a
    //https://docs.microsoft.com/en-us/microsoft-edge/webdriver-chromium/capabilities-edge-options

    [TestClass]
    public class TestRemoteBlazorWpf
    {
        protected static readonly List<ChromeDriver> _driver = new();
        private readonly string url = @"https://localhost:5001/";
        protected static GrpcChannel? channel;
        private static string[] ids = Array.Empty<string>();
        protected static Process? process;
        protected static List<Process> clients = new();
        private static int NUM_LOOPS_WAITING_FOR_PAGE_LOAD = 100;

        public virtual Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWpfApp();
        }

        public virtual void KillClient()
        {
            Utilities.KillRemoteBlazorWpfApp();
        }

        public virtual Process StartServer()
        {
            return Utilities.StartServer();
        }

        public async virtual Task Startup(int numClients)
        {
            Assert.AreEqual(0, _driver.Count, "_driver has not been cleared out at startup");
            KillClient();

            process = StartServer();
            
            for(int i=0; i < 10; i++)
			{
                // Wait for server to spin up
                try
				{
                    var ids = new WebViewIPC.WebViewIPCClient(channel).GetIds(new Empty());
                    Assert.AreEqual(0, ids.Responses.Count, "Server has connections at startup");
                    break;
                }
                catch (Exception ){}
                await Task.Delay(1000);
            }

            clients = new List<Process>();

            Stopwatch sw = new();
            for (int i = 0; i < numClients; i++)
            {
                clients.Add(CreateClient());
            }

            StartClient(numClients);
            Console.WriteLine($"Clients started in {sw.Elapsed}");

            sw.Restart();
            for (int i = 0; i < numClients; i++)
            {
                _driver.Add(new ChromeDriver());
            }

            Console.WriteLine($"Browsers started in {sw.Elapsed}");
        }


        protected static void StartClient(int num)
        {
            var client = new WebViewIPC.WebViewIPCClient(channel);
            int count = 0;
            do
            {
                ids = client.GetIds(new Empty()).Responses.ToArray();
                Thread.Sleep(1000);
                count++;
                Assert.IsTrue(count < 20, $"Timed out waiting to start {num} clients found {ids.Length}");
            } while (ids.Length != num);

        }


        [TestInitialize]
        public void Setup()
        {
            channel = GrpcChannel.ForAddress(url);
        }

        //[TestMethod]
        //public void Test1Client1Refresh()
        //{
        //    TestRefresh(1,1);
        //}

        //[TestMethod]
        //public void Test1Client10Refresh()
        //{
        //    TestRefresh(1, 10);
        //}
        //[TestMethod]
        //public void Test5Client1Refresh()
        //{
        //    TestRefresh(1, 10);
        //}
        [TestMethod]
        public void Test5Client10Refresh()
        {
            TestRefresh(1, 10);
        }


        [TestMethod]
        public async Task Test1Client()
        {
            await TestClient(1);
        }

        [TestMethod]
        public async Task Test2Client()
        {
            await TestClient(2);
        }

        [TestMethod]
        public async Task Test5Client()
        {
           await TestClient(5);
        }

        protected async virtual void TestRefresh(int numClients, int numRefreshes)
        {
            await Startup(numClients);

            Stopwatch sw = new();
            sw.Start();

            Assert.AreEqual(numClients, _driver.Count, $"Was not able to create expected {numClients} _drivers");

            for (int i = 0; i < numClients; i++) _driver[i].Url = url + $"app/{ids[i]}";
            Console.WriteLine($"Navigate home in {sw.Elapsed}");

            Thread.Sleep(3000);
            sw.Restart();

            for (int k = 0; k < numRefreshes; k++)
            {
                for (int i = 0; i < numClients; i++)
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
                        catch (Exception) { }
                        Thread.Sleep(100);
                    }
                }

                Console.WriteLine($"Navigate to counter in {sw.Elapsed}");

                sw.Restart();

                // Test refresh
                for (int i = 0; i < numClients; i++) _driver[i].Navigate().Refresh();

                for (int i = 0; i < numClients; i++)
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
                        catch (Exception) { }
                        Thread.Sleep(100);
                    }
                }

                for (int i = 0; i < numClients; i++)
                {
                    Assert.IsNotNull(_driver[i].FindElement(By.PartialLinkText("Counter")));
                }
            }     
        }

        protected virtual async Task TestClient(int num)
        {
            await Startup(num);

            Stopwatch sw = new();
            sw.Start();

            Assert.AreEqual(num, _driver.Count, $"Was not able to create expected {num} _drivers");

            for (int i = 0; i < num; i++) _driver[i].Url = url + $"app/{ids[i]}";
            Console.WriteLine($"Navigate home in {sw.Elapsed}");

            Thread.Sleep(3000);
            sw.Restart();

            for (int i = 0; i < num; i++)
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
                    catch (Exception) { }
                    Thread.Sleep(100);
                }
            }

            Console.WriteLine($"Navigate to counter in {sw.Elapsed}");

            Thread.Sleep(3000);

            List<IWebElement> button = new();
            List<IWebElement> para = new();

            for (int i = 0; i < num; i++)
            {
                button.Add(_driver[i].FindElement(By.ClassName("btn")));
                para.Add(_driver[i].FindElement(By.XPath("//p")));
            }

            sw.Restart();
            int numClicks = 10;
            for (int i = 0; i < numClicks; i++)
            {
                for (int j = 0; j < num; j++)
                {
                    button[j].Click();
                    Thread.Sleep(30);
                }

            }

            Console.WriteLine($"Click {numClicks} times in {sw.Elapsed}");


            Thread.Sleep(1000);
            int passCount = 0;
            for (int i = 0; i < num; i++)
            {
                var res = para[i].Text;
                if (res.Contains($"{numClicks}")) passCount++;

            }
            Assert.AreEqual(num, passCount, $"Did not get {num} counts");

            //Cleanup();
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                process?.Kill();
            }
            catch (Exception) { }

            try
            {
                clients.ForEach(x => x.Kill());
            }
            catch (Exception) { }
            try
            {
                _driver.ForEach(x => x.Quit());
            }
            catch (Exception) { }
            _driver.Clear();
        }
    }

    [TestClass]
    public class TestRemoteEmbeddedBlazorWpf : TestRemoteBlazorWpf
    {
        public override Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWpfEmbeddedApp();
        }

    }
}