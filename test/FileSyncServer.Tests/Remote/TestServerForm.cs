using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace FileSyncServer.Tests.Remote
{
    public class TestServerForm(ITestOutputHelper output) : TestRemoteBlazorForm(output)
    {
        private readonly int FILES_READ = 25;

        public override async Task Test2Client5Refresh() { await Task.CompletedTask; }
     
        [WindowsOnlyFact]
        public override async Task Test1Client()
        {
            await _fixture.TestClient(1);
            await _fixture.VerifyServerStats(1, FILES_READ, isWpf: false);
            await _fixture.VerifyDisconnect(1, false);
        }

        [WindowsOnlyFact]
        public override async Task Test2Client()
        {
            await _fixture.TestClient(2);
            await _fixture.VerifyServerStats(2, FILES_READ, isWpf: false);
            await _fixture.VerifyDisconnect(2, false);

        }

        [WindowsOnlyFact]
        public override async Task Test5Client()
        {
            await _fixture.TestClient(5);
            await _fixture.VerifyServerStats(5, FILES_READ, isWpf: false);
            await _fixture.VerifyDisconnect(5, false);

        }
    }
}
