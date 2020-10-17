using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using System.Collections.Generic;
using System.Linq;
//using NUnitTests;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Firefox;
using System.Threading;
using Grpc.Net.Client;
using PeakSwc.RemoteableWebWindows;
using Google.Protobuf.WellKnownTypes;
using System.Diagnostics;
using System.IO;
using Google.Protobuf;
using System;

namespace NUnitTestProject
{
    [TestClass]
    public class Tests
    {
        private static List<ChromeDriver> _driver = new List<ChromeDriver>();
        private readonly string url = @"https://localhost/";
        private static GrpcChannel channel;
        private static string[] ids;
        private static Process process;
        private static List<Process> clients;

        public static void Startup(int numClients)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "RemotableWebWindowService")?.Kill();
            var relative = @"..\..\..\..\..\src\RemoteableWebWindowService";
            var executable = @"bin\debug\netcoreapp3.1\RemoteableWebWindowService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), relative, executable);

            process = new Process();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.UseShellExecute = true;
           
            process.Start();
            var ids = new RemoteWebWindow.RemoteWebWindowClient(channel).GetIds(new Empty());


            Console.WriteLine($"Started server in {sw.Elapsed}");


            relative = @"..\..\..\..\..\testassets\BlazorWebViewTutorial.WpfApp";
            var exePath = @"bin\debug\netcoreapp3.1";
            executable = "BlazorWebViewTutorial.WpfApp.exe";
            f = Path.Combine(Directory.GetCurrentDirectory(), relative, exePath, executable);

            Process.GetProcesses().Where(p => p.ProcessName == "BlazorWebViewTutorial.WpfApp").ToList().ForEach(x => x.Kill());


            clients = new List<Process>();

            sw.Restart();
            for (int i=0;i<numClients; i++)
            {
                Process p = new Process();
                p.StartInfo.FileName = Path.GetFullPath(f);
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.Arguments = @"https://localhost:443";
                p.StartInfo.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
                
                p.Start();
                clients.Add(p);

                
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


        private static void StartClient(int num)
        {
            var client = new RemoteWebWindow.RemoteWebWindowClient(channel);

            do
            {
                ids = client.GetIds(new Empty()).Responses.ToArray();
                Thread.Sleep(10);
            } while (ids.Count() != num);
           
        }


        [TestInitialize]
        public void Setup()
        {
            var options = new ChromeOptions
            {
                PageLoadStrategy = PageLoadStrategy.Normal
            };
            //var driver = new EdgeDriver("C:\\Windows\\System32\\", options);
           
            channel = GrpcChannel.ForAddress(url) ;

           
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

        [TestMethod]
        public void Test10Client()
        {
            TestClient(10);
        }

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

        private void TestClient(int num)
        {
            Startup(num);
           

            //Assert.AreEqual("Demo", _driver.Title);

            //var element = _driver.FindElement(By.XPath("//button"));

            //element.Click();

            Stopwatch sw = new Stopwatch();
            sw.Start();

            Assert.AreEqual(num, _driver.Count());

            //_driver.AsParallel().WithDegreeOfParallelism(3).Select((w, i) => new { Index = i, Driver = w }).ForAll(x => x.Driver.Url = url + $"app?guid={ids[x.Index]}"); 

            for (int i=0; i<num; i++) _driver[i].Url = url + $"app?guid={ids[i]}";
            Console.WriteLine($"Navigate home in {sw.Elapsed}");

            Thread.Sleep(1000);
            sw.Restart();

            //_driver.AsParallel().ForAll(x => x.ExecuteScript("window['Blazor'].navigateTo('/counter', false);", null));

            for (int i = 0; i < num; i++) _driver[i].ExecuteScript("window['Blazor'].navigateTo('/counter', false);", null);
            Console.WriteLine($"Navigate to counter in {sw.Elapsed}");

            Thread.Sleep(1000);

            List<IWebElement> button = new List<IWebElement>();
            List<IWebElement> para = new List<IWebElement>();

            for (int i = 0; i < num; i++)
            {
                button.Add(_driver[i].FindElement(By.ClassName("btn")));
                para.Add(_driver[i].FindElement(By.XPath("//p")));
            }

            sw.Restart();
            int numClicks = 10;
            for (int i = 0; i < numClicks; i++)
            {
                for (int j=0; j<num; j++)
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

            Cleanup();

           
        }

        public void Cleanup()
        {
            try {
                process?.Kill();
            } catch (Exception) { }

            try
            {
                clients.ForEach(x => x.Kill());
            }
            catch (Exception) { }

            _driver.ForEach(x => x.Dispose());
        }
    }
}