extern alias client;

using ClientRemote = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

/// <summary>The relay's "participants:" roster as the app reads it for ParticipantsChanged.</summary>
public class ParticipantRosterTests
{
    [Fact]
    public void Reads_each_connection_in_order()
    {
        var roster = ClientRemote.ParticipantRoster.Parse(
            """[{"id":"o","owner":true,"primary":false,"name":""},{"id":"r","owner":false,"primary":true,"name":"Pat Lee"}]""");

        Assert.Equal(2, roster.Count);
        Assert.Equal(new ClientRemote.Participant("o", true, false, ""), roster[0]);
        Assert.Equal(new ClientRemote.Participant("r", false, true, "Pat Lee"), roster[1]);
    }

    [Fact]
    public void A_roster_from_an_older_relay_has_no_names()
    {
        var roster = ClientRemote.ParticipantRoster.Parse("""[{"id":"a","owner":false,"primary":true}]""");

        Assert.Equal(new ClientRemote.Participant("a", false, true, ""), Assert.Single(roster));
    }

    [Fact]
    public void An_empty_session_has_an_empty_roster() =>
        Assert.Empty(ClientRemote.ParticipantRoster.Parse("[]"));
}
