using Microsoft.VisualStudio.TestTools.UnitTesting;
//using NUnitTests;
using System.Diagnostics;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestRemotePackageBlazorForm : TestRemoteBlazorForm
    {

        public override Process StartServer()
        {
            return Utilities.StartServerFromPackage();
        }
    }
}