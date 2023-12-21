using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalBlazorForm
    {
        private static EdgeDriver? driver;
        private static string startingDirectory = string.Empty;
        private static int NUM_LOOPS_WAITING_FOR_PAGE_LOAD = 100;
        public virtual string BinaryLocation()
        {
            return Utilities.BlazorWinFormsAppExe();
        }

        [TestInitialize]
        public void Setup()
        {
            //Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--remote-debugging-port=9222");
            var webview2 = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "", "WebView2");
            //Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", webview2);
            //Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webview2);

            startingDirectory = Directory.GetCurrentDirectory();
            var binary = BinaryLocation();
            //Directory.SetCurrentDirectory(Path.GetDirectoryName(BinaryLocation()) ?? string.Empty);
            var options = new EdgeOptions
            {
                UseWebView = true,
                BinaryLocation = BinaryLocation()
            };
            options.SetLoggingPreference("performance", LogLevel.All);
         
            driver = new EdgeDriver(options);
       
          
            // Wait for page to load 
            Thread.Sleep(1000);
        }

        [TestMethod]
        public void Test10Clicks()
        {
            TestClient(10);
        }

        [TestMethod]
        public void Test100Clicks()
        {
            TestClient(100);
        }

        public void TestClient(int numClicks)
        {
            Stopwatch sw = new();
            sw.Start();

            for (int i=0;i< NUM_LOOPS_WAITING_FOR_PAGE_LOAD; i++)
            {
                try
                {
                    var link = driver?.FindElement(By.PartialLinkText("Counter"));
                    link?.Click();
                    Thread.Sleep(100);
                    break;
                }
                catch (Exception) { }
                Thread.Sleep(100);
            }
            

            var button = driver?.FindElement(By.ClassName("btn"));
            var para = driver?.FindElement(By.XPath("//p"));

            sw.Restart();

            for (int i = 0; i < numClicks; i++)
            {
                button?.Click();
                Thread.Sleep(30);
            }

            Console.WriteLine($"Click {numClicks} times in {sw.Elapsed}");
            Thread.Sleep(1000);

            var res = para?.Text;
            Assert.IsTrue(res?.Contains($"{numClicks}"));

            Cleanup();
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.SetCurrentDirectory(startingDirectory);

            driver?.Quit();
        }
    }
}