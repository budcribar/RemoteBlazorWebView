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

    [Collection("TestRemoteBlazorDebugWpf")]
    public class TestRemoteBlazorDebugWpf : BaseTestRemote<RemoteBlazorWpfFixture> { public TestRemoteBlazorDebugWpf(ITestOutputHelper output) : base(output) { } }
    public class RemoteBlazorDebugWpfFixture : BaseTestRemoteFixture { public RemoteBlazorDebugWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfDebugApp; } }


    [Collection("TestRemoteBlazorForm")]
    public class TestRemoteBlazorForm : BaseTestRemote<RemoteBlazorWpfFixture> { public TestRemoteBlazorForm(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteBlazorFormFixture : BaseTestRemoteFixture { public TestRemoteBlazorFormFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWinFormsApp; } }

    [Collection("TestRemoteBlazorWinFormsDebug")]
    public class TestRemoteBlazorWinFormsDebug : BaseTestRemote<RemoteBlazorWpfFixture> { public TestRemoteBlazorWinFormsDebug(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteBlazorWinFormsDebugFixture : BaseTestRemoteFixture { public TestRemoteBlazorWinFormsDebugFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWinFormsDebugApp; } }

    [Collection("TestRemoteEmbeddedBlazorForm")]
    public class TestRemoteEmbeddedBlazorForm : BaseTestRemote<RemoteBlazorWpfFixture> { public TestRemoteEmbeddedBlazorForm(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteEmbeddedBlazorFormFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorFormFixture() { ClientExecutablePath = Utilities.StartRemoteEmbeddedBlazorWinFormsApp; } }

    [Collection("TestRemoteEmbeddedBlazorWpf")]
    public class TestRemoteEmbeddedBlazorWpf : BaseTestRemote<RemoteBlazorWpfFixture> { public TestRemoteEmbeddedBlazorWpf(ITestOutputHelper output) : base(output) { } }
    public class TestRemoteEmbeddedBlazorWpfFixture : BaseTestRemoteFixture { public TestRemoteEmbeddedBlazorWpfFixture() { ClientExecutablePath = Utilities.StartRemoteBlazorWpfEmbeddedApp; } }


    
}
