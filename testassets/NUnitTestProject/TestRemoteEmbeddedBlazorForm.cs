using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteEmbeddedBlazorForm : TestRemoteBlazorForm
    {

        public override Process CreateClient()
        {
            return Utilities.StartRemoteEmbeddedBlazorWinFormsApp();
        }

    }
}