using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    public class TestServerWpf : TestRemoteBlazorWpf
    {
        public TestServerWpf(ITestOutputHelper output) : base(output) { }

        //private int BYTES_READ = 928282;
        private int BYTES_READ = 960320;
        
        //private int FILES_READ = 28;
        private int FILES_READ = 29;
        public override async Task Test2Client5Refresh() { await Task.CompletedTask; }
     
        [Fact]
        public override async Task Test1Client()
        {
            await _fixture.TestClient(1);
            await _fixture.VerifyServerStats(1, FILES_READ, BYTES_READ);
            await _fixture.VerifyDisconnect(1, true);
        }

        [Fact]
        public override async Task Test2Client()
        {
            await _fixture.TestClient(2);
            await _fixture.VerifyServerStats(2, FILES_READ, BYTES_READ);
            await _fixture.VerifyDisconnect(2, true);
        }

        [Fact]
        public override async Task Test5Client()
        {
            await _fixture.TestClient(5);
            await _fixture.VerifyServerStats(5,FILES_READ, BYTES_READ);
            await _fixture.VerifyDisconnect(5, true);

        }

        [Fact]
        public async Task Test10Client()
        {
            await _fixture.TestClient(10);
            await _fixture.VerifyServerStats(10, FILES_READ, BYTES_READ);
            await _fixture.VerifyDisconnect(10, true);

        }
    }
}
