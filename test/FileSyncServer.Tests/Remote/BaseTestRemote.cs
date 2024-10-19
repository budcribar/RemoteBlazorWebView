// TestRemoteBlazorWpf.cs
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    [Collection("RemoteBlazorWpf Collection")]
    public abstract class BaseTestRemote<T> : IAsyncLifetime where T : BaseTestRemoteFixture, new()
    {
        protected readonly T _fixture = new T();
        protected readonly ITestOutputHelper Output;

        public BaseTestRemote(ITestOutputHelper output)
        {
            Output = output;
        }

        [Fact]
        public async virtual Task Test2Client5Refresh()
        {
            await _fixture.TestRefresh(2, 5);
        }

        [Fact]
        public async virtual Task Test1Client()
        {
            await _fixture.TestClient(1);
        }

        [Fact]
        public async virtual Task Test2Client()
        {
            await _fixture.TestClient(2);
        }

        [Fact]
        public async virtual Task Test5Client()
        {
            await _fixture.TestClient(5);
        }

        public virtual async Task InitializeAsync()
        {
            await _fixture.InitializeAsync();
        }

        public async Task DisposeAsync()
        {
            await _fixture.DisposeAsync();
        }
    }
}
