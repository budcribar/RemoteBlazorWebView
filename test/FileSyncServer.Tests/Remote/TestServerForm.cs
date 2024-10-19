using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    public class TestServerForm : TestRemoteBlazorForm
    {
        public TestServerForm(ITestOutputHelper output) : base(output) { }

        //private int BYTES_READ = 927858
        private int BYTES_READ = 959896;
        
        //private int FILES_READ = 24;

        private int FILES_READ = 25;

        public override async Task Test2Client5Refresh() { await Task.CompletedTask; }
     
        [Fact]
        public override async Task Test1Client()
        {
            await _fixture.TestClient(1);
            await _fixture.VerifyServerStats(1, FILES_READ, BYTES_READ);
            await _fixture.VerifyDisconnect(1, false);
        }

        [Fact]
        public override async Task Test2Client()
        {
            await _fixture.TestClient(2);
            await _fixture.VerifyServerStats(2, FILES_READ, BYTES_READ);
            await _fixture.VerifyDisconnect(2, false);

        }

        [Fact]
        public override async Task Test5Client()
        {
            await _fixture.TestClient(5);
            await _fixture.VerifyServerStats(5,FILES_READ, BYTES_READ);
            await _fixture.VerifyDisconnect(5, false);

        }
    }
}
