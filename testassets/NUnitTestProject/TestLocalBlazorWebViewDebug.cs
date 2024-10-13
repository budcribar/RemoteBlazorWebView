using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalBlazorWebViewDebug : TestLocalBlazorWebView
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWebViewAppExe();
        }
    }
}