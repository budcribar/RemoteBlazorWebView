// TestLocalBlazorWebView.cs
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    public class TestLocalBlazorWebView : IClassFixture<TestLocalBlazorWebViewFixture>
    {
        private readonly TestLocalBlazorWebViewFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly BlazorTestHelper _helper;

        public TestLocalBlazorWebView(TestLocalBlazorWebViewFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            _helper = new BlazorTestHelper(_fixture.Page, _output);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        public async Task TestClicks(int numClicks)
        {
            await _helper.TestClicks(numClicks);
        }

    }
}
