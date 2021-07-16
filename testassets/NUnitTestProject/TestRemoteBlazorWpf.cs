using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using PeakSWC.RemoteableWebView;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace WebdriverTestProject
{
    //https://intellitect.com/selenium-chrome-csharp/
    // https://stackoverflow.com/questions/64233124/how-to-attach-a-selenium-chromedriver-to-an-embedded-cefsharp-browser-in-a-wpf-a
    //https://docs.microsoft.com/en-us/microsoft-edge/webdriver-chromium/capabilities-edge-options

    [TestClass]
    public class TestRemoteBlazorWpf
    {
        protected static readonly List<ChromeDriver> _driver = new();
        private readonly string url = @"https://localhost/";
        protected static GrpcChannel? channel;
        private static string[] ids = Array.Empty<string>();
        protected static Process? process;
        protected static List<Process> clients = new();

        public virtual Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWpfApp();
        }

        public virtual void KillClient()
        {
            Utilities.KillRemoteBlazorWpfApp();
        }

        public virtual void Startup(int numClients)
        {
            KillClient();

            process = Utilities.StartServer();
            
            var ids = new RemoteWebWindow.RemoteWebWindowClient(channel).GetIds(new Empty());
            Assert.AreEqual(0, ids.Responses.Count);

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
            var client = new RemoteWebWindow.RemoteWebWindowClient(channel);

            do
            {
                ids = client.GetIds(new Empty()).Responses.ToArray();
                Thread.Sleep(200);
            } while (ids.Length != num);

        }


        [TestInitialize]
        public void Setup()
        {
            channel = GrpcChannel.ForAddress(url);
        }

        [TestMethod]
        public void Test1Client()
        {
            TestClient(1);
        }

        [TestMethod]
        public void Test2Client()
        {
            TestClient(2);
        }

        [Ignore("Not enough resources")]
        [TestMethod]
        public void Test10Client()
        {
            TestClient(10);
        }

        [TestMethod]
        public void Test5Client()
        {
            TestClient(5);
        }

        [Ignore("Not enough resources")]
        [TestMethod]
        public void Test50Client()
        {
            TestClient(50);

            // 37 minutes
            // 10 failed
            // bootstrap.min.css + site.css 5
            // // bootstrap.min.css + site.css + GET ERR_ABORTED 404 2

            // ERR_HTTP2_PING_FAILED webindow.BrowserIPC/ReceiveMessage  1


            // Slow network detected on index.html (but passed)
        }

        protected virtual void TestClient(int num)
        {
            Startup(num);

            Stopwatch sw = new();
            sw.Start();

            Assert.AreEqual(num, _driver.Count);

            for (int i = 0; i < num; i++) _driver[i].Url = url + $"app/{ids[i]}";
            Console.WriteLine($"Navigate home in {sw.Elapsed}");

            Thread.Sleep(1000);
            sw.Restart();

          

            // TODO Why the slow down
            for (int i = 0; i < num; i++)
            {
                for (int j = 0; j < 100; j++)
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
            Assert.AreEqual(num, passCount);

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