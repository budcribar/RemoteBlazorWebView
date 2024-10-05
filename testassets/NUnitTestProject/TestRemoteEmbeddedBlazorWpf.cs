using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteEmbeddedBlazorWpf : TestRemoteBlazorWpf
    {
        public override Process CreateClient(string url, string pid)
        {
            return Utilities.StartRemoteBlazorWpfEmbeddedApp(url,pid);
        }

    }
}