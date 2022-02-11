using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalEmbeddedBlazorWebView : TestLocalBlazorForm
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWebViewAppEmbeddedExe();
        }
    }
}