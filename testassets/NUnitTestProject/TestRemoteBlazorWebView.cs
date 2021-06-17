using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
    //https://intellitect.com/selenium-chrome-csharp/
    // https://stackoverflow.com/questions/64233124/how-to-attach-a-selenium-chromedriver-to-an-embedded-cefsharp-browser-in-a-wpf-a
    //https://docs.microsoft.com/en-us/microsoft-edge/webdriver-chromium/capabilities-edge-options

    [TestClass]
    public class TestRemoteBlazorWebView : TestRemoteBlazorWpf
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