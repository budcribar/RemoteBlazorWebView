using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
   [TestClass]
    public class TestRemoteBlazorWebView : TestRemoteBlazorWpf
    {
        public override void Test2Client5Refresh()
        {
           // TODO Remove this to add back
        }
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