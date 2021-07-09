using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebdriverTestProject
{
    [TestClass]
    public class TestLocalBlazorWpf : TestLocalBlazorForm
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWpfAppExe();
        }
    }

    [TestClass]
    public class TestLocalEmbeddedBlazorWpf : TestLocalBlazorForm
    {
        public override string BinaryLocation()
        {
            return Utilities.BlazorWpfAppEmbeddedExe();
        }
    }
}