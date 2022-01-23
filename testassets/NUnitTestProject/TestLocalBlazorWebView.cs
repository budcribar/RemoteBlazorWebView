using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalBlazorWebView : TestLocalBlazorForm
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWebViewAppExe();
        }
    }

    //[TestClass]
    //public class TestLocalEmbeddedBlazorWebView : TestLocalBlazorForm
    //{
    //    public override string BinaryLocation()
    //    {
    //        return Utilities.BlazorWpfAppEmbeddedExe();
    //    }
    //}
}