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
        private ChromeDriver _driver;
        private readonly string url = @"https://localhost/";
        private GrpcChannel channel;
        private string[] ids;
        private static Process process;
        private static Process process2;

        public static void Startup()
        {


            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "RemotableWebWindowService")?.Kill();
            var relative = @"..\..\..\..\..\src\RemoteableWebWindowService";
            var executable = @"bin\debug\netcoreapp3.1\RemoteableWebWindowService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), relative, executable);

            process = new Process();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.UseShellExecute = true;
            //process.StartInfo.Arguments = "http://localhost:62799";
            //process.StartInfo.WorkingDirectory = relative;

            

            process.Start();
            Thread.Sleep(1000);


            relative = @"..\..\..\..\..\testassets\BlazorWebViewTutorial.WpfApp";
            var exePath = @"bin\debug\netcoreapp3.1";
            executable = "BlazorWebViewTutorial.WpfApp.exe";
            f = Path.Combine(Directory.GetCurrentDirectory(), relative, exePath, executable);

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "BlazorWebViewTutorial.WpfApp")?.Kill();
            process2 = new Process();
            process2.StartInfo.FileName = Path.GetFullPath(f);
            process2.StartInfo.UseShellExecute = false;
            process2.StartInfo.Arguments = @"https://localhost:443";
            process2.StartInfo.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);

            process2.Start();
            Thread.Sleep(1000);
            Thread.Sleep(2000);

        }


        private void StartClient()
        {
            var client = new RemoteWebWindow.RemoteWebWindowClient(channel);

            do
            {
                ids = client.GetIds(new Empty()).Responses.ToArray();
            } while (ids.Count() == 0);
           
        }


        [TestInitialize]
        public void Setup()
        {
            var options = new ChromeOptions
            {
                PageLoadStrategy = PageLoadStrategy.Normal
            };
            //var driver = new EdgeDriver("C:\\Windows\\System32\\", options);
            _driver = new ChromeDriver(options);
            channel = GrpcChannel.ForAddress(url) ;

            Startup();
        }

        [TestMethod]
        public void Test1()
        {
            StartClient();
            // _driver.Url = url;


            //Assert.AreEqual("Demo", _driver.Title);

            //var element = _driver.FindElement(By.XPath("//button"));

            //element.Click();

            _driver.Url = url + $"app?guid={ids[0]}";

            //_driver.Url = url + "counter";

            Thread.Sleep(1000);

            _driver.ExecuteScript("window['Blazor'].navigateTo('/counter', false);", null);

            Thread.Sleep(1000);

            var element = _driver.FindElement(By.XPath("//button"));

            element = _driver.FindElement(By.ClassName("btn"));

            for (int i=0; i<150; i++)
            {
                element.Click();
                Thread.Sleep(10);
            }
                

            var para = _driver.FindElement(By.XPath("//p"));

            Thread.Sleep(1000);

            var res = para.Text;

            Assert.IsTrue(res.Contains("150"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try {
                process?.Kill();
            } catch (Exception) { }

            try
            {
                process2?.Kill();
            }
            catch (Exception) { }

            _driver.Dispose();
        }
    }
}