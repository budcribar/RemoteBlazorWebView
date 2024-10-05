using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemoteEmbeddedBlazorForm : TestRemoteBlazorForm
    {

        public override Process CreateClient(string url, string id)
        {
            return Utilities.StartRemoteEmbeddedBlazorWinFormsApp(url,id);
        }

    }
}