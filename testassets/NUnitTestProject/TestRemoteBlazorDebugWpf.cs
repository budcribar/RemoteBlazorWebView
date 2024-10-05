using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteBlazorDebugWpf : TestRemoteBlazorWpf
    {

        public override Process CreateClient(string url, string pid)
        {
            return Utilities.StartRemoteBlazorWpfDebugApp(url, pid);
        }

    }
}