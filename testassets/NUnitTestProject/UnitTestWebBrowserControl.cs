using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using System.Threading;
using System.Diagnostics;
using System;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestWebBrowserControl
    {
        private static EdgeDriver driver;
       
        [TestInitialize]
        public void Setup()
        {
           driver = new EdgeDriver(new EdgeOptions { UseWebView=true, BinaryLocation = Utilities.BlazorWinFormsAppExe() });
          
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

        [Ignore("Too long")]
        [TestMethod]
        public void Test1000Clicks()
        {
            TestClient(1000);
        }

        public void TestClient(int numClicks)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            var button = driver.FindElementByTagName("button");
            var para = driver.FindElement(By.XPath("//p"));

            sw.Restart();
           
            for (int i = 0; i < numClicks; i++)
            {
                button.Click();
                Thread.Sleep(30);
            }

            Console.WriteLine($"Click {numClicks} times in {sw.Elapsed}");
            Thread.Sleep(1000);

            var res = para.Text;
            Assert.IsTrue(res.Contains($"{numClicks}"));

            Cleanup();
        }

        [TestCleanup]
        public void Cleanup()
        {
            driver.Dispose();
        }
    }
}