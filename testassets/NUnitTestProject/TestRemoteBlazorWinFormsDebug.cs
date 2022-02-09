using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteBlazorWinFormsDebug : TestRemoteBlazorForm
    {

        public override Process CreateClient()
        {
            return Utilities.StartRemoteBlazorWinFormsDebugApp();
        }

    }
}