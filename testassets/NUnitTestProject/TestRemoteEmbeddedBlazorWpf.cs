using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteEmbeddedBlazorWpf : TestRemoteBlazorWpf
    {
        public override Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWpfEmbeddedApp();
        }

    }
}