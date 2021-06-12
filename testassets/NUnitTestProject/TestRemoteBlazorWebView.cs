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

namespace WebdriverTestProject
{
    //https://intellitect.com/selenium-chrome-csharp/
    // https://stackoverflow.com/questions/64233124/how-to-attach-a-selenium-chromedriver-to-an-embedded-cefsharp-browser-in-a-wpf-a
    //https://docs.microsoft.com/en-us/microsoft-edge/webdriver-chromium/capabilities-edge-options

    [TestClass]
    public class TestRemoteBlazorWebView: TestRemoteBlazorWpf
    {
       
        public override Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWebViewApp();
        }

        public override void KillClient()
        {
            Utilities.KillRemoteBlazorWebViewApp();
        }

      
    }
}