
using Xunit.Abstractions;
using WebdriverTestProject;

namespace FileSyncServer.Tests
{
    public class TestMisc
    {
        private readonly ITestOutputHelper _output;

        // Constructor to inject ITestOutputHelper
        public TestMisc(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestJavascriptCompiledForRelease()
        {
            // Assuming Utilities.JavascriptFile is a valid file path
            FileInfo fi = new FileInfo(Utilities.JavascriptFile);
            var max = 402 * 1024; // 402 KB in bytes

            // Assert that the file size is less than the maximum allowed size
            Assert.True(fi.Length < max, $"{Path.GetFileName(Utilities.JavascriptFile)} >= {max} bytes");

            // Output the file size in kilobytes
            _output.WriteLine($"{Path.GetFileName(Utilities.JavascriptFile)} is {fi.Length / 1024} KB long");
        }
    }
}
