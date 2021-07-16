using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Edge;
using System.IO;
using System.Threading;
using PeakSWC.RemoteableWebView;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Generic;
using System.Diagnostics;
using System;
using OpenQA.Selenium.Chrome;
using System.Linq;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestServer : TestRemoteBlazorWpf
    {
        protected override void TestClient(int num)
        {
            Startup(num);

            Stopwatch sw = new();
            sw.Start();

            Assert.AreEqual(num, _driver.Count);

            _driver[0].Url = "https:localhost:443";

            List<string> links = new();
            while (sw.ElapsedMilliseconds < 30000)
            {
                links = _driver[0].FindElements(By.TagName("a")).Select(x => x.GetAttribute("href")).Where(x => x?.Contains("/app/") ?? false).ToList();
                if (links.Count == num) break;
            }

            Assert.IsTrue(sw.ElapsedMilliseconds < 30000);

            Console.WriteLine($"Navigate home in {sw.Elapsed}");
 
            for (int i = 0; i < num; i++) _driver[i].Url = links[i];

            Thread.Sleep(1000);

            for (int i = 0; i < num; i++)
            {
                var link = _driver[i].FindElement(By.PartialLinkText("Counter"));

                link.Click();
            }

            Console.WriteLine($"Navigate to counter in {sw.Elapsed}");

            Thread.Sleep(1000);

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
        }

    }
}