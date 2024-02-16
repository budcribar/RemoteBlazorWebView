using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [Ignore]
    [TestClass]
    public class TestRemoteEmbeddedBlazorWebView : TestRemoteBlazorWpf
    {
        public override void Test2Client5Refresh()
        {
            // TODO Remove this to add back
        }
        public override Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWebViewEmbeddedApp();
        }

    }
}