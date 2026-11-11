using System;
using System.Collections.Generic;

namespace PeakSWC.RemoteWebView
{
    /// <summary>One browser connection in a shared session, as the relay reports it.</summary>
    /// <param name="ClientId">The connection's id (one per open page).</param>
    /// <param name="IsOwner">The owner's own page (the app's window in a shared session).</param>
    /// <param name="HasControl">This connection's input reaches the app.</param>
    /// <param name="UserName">The signed-in name when the relay requires sign-in; otherwise empty.</param>
    public sealed record Participant(string ClientId, bool IsOwner, bool HasControl, string UserName);

    /// <summary>
    /// Raised whenever someone joins or leaves a shared session or control moves. <see cref="Participants"/> is the whole
    /// roster in join order: the first connection that is not the owner is the remote user, any later ones are read-only
    /// viewers. Compare with the previous roster to see what changed.
    /// </summary>
    public class ParticipantsChangedEventArgs(Guid id, IReadOnlyList<Participant> participants) : EventArgs
    {
        public Guid Id { get; } = id;
        public IReadOnlyList<Participant> Participants { get; } = participants;
    }
}
