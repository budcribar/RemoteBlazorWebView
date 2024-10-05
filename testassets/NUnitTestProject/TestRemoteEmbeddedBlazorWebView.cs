using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteEmbeddedBlazorWebView : TestRemoteBlazorWpf
    {
        public override void Test2Client5Refresh()
        {
            // TODO Remove this to add back
        }
        public override Process CreateClient(string url, string pid)
        {
            return Utilities.StartRemoteBlazorWebViewEmbeddedApp(url,pid);
        }

    }
}