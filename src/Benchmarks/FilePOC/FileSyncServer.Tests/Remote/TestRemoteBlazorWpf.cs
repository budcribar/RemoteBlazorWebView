// TestRemoteBlazorWpf.cs
using System.Threading.Tasks;
using Xunit;

namespace WebdriverTestProject
{
    [Collection("RemoteBlazorWpf Collection")]
    public class TestRemoteBlazorWpf : IClassFixture<RemoteBlazorWpfFixture>
    {
        private readonly RemoteBlazorWpfFixture _fixture;

        public TestRemoteBlazorWpf(RemoteBlazorWpfFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Test2Client5Refresh()
        {
            await _fixture.TestRefresh(2, 5);
        }

        [Fact]
        public async Task Test1Client()
        {
            await _fixture.TestClient(1);
        }

        [Fact]
        public async Task Test2Client()
        {
            await _fixture.TestClient(2);
        }

        [Fact]
        public async Task Test5Client()
        {
            await _fixture.TestClient(5);
        }
    }
}
