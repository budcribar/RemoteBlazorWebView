using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    public class TestLocalBlazorEmbeddedForm : IClassFixture<TestLocalBlazorEmbeddedFormFixture>
    {
        private readonly TestLocalBlazorEmbeddedFormFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly BlazorTestHelper _helper;

        public TestLocalBlazorEmbeddedForm(TestLocalBlazorEmbeddedFormFixture fixture, ITestOutputHelper output)
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
