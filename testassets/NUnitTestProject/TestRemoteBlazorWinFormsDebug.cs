using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteBlazorWinFormsDebug : TestRemoteBlazorForm
    {

        public override Process CreateClient(string url, string pid)
        {
            return Utilities.StartRemoteBlazorWinFormsDebugApp(url, pid);
        }

    }
}