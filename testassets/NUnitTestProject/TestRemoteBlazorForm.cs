using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
    //https://intellitect.com/selenium-chrome-csharp/
    // https://stackoverflow.com/questions/64233124/how-to-attach-a-selenium-chromedriver-to-an-embedded-cefsharp-browser-in-a-wpf-a
    //https://docs.microsoft.com/en-us/microsoft-edge/webdriver-chromium/capabilities-edge-options

    [TestClass]
    public class TestRemoteBlazorForm : TestRemoteBlazorWpf
    {

        public override Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWinFormsApp();
        }

        public override void KillClient()
        {
            Utilities.KillBlazorWinFormsApp();
        }
        public override int CountClients()
        {
            return Utilities.CountRemoteBlazorWinFormsApp();
        }
    }

    //[TestClass]
    //public class TestRemotePackageBlazorForm : TestRemoteBlazorForm
    //{

    //    public override Process StartServer()
    //    {
    //        return Utilities.StartServerFromPackage();
    //    }
    //}


    // This is now failing
    //[TestClass]
    //public class TestRemoteBlazorDebugForm : TestRemoteBlazorForm
    //{

    //    public override Process CreateClient()
    //    {
    //        return Utilities.StartRemoteBlazorWinFormsDebugApp();
    //    }

    //}

    [TestClass]
    public class TestRemoteEmbeddedBlazorForm : TestRemoteBlazorForm
    {

        public override Process CreateClient()
        {
            return Utilities.StartRemoteEmbeddedBlazorWinFormsApp();
        }

    }
}