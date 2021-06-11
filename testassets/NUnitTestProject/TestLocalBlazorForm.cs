using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using System.Threading;
using System.Diagnostics;
using System;
using System.IO;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalBlazorForm
    {
        private static EdgeDriver driver;
        private static string startingDirectory;

        public virtual string BinaryLocation()
        {
            return Utilities.BlazorWinFormsAppExe();
        }

        [TestInitialize]
        public void Setup()
        {
            startingDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.GetDirectoryName(BinaryLocation()));
            driver = new EdgeDriver(new EdgeOptions { UseWebView = true, BinaryLocation = Path.GetFileName(this.BinaryLocation()) });
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

            var link = driver.FindElementByPartialLinkText("Counter");
            link.Click();
            Thread.Sleep(100);

            var button = driver.FindElement(By.ClassName("btn"));
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
            Directory.SetCurrentDirectory(startingDirectory);

            driver.Quit();
        }
    }
}