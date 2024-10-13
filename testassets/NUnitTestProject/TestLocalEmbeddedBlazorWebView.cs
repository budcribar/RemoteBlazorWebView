using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalEmbeddedBlazorWebView : TestLocalBlazorWebView
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWebViewAppEmbeddedExe();
        }
    }
}