using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    [Collection("TestRemoteBlazorWpf")]
    public class TestRemoteBlazorWpf : BaseTestRemote<RemoteBlazorWpfFixture> { public TestRemoteBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class RemoteBlazorWpfFixture : BaseTestRemoteFixture { public RemoteBlazorWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfApp; } }
}
