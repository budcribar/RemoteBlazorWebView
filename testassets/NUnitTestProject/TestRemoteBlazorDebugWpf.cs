using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteBlazorDebugWpf : TestRemoteBlazorWpf
    {

        public override Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWpfDebugApp();
        }

    }
}